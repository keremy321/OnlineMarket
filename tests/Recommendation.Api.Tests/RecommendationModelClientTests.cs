using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Infrastructure.Http;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelClientTests
{
    private const string ModelApiKey = "test-only-model-api-key";

    [Fact]
    public async Task Model_client_delivers_key_only_on_its_own_request()
    {
        string? deliveredKey = null;
        var handler = new DelegateHandler(request =>
        {
            deliveredKey = request.Headers.TryGetValues(
                ApiKeyDefaults.HeaderName,
                out var values)
                    ? values.Single()
                    : null;
            return JsonResponse(CreateSimilarJson());
        });
        var client = CreateClient(handler);

        var result = await client.GetSimilarAsync(
            new RecommendationModelSimilarRequest(SourceId, 2));
        using var unrelatedHandler = new DelegateHandler(request =>
        {
            Assert.False(request.Headers.Contains(ApiKeyDefaults.HeaderName));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var unrelatedClient = new HttpClient(unrelatedHandler);
        using var unrelatedResponse = await unrelatedClient.GetAsync(
            "https://unrelated-service.test/health");

        Assert.Equal(
            RecommendationModelClientOutcome.Succeeded,
            result.Outcome);
        Assert.Equal(ModelApiKey, deliveredKey);
        Assert.Equal(HttpStatusCode.OK, unrelatedResponse.StatusCode);
    }

    [Fact]
    public async Task Similar_response_is_mapped_when_valid()
    {
        var client = CreateClient(new DelegateHandler(
            _ => JsonResponse(CreateSimilarJson())));

        var result = await client.GetSimilarAsync(
            new RecommendationModelSimilarRequest(SourceId, 2));

        Assert.Equal(
            RecommendationModelClientOutcome.Succeeded,
            result.Outcome);
        Assert.Equal("tfidf-test-v1", result.Value!.ModelVersion);
        Assert.Collection(
            result.Value.Items,
            item =>
            {
                Assert.Equal(CandidateOneId, item.ProductId);
                Assert.Equal(0.90m, item.TfidfScore);
            },
            item =>
            {
                Assert.Equal(CandidateTwoId, item.ProductId);
                Assert.Equal(0.50m, item.TfidfScore);
            });
    }

    [Fact]
    public async Task Training_request_contains_subject_id_and_no_customer_id()
    {
        string? requestJson = null;
        var client = CreateClient(new DelegateHandler(request =>
        {
            requestJson = request.Content!
                .ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        var subjectId = $"v1.{new string('A', 43)}";

        await client.TrainAsync(new RecommendationModelTrainingRequest(
            "tfidf-test-v1",
            Guid.NewGuid(),
            [
                new RecommendationModelProductRequest(
                    SourceId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Product",
                    null,
                    "Piece",
                    1m,
                    10m,
                    true,
                    true)
            ],
            [
                new RecommendationModelOrderInteractionRequest(
                    Guid.NewGuid(),
                    subjectId,
                    [
                        new RecommendationModelOrderInteractionItemRequest(
                            SourceId,
                            1)
                    ])
            ]));

        Assert.NotNull(requestJson);
        Assert.Contains(
            $"\"subjectId\":\"{subjectId}\"",
            requestJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "customerId",
            requestJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Unavailable_model_response_returns_unavailable(
        HttpStatusCode statusCode)
    {
        var client = CreateClient(new DelegateHandler(
            _ => new HttpResponseMessage(statusCode)));

        var result = await client.GetSimilarAsync(
            new RecommendationModelSimilarRequest(SourceId, 2));

        Assert.Equal(
            RecommendationModelClientOutcome.Unavailable,
            result.Outcome);
    }

    [Fact]
    public async Task Timeout_returns_unavailable()
    {
        var handler = new DelegateHandler(_ =>
            throw new TaskCanceledException("Injected timeout."));
        var client = CreateClient(handler);

        var result = await client.GetSimilarAsync(
            new RecommendationModelSimilarRequest(SourceId, 2));

        Assert.Equal(
            RecommendationModelClientOutcome.Unavailable,
            result.Outcome);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"modelVersion\":\"tfidf-test-v1\",\"sourceProductId\":\"00000000-0000-0000-0000-000000000001\",\"items\":[{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"tfidfScore\":0.5}]}")]
    [InlineData("{\"modelVersion\":\"tfidf-test-v1\",\"sourceProductId\":\"00000000-0000-0000-0000-000000000001\",\"items\":[{\"productId\":\"00000000-0000-0000-0000-000000000002\",\"tfidfScore\":1.5}]}")]
    public async Task Invalid_response_returns_invalid_response(string json)
    {
        var client = CreateClient(new DelegateHandler(
            _ => JsonResponse(json)));

        var result = await client.GetSimilarAsync(
            new RecommendationModelSimilarRequest(SourceId, 2));

        Assert.Equal(
            RecommendationModelClientOutcome.InvalidResponse,
            result.Outcome);
    }

    private static RecommendationModelClient CreateClient(
        HttpMessageHandler handler)
    {
        var options = Options.Create(new RecommendationModelServiceOptions
        {
            BaseAddress = "https://model-service.test",
            ApiKey = ModelApiKey,
            Timeout = TimeSpan.FromSeconds(1)
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.Value.BaseAddress),
            Timeout = options.Value.Timeout
        };
        return new RecommendationModelClient(
            httpClient,
            options,
            new RecommendationModelCircuitBreaker(TimeProvider.System),
            NullLogger<RecommendationModelClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string CreateSimilarJson()
    {
        return JsonSerializer.Serialize(new
        {
            modelVersion = "tfidf-test-v1",
            sourceProductId = SourceId,
            items = new[]
            {
                new { productId = CandidateOneId, tfidfScore = 0.90m },
                new { productId = CandidateTwoId, tfidfScore = 0.50m }
            }
        });
    }

    private static readonly Guid SourceId = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid CandidateOneId = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid CandidateTwoId = Guid.Parse(
        "00000000-0000-0000-0000-000000000003");

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(callback(request));
        }
    }
}
