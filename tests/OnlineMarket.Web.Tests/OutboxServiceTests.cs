using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Http;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class OutboxServiceTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public OutboxServiceTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task TwoWorkersCannotClaimTheSameMessage()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using (var arrangeContext = database.CreateContext())
        {
            arrangeContext.OutboxMessages.Add(CreateMessage());
            await arrangeContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext(enableRetryOnFailure: true);
        await using var secondContext = database.CreateContext(enableRetryOnFailure: true);
        var firstStore = new SqlServerOutboxStore(firstContext);
        var secondStore = new SqlServerOutboxStore(secondContext);
        var now = DateTime.UtcNow;

        var claims = await Task.WhenAll(
            firstStore.ClaimAsync(1, "worker-one", now),
            secondStore.ClaimAsync(1, "worker-two", now));

        Assert.Single(claims.SelectMany(batch => batch));
    }

    [Fact]
    public async Task RetryingExecutionStrategyRecordsSuccessfulDeliveryAfterClaimCommit()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext(enableRetryOnFailure: true);
        context.OutboxMessages.Add(CreateMessage());
        await context.SaveChangesAsync();

        var processingWasVisible = false;
        var transactionWasOpenDuringDelivery = true;
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            transactionWasOpenDuringDelivery = context.Database.CurrentTransaction is not null;
            await using var verification = database.CreateContext();
            processingWasVisible = await verification.OutboxMessages.AnyAsync(
                message =>
                    message.Status == OutboxStatus.Processing
                    && message.LockedAtUtc != null
                    && message.LockedBy != null,
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(context, handler);

        await service.ProcessPendingMessagesAsync();

        Assert.True(processingWasVisible);
        Assert.False(transactionWasOpenDuringDelivery);
        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.Processed, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Null(message.LockedAtUtc);
        Assert.Null(message.LockedBy);
    }

    [Fact]
    public async Task RetryableApplicationFailureSchedulesDurableRetry()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext(enableRetryOnFailure: true);
        context.OutboxMessages.Add(CreateMessage());
        await context.SaveChangesAsync();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """{"error":{"code":"Recommendation.ProductSnapshotMissing","retryable":true,"message":"not persisted"}}""")
            }));

        await CreateService(context, handler).ProcessPendingMessagesAsync();

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.Retrying, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Recommendation.ProductSnapshotMissing", message.LastErrorCode);
        Assert.NotNull(message.NextAttemptAtUtc);
        Assert.Equal("Downstream delivery returned HTTP 409.", message.LastError);
        Assert.DoesNotContain("not persisted", message.LastError);
        Assert.Null(message.LockedAtUtc);
        Assert.Null(message.LockedBy);
    }

    [Fact]
    public async Task RetryableTransportFailureSchedulesDurableRetry()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext(enableRetryOnFailure: true);
        context.OutboxMessages.Add(CreateMessage());
        await context.SaveChangesAsync();
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Sensitive downstream detail."));

        await CreateService(context, handler).ProcessPendingMessagesAsync();

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.Retrying, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Delivery.TransportFailure", message.LastErrorCode);
        Assert.Equal("Downstream delivery failed.", message.LastError);
        Assert.NotNull(message.NextAttemptAtUtc);
        Assert.Null(message.LockedAtUtc);
        Assert.Null(message.LockedBy);
    }

    [Fact]
    public async Task NonRetryableApplicationFailureIsStoredAsPermanent()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext(enableRetryOnFailure: true);
        context.OutboxMessages.Add(CreateMessage());
        await context.SaveChangesAsync();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """{"code":"Idempotency.PayloadConflict","retryable":false}""")
            }));

        await CreateService(context, handler).ProcessPendingMessagesAsync();

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.FailedPermanent, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Idempotency.PayloadConflict", message.LastErrorCode);
        Assert.Null(message.NextAttemptAtUtc);
        Assert.Null(message.LockedAtUtc);
        Assert.Null(message.LockedBy);
    }

    [Fact]
    public async Task DeliveryResultRequiresWorkerOwnership()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext(enableRetryOnFailure: true);
        context.OutboxMessages.Add(CreateMessage());
        await context.SaveChangesAsync();
        var store = new SqlServerOutboxStore(context);
        var claimed = Assert.Single(await store.ClaimAsync(1, "owning-worker", DateTime.UtcNow));

        await store.RecordDeliveryResultAsync(new(
            claimed.Id,
            "different-worker",
            true,
            false,
            null,
            null,
            DateTime.UtcNow));

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.Processing, message.Status);
        Assert.Equal("owning-worker", message.LockedBy);
        Assert.Equal(0, message.AttemptCount);
    }

    [Fact]
    public async Task DeliveryResultPreservesRowVersionConcurrencyAndReplayIdempotency()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var staleContext = database.CreateContext();
        staleContext.OutboxMessages.Add(CreateMessage());
        await staleContext.SaveChangesAsync();

        await using var workerContext = database.CreateContext(enableRetryOnFailure: true);
        var store = new SqlServerOutboxStore(workerContext);
        var claimed = Assert.Single(await store.ClaimAsync(1, "worker", DateTime.UtcNow));
        var staleMessage = await staleContext.OutboxMessages.SingleAsync();
        var result = new OnlineMarket.Web.Application.Models.OutboxDeliveryResultDto(
            claimed.Id,
            "worker",
            true,
            false,
            null,
            null,
            DateTime.UtcNow);

        await store.RecordDeliveryResultAsync(result);
        await store.RecordDeliveryResultAsync(result);

        workerContext.ChangeTracker.Clear();
        var processedMessage = await workerContext.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxStatus.Processed, processedMessage.Status);
        Assert.Equal(1, processedMessage.AttemptCount);

        staleMessage.LastError = "stale write";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
    }

    [Fact]
    public async Task InvalidOutboxPayloadIsRejectedBySqlJsonConstraint()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var message = CreateMessage();
        message.Payload = "not-json";
        context.OutboxMessages.Add(message);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static OutboxService CreateService(
        OnlineMarketDbContext context,
        HttpMessageHandler handler)
    {
        return new OutboxService(
            new SqlServerOutboxStore(context),
            new HttpOutboxDispatcher(
                new StubHttpClientFactory(handler),
                NullLogger<HttpOutboxDispatcher>.Instance),
            NullLogger<OutboxService>.Instance);
    }

    private static OutboxMessage CreateMessage()
    {
        var now = DateTime.UtcNow;
        return new OutboxMessage
        {
            EventId = Guid.NewGuid(),
            EventType = "ProductSnapshotChangedV1",
            Destination = "Recommendation.Api",
            AggregateType = "Product",
            AggregateId = Guid.NewGuid(),
            Payload = "{}",
            Status = OutboxStatus.Pending,
            OccurredAtUtc = now,
            AvailableAtUtc = now,
            CorrelationId = Guid.NewGuid()
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://test.invalid")
            };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> responseFactory;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }
}
