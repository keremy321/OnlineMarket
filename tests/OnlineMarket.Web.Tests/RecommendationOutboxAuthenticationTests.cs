using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Http;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class RecommendationOutboxAuthenticationTests(
    OnlineMarketSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-outbox-key";

    [Fact]
    public async Task Recommendation_query_client_includes_recommendation_api_key()
    {
        var recorder = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<RecommendationItemDto>())
        });
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var logs = new CapturingLogger<RecommendationApiClient>();
        var client = new RecommendationApiClient(httpClient, logs);

        var recommendations = await client.GetPopularRecommendationsAsync();

        Assert.Empty(recommendations);
        Assert.Equal("/api/v1/recommendations/popular", recorder.RequestPath);
        Assert.Equal("?limit=5", recorder.RequestQuery);
        Assert.Equal(ApiKey, recorder.ApiKey);
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Frequently_bought_together_query_uses_the_authenticated_api_route()
    {
        var productId = Guid.NewGuid();
        var recorder = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<RecommendationItemDto>())
        });
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var logs = new CapturingLogger<RecommendationApiClient>();
        var client = new RecommendationApiClient(httpClient, logs);

        var recommendations = await client.GetFrequentlyBoughtTogetherAsync(productId, 4);

        Assert.Empty(recommendations);
        Assert.Equal($"/api/v1/recommendations/fbt/{productId}", recorder.RequestPath);
        Assert.Equal("?limit=4", recorder.RequestQuery);
        Assert.Equal(ApiKey, recorder.ApiKey);
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Product_delivery_includes_recommendation_api_key()
    {
        var recorder = new RecordingHandler();
        var dispatcher = CreateDispatcher(recorder, ApiKey);

        var result = await dispatcher.DispatchAsync(CreateClaimedMessage(
            "ProductSnapshotChangedV1",
            "Recommendation.Api"));

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/events/products", recorder.RequestPath);
        Assert.Equal(ApiKey, recorder.ApiKey);
    }

    [Fact]
    public async Task Order_delivery_includes_recommendation_api_key()
    {
        var recorder = new RecordingHandler();
        var dispatcher = CreateDispatcher(recorder, ApiKey);

        var result = await dispatcher.DispatchAsync(CreateClaimedMessage(
            "OrderConfirmedForRecommendationV1",
            "Recommendation.Api"));

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/events/orders", recorder.RequestPath);
        Assert.Equal(ApiKey, recorder.ApiKey);
    }

    [Fact]
    public async Task Erp_delivery_does_not_receive_recommendation_api_key()
    {
        var recorder = new RecordingHandler();
        var dispatcher = CreateDispatcher(recorder, ApiKey);

        var result = await dispatcher.DispatchAsync(CreateClaimedMessage(
            "OrderReadyForErpV1",
            "ErpIntegration.Api"));

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/integration/orders", recorder.RequestPath);
        Assert.Null(recorder.ApiKey);
    }

    [Fact]
    public async Task Unrelated_request_does_not_receive_recommendation_api_key()
    {
        var recorder = new RecordingHandler();
        using var client = CreateRecommendationHttpClient(recorder, ApiKey);

        using var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/v1/health", recorder.RequestPath);
        Assert.Null(recorder.ApiKey);
    }

    [Fact]
    public async Task Missing_recommendation_key_is_a_clear_configuration_failure()
    {
        var recorder = new RecordingHandler();
        var dispatcher = CreateDispatcher(recorder, string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(CreateClaimedMessage(
                "ProductSnapshotChangedV1",
                "Recommendation.Api")));

        Assert.Contains(
            "Services:RecommendationOutbox:ApiKey",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "required",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(recorder.RequestPath);
    }

    [Fact]
    public async Task Key_is_not_logged_or_persisted_in_outbox_payload()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var payload = $"{{\"EventId\":\"{Guid.NewGuid()}\"}}";
        context.OutboxMessages.Add(CreatePersistedMessage(payload));
        await context.SaveChangesAsync();

        var recorder = new RecordingHandler(
            _ => throw new HttpRequestException("Injected transport failure."));
        var logs = new CapturingLogger<OutboxService>();
        var service = new OutboxService(
            new SqlServerOutboxStore(context),
            CreateDispatcher(recorder, ApiKey),
            logs);

        await service.ProcessPendingMessagesAsync();

        Assert.Equal(ApiKey, recorder.ApiKey);
        Assert.Equal(payload, recorder.RequestPayload);
        Assert.DoesNotContain(ApiKey, recorder.RequestPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Contains(ApiKey, StringComparison.Ordinal));

        context.ChangeTracker.Clear();
        var persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(payload, persisted.Payload);
        Assert.DoesNotContain(ApiKey, persisted.Payload, StringComparison.Ordinal);
        Assert.Equal(OutboxStatus.Retrying, persisted.Status);
    }

    private static HttpOutboxDispatcher CreateDispatcher(
        RecordingHandler recorder,
        string apiKey)
    {
        return new HttpOutboxDispatcher(
            new NamedClientFactory(recorder, apiKey),
            NullLogger<HttpOutboxDispatcher>.Instance);
    }

    private static HttpClient CreateRecommendationHttpClient(
        RecordingHandler recorder,
        string apiKey)
    {
        var authenticationHandler = new RecommendationApiKeyHandler(
            Options.Create(new RecommendationOutboxOptions
            {
                ApiKey = apiKey
            }))
        {
            InnerHandler = recorder
        };

        return new HttpClient(authenticationHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://test.invalid")
        };
    }

    private static ClaimedOutboxMessageDto CreateClaimedMessage(
        string eventType,
        string destination)
    {
        return new ClaimedOutboxMessageDto(
            1,
            eventType,
            destination,
            "{}");
    }

    private static OutboxMessage CreatePersistedMessage(string payload)
    {
        var now = DateTime.UtcNow;
        return new OutboxMessage
        {
            EventId = Guid.NewGuid(),
            EventType = "ProductSnapshotChangedV1",
            Destination = "Recommendation.Api",
            AggregateType = "Product",
            AggregateId = Guid.NewGuid(),
            Payload = payload,
            Status = OutboxStatus.Pending,
            OccurredAtUtc = now,
            AvailableAtUtc = now,
            CorrelationId = Guid.NewGuid()
        };
    }

    private sealed class NamedClientFactory(
        RecordingHandler terminalHandler,
        string apiKey)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            HttpMessageHandler pipeline = terminalHandler;
            if (name.Equals("RecommendationApi", StringComparison.Ordinal))
            {
                pipeline = new RecommendationApiKeyHandler(
                    Options.Create(new RecommendationOutboxOptions
                    {
                        ApiKey = apiKey
                    }))
                {
                    InnerHandler = terminalHandler
                };
            }

            return new HttpClient(pipeline, disposeHandler: false)
            {
                BaseAddress = new Uri("https://test.invalid")
            };
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null)
        : HttpMessageHandler
    {
        public string? RequestPath { get; private set; }

        public string? RequestQuery { get; private set; }

        public string? ApiKey { get; private set; }

        public string RequestPayload { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestQuery = request.RequestUri?.Query;
            ApiKey = request.Headers.TryGetValues(
                RecommendationOutboxOptions.ApiKeyHeaderName,
                out var values)
                    ? values.Single()
                    : null;
            RequestPayload = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

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
            Entries.Add(formatter(state, exception));
            if (exception is not null)
            {
                Entries.Add(exception.ToString());
            }
        }
    }
}
