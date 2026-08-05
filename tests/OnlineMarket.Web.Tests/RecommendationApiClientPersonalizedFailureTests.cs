using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Infrastructure.Http;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class RecommendationApiClientPersonalizedFailureTests : IDisposable
{
    private const string ApiKey = "test-only-personalized-api-key";

    public RecommendationApiClientPersonalizedFailureTests()
    {
        ResetRecommendationCircuit();
    }

    [Fact]
    public async Task Empty_personalized_response_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("[]")
            }));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Personalized_timeout_returns_no_recommendations()
    {
        var client = CreateClient(new ThrowingHandler(
            new TaskCanceledException("Injected timeout.")));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Personalized_non_success_response_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Personalized_malformed_json_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("{not-json")
            }));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Open_circuit_returns_no_recommendations_without_an_http_call()
    {
        var handler = new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("[]")
            });
        var client = CreateClient(handler);
        SetRecommendationCircuit(DateTime.UtcNow.AddMinutes(1));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"score\":0.5,\"recommendationType\":\"HybridPersonalized\",\"reasonCode\":\"Internal\",\"reasonText\":\"Internal\",\"metrics\":null}]")]
    public async Task Personalized_invalid_contract_returns_no_recommendations(
        string payload)
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(payload)
            }));

        var recommendations = await client.GetPersonalizedRecommendationsAsync(
            Guid.NewGuid(),
            8);

        Assert.Empty(recommendations);
    }

    public void Dispose()
    {
        ResetRecommendationCircuit();
    }

    private static StringContent Json(string payload) =>
        new(payload, Encoding.UTF8, "application/json");

    private static RecommendationApiClient CreateClient(
        HttpMessageHandler terminalHandler)
    {
        var authenticationHandler = new RecommendationApiKeyHandler(
            Options.Create(new RecommendationOutboxOptions
            {
                ApiKey = ApiKey,
                RecommendationApiBaseAddress = "https://test.invalid"
            }))
        {
            InnerHandler = terminalHandler
        };
        var httpClient = new HttpClient(authenticationHandler)
        {
            BaseAddress = new Uri("https://test.invalid")
        };
        return new RecommendationApiClient(
            httpClient,
            NullLogger<RecommendationApiClient>.Instance);
    }

    private static void ResetRecommendationCircuit()
    {
        SetRecommendationCircuit(DateTime.MinValue);
    }

    private static void SetRecommendationCircuit(DateTime offlineUntilUtc)
    {
        var field = typeof(RecommendationApiClient).GetField(
            "_offlineUntilUtc",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        field.SetValue(null, offlineUntilUtc);
    }

    private sealed class ResponseHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
