using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Infrastructure.Http;

namespace OnlineMarket.Web.Tests;

/// <summary>
/// Covers the fix for RECOMMENDATION-FBT-TIMEOUT-ALIGNMENT: GetFrequentlyBoughtTogetherAsync
/// used an isolated, hard-coded 500 ms CancellationTokenSource that made the AI assistant's
/// FBT scenario unreliable locally. It now shares the same configurable, bounded budget as
/// Similar/Personalized and distinguishes caller cancellation from an internal timeout.
/// </summary>
[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class RecommendationApiClientFbtTimeoutTests : IDisposable
{
    private const string ApiKey = "test-only-fbt-timeout-api-key";

    public RecommendationApiClientFbtTimeoutTests()
    {
        ResetRecommendationCircuit();
    }

    public void Dispose()
    {
        ResetRecommendationCircuit();
    }

    // A valid FBT response taking longer than the old 500 ms budget must now succeed.
    [Fact]
    public async Task Fbt_ResponseSlowerThanTheOldFiveHundredMillisecondBudget_StillReturnsRecommendations()
    {
        var payload = "[{\"productId\":\"00000000-0000-0000-0000-000000000001\"," +
            "\"reasonText\":\"Frequently bought together\",\"score\":0.75}]";
        var client = CreateClient(
            new DelayingHandler(
                TimeSpan.FromMilliseconds(700),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(payload) }),
            readTimeoutSeconds: 4);

        var recommendations = await client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        var recommendation = Assert.Single(recommendations);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), recommendation.ProductId);
    }

    // The configured timeout is still enforced: a response slower than the configured
    // budget degrades to an empty result instead of hanging or throwing to the caller.
    [Fact]
    public async Task Fbt_ResponseSlowerThanTheConfiguredBudget_DegradesToEmptyResult()
    {
        var handler = new DelayingHandler(
            TimeSpan.FromSeconds(2),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var client = CreateClient(handler, readTimeoutSeconds: 1);

        var recommendations = await client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        Assert.Empty(recommendations);
        // The internal timeout must have actually fired before the handler's own delay
        // elapsed; otherwise this assertion would be exercising the wrong code path.
        Assert.True(handler.ObservedCancellation);
    }

    // A caller cancellation (the shopper closing the chat panel) must propagate as a real
    // OperationCanceledException, not degrade into an empty "unavailable" result.
    [Fact]
    public async Task Fbt_CallerCancellation_PropagatesInsteadOfDegrading()
    {
        var handler = new DelayingHandler(
            TimeSpan.FromSeconds(30),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var client = CreateClient(handler, readTimeoutSeconds: 4);
        using var callerCts = new CancellationTokenSource();
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5, callerCts.Token));
    }

    // Caller cancellation must not be treated as a Recommendation.Api failure: it must not
    // open the circuit breaker used by every other read.
    [Fact]
    public async Task Fbt_CallerCancellation_DoesNotOpenTheCircuit()
    {
        var handler = new DelayingHandler(
            TimeSpan.FromSeconds(30),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var client = CreateClient(handler, readTimeoutSeconds: 4);
        using var callerCts = new CancellationTokenSource();
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5, callerCts.Token));

        // A subsequent, independent call must still reach the network rather than being
        // short-circuited by a falsely opened circuit.
        var followUpHandler = new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var followUpClient = CreateClient(followUpHandler, readTimeoutSeconds: 4);
        await followUpClient.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        Assert.Equal(1, followUpHandler.RequestCount);
    }

    // Canceled requests must not be retried.
    [Fact]
    public async Task Fbt_CallerCancellation_DoesNotRetry()
    {
        var handler = new DelayingHandler(
            TimeSpan.FromSeconds(30),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var client = CreateClient(handler, readTimeoutSeconds: 4);
        using var callerCts = new CancellationTokenSource();
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5, callerCts.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    // An unavailable Recommendation.Api still degrades safely (existing behavior preserved).
    [Fact]
    public async Task Fbt_RecommendationApiUnavailable_DegradesToEmptyResultAndOpensTheCircuit()
    {
        var client = CreateClient(
            new ThrowingHandler(new HttpRequestException("Injected connection failure.")),
            readTimeoutSeconds: 4);

        var recommendations = await client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        Assert.Empty(recommendations);

        var openCircuitHandler = new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var openCircuitClient = CreateClient(openCircuitHandler, readTimeoutSeconds: 4);
        var secondCall = await openCircuitClient.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        Assert.Empty(secondCall);
        Assert.Equal(0, openCircuitHandler.RequestCount);
    }

    [Fact]
    public async Task Fbt_NonSuccessResponse_DegradesToEmptyResult()
    {
        var client = CreateClient(
            new ResponseHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            readTimeoutSeconds: 4);

        var recommendations = await client.GetFrequentlyBoughtTogetherAsync(Guid.NewGuid(), 5);

        Assert.Empty(recommendations);
    }

    // Only FBT gained a CancellationToken parameter; the other four reads keep their
    // existing contract untouched by this fix.
    [Fact]
    public void OnlyFrequentlyBoughtTogether_GainedACancellationTokenParameter()
    {
        foreach (var method in typeof(IRecommendationClient).GetMethods())
        {
            var parameters = method.GetParameters();
            var hasCancellationToken = parameters.Any(
                parameter => parameter.ParameterType == typeof(CancellationToken));

            if (method.Name == nameof(IRecommendationClient.GetFrequentlyBoughtTogetherAsync))
            {
                Assert.True(hasCancellationToken);
                var tokenParameter = Assert.Single(
                    parameters,
                    parameter => parameter.ParameterType == typeof(CancellationToken));
                Assert.True(tokenParameter.HasDefaultValue);
            }
            else
            {
                Assert.False(
                    hasCancellationToken,
                    $"{method.Name} must not have gained a CancellationToken parameter.");
            }
        }
    }

    // No change to Recommendation.Api's wire contract: same route, method and query shape
    // for every read this fix touches (FBT, Similar, Personalized) and the two it does not
    // (Popular, CartCompletion).
    [Theory]
    [InlineData(
        nameof(IRecommendationClient.GetPopularRecommendationsAsync),
        "GET",
        "/api/v1/recommendations/popular?limit=5")]
    [InlineData(
        nameof(IRecommendationClient.GetSimilarProductsAsync),
        "GET",
        "/api/v1/recommendations/similar/11111111-1111-1111-1111-111111111111?limit=4")]
    [InlineData(
        nameof(IRecommendationClient.GetPersonalizedRecommendationsAsync),
        "GET",
        "/api/v1/recommendations/customers/11111111-1111-1111-1111-111111111111?limit=5")]
    [InlineData(
        nameof(IRecommendationClient.GetFrequentlyBoughtTogetherAsync),
        "GET",
        "/api/v1/recommendations/fbt/11111111-1111-1111-1111-111111111111?limit=5")]
    public async Task OtherEndpointContracts_AreUnchanged(
        string methodName,
        string expectedMethod,
        string expectedPathAndQuery)
    {
        var handler = new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("[]") });
        var client = CreateClient(handler, readTimeoutSeconds: 4);
        var productId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        switch (methodName)
        {
            case nameof(IRecommendationClient.GetPopularRecommendationsAsync):
                await client.GetPopularRecommendationsAsync(5);
                break;
            case nameof(IRecommendationClient.GetSimilarProductsAsync):
                await client.GetSimilarProductsAsync(productId, 4);
                break;
            case nameof(IRecommendationClient.GetPersonalizedRecommendationsAsync):
                await client.GetPersonalizedRecommendationsAsync(productId, 5);
                break;
            case nameof(IRecommendationClient.GetFrequentlyBoughtTogetherAsync):
                await client.GetFrequentlyBoughtTogetherAsync(productId, 5);
                break;
        }

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(expectedMethod, handler.LastRequest!.Method.Method);
        Assert.Equal(
            expectedPathAndQuery,
            handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task CartCompletion_UnaffectedByFbtChange_KeepsItsOwnFastBudget()
    {
        // A non-empty result is required here: an empty cart-completion response makes
        // the client fall through to its own popularity fallback (a second HTTP call),
        // which would overwrite the captured request this test inspects.
        var payload = "[{\"productId\":\"00000000-0000-0000-0000-000000000002\"," +
            "\"reasonText\":\"Cart completion\",\"score\":0.6}]";
        var handler = new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(payload) });
        var client = CreateClient(handler, readTimeoutSeconds: 4);

        var recommendations = await client.GetCartCompletionRecommendationsAsync([Guid.NewGuid()], 5);

        Assert.Single(recommendations);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("POST", handler.LastRequest!.Method.Method);
        Assert.Equal("/api/v1/recommendations/cart", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    private static StringContent Json(string payload) =>
        new(payload, Encoding.UTF8, "application/json");

    private static RecommendationApiClient CreateClient(
        HttpMessageHandler terminalHandler,
        int readTimeoutSeconds)
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
            NullLogger<RecommendationApiClient>.Instance,
            Options.Create(new RecommendationApiClientOptions
            {
                ReadTimeoutSeconds = readTimeoutSeconds
            }));
    }

    private static void ResetRecommendationCircuit()
    {
        var field = typeof(RecommendationApiClient).GetField(
            "_offlineUntilUtc",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        field.SetValue(null, DateTime.MinValue);
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    /// <summary>
    /// Waits cooperatively so the internal read-timeout (or an external caller
    /// cancellation) actually fires within the test instead of the delay running to
    /// completion first.
    /// </summary>
    private sealed class DelayingHandler(TimeSpan delay, HttpResponseMessage response)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool ObservedCancellation { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            return response;
        }
    }
}
