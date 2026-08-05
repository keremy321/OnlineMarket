using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Similar_query_uses_capped_limit_and_recommendation_api_key()
    {
        var productId = Guid.NewGuid();
        var recommendedProductIds = Enumerable.Range(0, 5)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var recorder = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(recommendedProductIds.Select(
                (recommendedProductId, index) =>
                    new SimilarRecommendationResponseDto(
                        recommendedProductId,
                        1m - (index * 0.1m),
                        "Similar",
                        "Similar.Content",
                        "Similar product")))
        });
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var logs = new CapturingLogger<RecommendationApiClient>();
        var client = new RecommendationApiClient(httpClient, logs);

        var recommendations = await client.GetSimilarProductsAsync(productId, 99);

        Assert.Equal(recommendedProductIds.Take(4), recommendations.Select(item => item.ProductId));
        Assert.Equal($"/api/v1/recommendations/similar/{productId}", recorder.RequestPath);
        Assert.Equal("?limit=4", recorder.RequestQuery);
        Assert.Equal(ApiKey, recorder.ApiKey);
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Similar_query_rejects_non_positive_limit(int limit)
    {
        var recorder = new RecordingHandler();
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var client = new RecommendationApiClient(
            httpClient,
            NullLogger<RecommendationApiClient>.Instance);

        var recommendations = await client.GetSimilarProductsAsync(
            Guid.NewGuid(),
            limit);

        Assert.Empty(recommendations);
        Assert.Null(recorder.RequestPath);
    }

    [Fact]
    public async Task Cart_completion_client_posts_strict_body_with_api_key()
    {
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var recommendedProductId = Guid.NewGuid();
        var recorder = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new RecommendationItemDto(
                    recommendedProductId,
                    "Cart completion",
                    1m)
            })
        });
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var logs = new CapturingLogger<RecommendationApiClient>();
        var client = new RecommendationApiClient(httpClient, logs);

        var recommendations = await client.GetCartCompletionRecommendationsAsync(
            [firstProductId, firstProductId, secondProductId],
            4);

        var recommendation = Assert.Single(recommendations);
        Assert.Equal(recommendedProductId, recommendation.ProductId);
        var request = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Post.Method, request.Method);
        Assert.Equal("/api/v1/recommendations/cart", request.Path);
        Assert.Equal(ApiKey, request.ApiKey);
        using var payload = JsonDocument.Parse(request.Payload);
        Assert.Equal(
            [firstProductId, secondProductId],
            payload.RootElement
                .GetProperty("productIds")
                .EnumerateArray()
                .Select(element => element.GetGuid()));
        Assert.Equal(4, payload.RootElement.GetProperty("limit").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("count", out _));
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Cart_completion_client_uses_ordered_popularity_fallback(
        HttpStatusCode cartStatus)
    {
        var cartProductId = Guid.NewGuid();
        var firstFallbackId = Guid.NewGuid();
        var secondFallbackId = Guid.NewGuid();
        var recorder = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath
                == "/api/v1/recommendations/cart")
            {
                return new HttpResponseMessage(cartStatus)
                {
                    Content = JsonContent.Create(
                        Array.Empty<RecommendationItemDto>())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new RecommendationItemDto(cartProductId, "Popular", 1m),
                    new RecommendationItemDto(firstFallbackId, "Popular", 0.8m),
                    new RecommendationItemDto(secondFallbackId, "Popular", 0.7m)
                })
            };
        });
        using var httpClient = CreateRecommendationHttpClient(recorder, ApiKey);
        var client = new RecommendationApiClient(
            httpClient,
            NullLogger<RecommendationApiClient>.Instance);

        var recommendations = await client.GetCartCompletionRecommendationsAsync(
            [cartProductId],
            2);

        Assert.Equal(
            [firstFallbackId, secondFallbackId],
            recommendations.Select(item => item.ProductId));
        Assert.Collection(
            recorder.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post.Method, request.Method);
                Assert.Equal("/api/v1/recommendations/cart", request.Path);
                Assert.Equal(ApiKey, request.ApiKey);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get.Method, request.Method);
                Assert.Equal("/api/v1/recommendations/popular", request.Path);
                Assert.Equal(ApiKey, request.ApiKey);
            });
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
    public async Task Unrelated_service_does_not_receive_recommendation_api_key()
    {
        var recorder = new RecordingHandler();
        using var client = CreateRecommendationHttpClient(recorder, ApiKey);

        using var response = await client.GetAsync(
            $"https://unrelated.invalid/api/v1/recommendations/similar/{Guid.NewGuid()}?limit=4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
                ApiKey = apiKey,
                RecommendationApiBaseAddress = "https://test.invalid"
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
                        ApiKey = apiKey,
                        RecommendationApiBaseAddress = "https://test.invalid"
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

        public List<RecordedRequest> Requests { get; } = [];

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
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                RequestPath ?? string.Empty,
                RequestQuery ?? string.Empty,
                ApiKey,
                RequestPayload));
            return responseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record RecordedRequest(
        string Method,
        string Path,
        string Query,
        string? ApiKey,
        string Payload);

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
