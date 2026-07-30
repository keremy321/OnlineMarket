using System.Collections.Concurrent;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Domain.Enums;
using ErpIntegration.Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Tests;

[Collection(IntegrationSqlServerCollection.CollectionName)]
public sealed class IntegrationWorkerMockErpEndToEndTests(
    IntegrationSqlServerFixture fixture)
{
    [Fact]
    public async Task Worker_completes_all_steps_in_order_against_mock_erp()
    {
        await using var integrationDatabase =
            await fixture.CreateDatabaseAsync();
        await using var mockErpDatabase =
            await MockErpWorkerTestDatabase.CreateAsync(
                integrationDatabase.ConnectionString);
        var request = IntegrationApiTestData.CreateValidRequest();
        var item = request.Items![0]!;
        await mockErpDatabase.SeedStockAsync(
            item.ProductId,
            item.Sku!,
            item.ProductName!,
            20);
        await IntegrationWorkerTestSupport.AcceptAsync(
            integrationDatabase,
            request);

        const string apiKey = "worker-test-api-key";
        using var factory = new MockErpWorkerWebApplicationFactory(
            mockErpDatabase.ConnectionString,
            apiKey);
        var recordingHandler = new RecordingIntegrationHandler(
            factory.Server.CreateHandler(),
            integrationDatabase);
        using var httpClient = new HttpClient(recordingHandler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new CollectingLoggerProvider()));
        var loggerProvider = loggerFactory
            .CreateLogger<MockErpClient>();
        var client = new MockErpClient(
            httpClient,
            Options.Create(new MockErpClientOptions
            {
                BaseAddress = "http://localhost/",
                ApiKey = apiKey,
                Timeout = TimeSpan.FromSeconds(5),
                FastRetryCount = 0,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30)
            }),
            new MockErpCircuitBreaker(),
            TimeProvider.System,
            loggerProvider);

        for (var index = 0; index < 4; index++)
        {
            await using var context = integrationDatabase.CreateContext();
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(context, client)
                .ProcessNextAsync($"end-to-end-worker-{index}"));
        }

        await using (var noWorkContext =
            integrationDatabase.CreateContext())
        {
            Assert.False(await IntegrationWorkerTestSupport
                .CreateProcessor(noWorkContext, client)
                .ProcessNextAsync("end-to-end-worker-finished"));
        }

        await using (var integrationContext =
            integrationDatabase.CreateContext())
        {
            var batch = await integrationContext.IntegrationBatches
                .AsNoTracking()
                .Include(value => value.Steps)
                .SingleAsync();
            var steps = batch.Steps
                .OrderBy(value => value.SequenceNumber)
                .ToArray();
            var attempts = await integrationContext.IntegrationAttempts
                .AsNoTracking()
                .OrderBy(value => value.StartedAtUtc)
                .ToArrayAsync();
            var customerLink = await integrationContext.ErpCustomerLinks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(IntegrationBatchStatus.Succeeded, batch.Status);
            Assert.Null(batch.CurrentStepType);
            Assert.All(
                steps,
                step => Assert.Equal(
                    IntegrationStepStatus.Succeeded,
                    step.Status));
            Assert.Equal([1, 1, 1, 1], steps.Select(step => step.AttemptCount));
            Assert.Equal(4, attempts.Length);
            Assert.All(
                attempts,
                attempt => Assert.Equal(
                    IntegrationResultType.Succeeded,
                    attempt.ResultType));
            Assert.All(
                attempts,
                attempt => Assert.Equal(1, attempt.AttemptNumber));
            Assert.Equal(request.Customer!.CustomerId, customerLink.CustomerId);
            Assert.StartsWith("CARI-", customerLink.ErpCustomerCode);
            Assert.All(
                attempts,
                attempt =>
                {
                    Assert.DoesNotContain(
                        request.Customer.Email!,
                        attempt.RequestPayloadMasked ?? string.Empty,
                        StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        request.Address!.PhoneNumber!,
                        attempt.RequestPayloadMasked ?? string.Empty,
                        StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        request.Address.AddressLine1!,
                        attempt.RequestPayloadMasked ?? string.Empty,
                        StringComparison.Ordinal);
                });
        }

        await using (var mockContext = mockErpDatabase.CreateContext())
        {
            Assert.Equal(1, await mockContext.ErpCustomers.CountAsync());
            Assert.Equal(1, await mockContext.ErpOrders.CountAsync());
            Assert.Equal(1, await mockContext.ErpOrderAddresses.CountAsync());
            Assert.Equal(1, await mockContext.ErpOrderLines.CountAsync());
            Assert.Equal(1, await mockContext.ErpStockMovements.CountAsync());
            Assert.Equal(
                18,
                await mockContext.ErpStocks
                    .Select(stock => stock.Quantity)
                    .SingleAsync());
            var accounting = await mockContext.ErpAccountingEntries
                .AsNoTracking()
                .Include(value => value.Lines)
                .SingleAsync();
            Assert.Equal(accounting.TotalDebit, accounting.TotalCredit);
            Assert.Equal(
                ["120", "600", "391"],
                accounting.Lines
                    .OrderBy(line => line.SequenceNumber)
                    .Select(line => line.AccountCode));
        }

        var recorded = recordingHandler.Requests.ToArray();
        Assert.Equal(4, recorded.Length);
        Assert.Equal(
            [
                "/api/v1/customers/ensure",
                "/api/v1/orders",
                "/api/v1/stock-movements",
                "/api/v1/accounting-entries"
            ],
            recorded.Select(value => value.Path));
        Assert.Equal(
            [
                $"customer:{request.OrderId}",
                $"order:{request.OrderId}",
                $"stock:{request.OrderId}",
                $"accounting:{request.OrderId}"
            ],
            recorded.Select(value => value.IdempotencyKey));
        Assert.All(recorded, value => Assert.Equal(apiKey, value.ApiKey));
        Assert.All(
            recorded,
            value => Assert.Equal(
                request.CorrelationId.ToString("D"),
                value.CorrelationId));
        Assert.All(recorded, value => Assert.True(value.ClaimWasCommitted));

        var logs = CollectingLoggerProvider.Messages;
        Assert.DoesNotContain(apiKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            request.Customer.Email!,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            request.Address!.PhoneNumber!,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            request.Address.AddressLine1!,
            logs,
            StringComparison.Ordinal);
    }

    private sealed class RecordingIntegrationHandler(
        HttpMessageHandler innerHandler,
        IntegrationTestDatabase integrationDatabase)
        : DelegatingHandler(innerHandler)
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var idempotencyKey = request.Headers
                .GetValues(MockErpClient.IdempotencyHeaderName)
                .Single();
            await using var context = integrationDatabase.CreateContext();
            var claimWasCommitted = await context.IntegrationSteps
                .AsNoTracking()
                .AnyAsync(
                    step => step.IdempotencyKey == idempotencyKey
                        && step.Status == IntegrationStepStatus.InProgress
                        && step.LockedAtUtc != null
                        && step.LockedBy != null,
                    cancellationToken);
            Requests.Enqueue(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers
                    .GetValues(MockErpClient.ApiKeyHeaderName)
                    .Single(),
                idempotencyKey,
                request.Headers
                    .GetValues(MockErpClient.CorrelationHeaderName)
                    .Single(),
                claimWasCommitted));
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        string Path,
        string ApiKey,
        string IdempotencyKey,
        string CorrelationId,
        bool ClaimWasCommitted);

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private static readonly ConcurrentQueue<string> Entries = new();

        public static string Messages => string.Join(
            Environment.NewLine,
            Entries);

        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger();
        }

        public void Dispose()
        {
        }

        private sealed class CollectingLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Enqueue(formatter(state, exception));
            }
        }
    }
}
