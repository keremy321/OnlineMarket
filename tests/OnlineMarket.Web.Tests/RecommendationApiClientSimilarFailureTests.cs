using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Infrastructure.Http;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class RecommendationApiClientSimilarFailureTests : IDisposable
{
    private const string ApiKey = "test-only-similar-api-key";

    public RecommendationApiClientSimilarFailureTests()
    {
        ResetRecommendationCircuit();
    }

    [Fact]
    public async Task Empty_similar_response_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[]",
                    Encoding.UTF8,
                    "application/json")
            }));

        var recommendations = await client.GetSimilarProductsAsync(
            Guid.NewGuid(),
            4);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Similar_timeout_returns_no_recommendations()
    {
        var client = CreateClient(new ThrowingHandler(
            new TaskCanceledException("Injected timeout.")));

        var recommendations = await client.GetSimilarProductsAsync(
            Guid.NewGuid(),
            4);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Similar_non_success_response_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var recommendations = await client.GetSimilarProductsAsync(
            Guid.NewGuid(),
            4);

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Similar_malformed_json_returns_no_recommendations()
    {
        var client = CreateClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            }));

        var recommendations = await client.GetSimilarProductsAsync(
            Guid.NewGuid(),
            4);

        Assert.Empty(recommendations);
    }

    public void Dispose()
    {
        ResetRecommendationCircuit();
    }

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
        var field = typeof(RecommendationApiClient).GetField(
            "_offlineUntilUtc",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        field.SetValue(null, DateTime.MinValue);
    }

    private sealed class ResponseHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
