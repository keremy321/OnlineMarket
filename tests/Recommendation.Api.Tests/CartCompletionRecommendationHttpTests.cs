using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class CartCompletionRecommendationHttpTests(
    RecommendationSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Cart_completion_requires_authentication_and_empty_results_succeed()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var source = CreateProduct(1);
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.Add(source);
            await setup.SaveChangesAsync();
        }

        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        using var unauthenticated = await client.PostAsJsonAsync(
            "/api/v1/recommendations/cart",
            new { productIds = new[] { source.ProductId }, limit = 10 });
        using var authenticated = await PostAuthenticatedAsync(
            client,
            new { productIds = new[] { source.ProductId }, limit = 10 });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Empty(await ReadRecommendationsAsync(authenticated));
    }

    [Fact]
    public async Task Cart_completion_strictly_validates_json_and_product_ids()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var productId = Guid.NewGuid();

        using var unknownProperty = await PostAuthenticatedJsonAsync(
            client,
            $$"""
            {
              "productIds": ["{{productId}}"],
              "limit": 10,
              "customerId": "{{Guid.NewGuid()}}"
            }
            """);
        using var emptyProducts = await PostAuthenticatedJsonAsync(
            client,
            """{"productIds": [], "limit": 10}""");
        using var emptyGuid = await PostAuthenticatedJsonAsync(
            client,
            $$"""{"productIds": ["{{Guid.Empty}}"], "limit": 10}""");
        using var invalidGuid = await PostAuthenticatedJsonAsync(
            client,
            """{"productIds": ["not-a-guid"], "limit": 10}""");

        Assert.Equal(HttpStatusCode.BadRequest, unknownProperty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyProducts.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyGuid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidGuid.StatusCode);
    }

    [Fact]
    public async Task Cart_completion_aggregates_distinct_sources_and_applies_exclusions()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstSource = CreateProduct(11);
        var secondSource = CreateProduct(12);
        var candidate = CreateProduct(13);
        var inactive = CreateProduct(14, isActive: false);
        var outOfStock = CreateProduct(15, isInStock: false);
        await SeedAffinitiesAsync(
            database,
            [firstSource, secondSource, candidate, inactive, outOfStock],
            [
                Rule(firstSource, candidate, 0.8m, 0.6m, 2m),
                Rule(secondSource, candidate, 0.4m, 0.8m, 4m),
                Rule(firstSource, secondSource, 0.9m, 0.9m, 3m),
                Rule(firstSource, inactive, 0.9m, 0.9m, 3m),
                Rule(secondSource, outOfStock, 0.9m, 0.9m, 3m)
            ]);

        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        using var response = await PostAuthenticatedAsync(
            client,
            new
            {
                productIds = new[]
                {
                    firstSource.ProductId,
                    firstSource.ProductId,
                    secondSource.ProductId,
                    firstSource.ProductId
                },
                limit = 10
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recommendation = Assert.Single(
            await ReadRecommendationsAsync(response));
        Assert.Equal(candidate.ProductId, recommendation.ProductId);
        Assert.Equal(1m, recommendation.Score);
        Assert.Equal("CartCompletion", recommendation.RecommendationType);
        Assert.Equal(
            "Association.CartCompletion",
            recommendation.ReasonCode);
        Assert.NotNull(recommendation.Metrics);
        Assert.Equal(2, recommendation.Metrics.SupportingCartProductCount);
        Assert.Equal(0.700000m, recommendation.Metrics.Confidence);
        Assert.Equal(3.000000m, recommendation.Metrics.Lift);
    }

    [Fact]
    public async Task Cart_completion_scoring_order_and_limits_are_deterministic()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstSource = CreateProduct(21);
        var secondSource = CreateProduct(22);
        var strongest = CreateProduct(23);
        var firstTie = CreateProduct(24);
        var secondTie = CreateProduct(25);
        var weakest = CreateProduct(26);
        await SeedAffinitiesAsync(
            database,
            [
                firstSource,
                secondSource,
                strongest,
                firstTie,
                secondTie,
                weakest
            ],
            [
                Rule(firstSource, strongest, 0.8m, 0.8m, 2m),
                Rule(secondSource, strongest, 0.8m, 0.8m, 2m),
                Rule(firstSource, firstTie, 0.8m, 0.8m, 2m),
                Rule(firstSource, secondTie, 0.8m, 0.8m, 2m),
                Rule(firstSource, weakest, 0.1m, 0.1m, 0.5m)
            ]);
        var configuration = new Dictionary<string, string?>
        {
            ["Recommendation:CartCompletion:DefaultLimit"] = "2",
            ["Recommendation:CartCompletion:MaximumLimit"] = "3"
        };

        await using var factory = CreateFactory(database, configuration);
        using var client = CreateClient(factory);
        var cart = new[] { firstSource.ProductId, secondSource.ProductId };
        using var defaultResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart });
        using var oneResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart, limit = 1 });
        using var cappedResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart, limit = 1000 });
        using var repeatedResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart, limit = 1000 });
        using var zeroResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart, limit = 0 });
        using var negativeResponse = await PostAuthenticatedAsync(
            client,
            new { productIds = cart, limit = -1 });

        var defaults = await ReadRecommendationsAsync(defaultResponse);
        var one = await ReadRecommendationsAsync(oneResponse);
        var capped = await ReadRecommendationsAsync(cappedResponse);
        var repeated = await ReadRecommendationsAsync(repeatedResponse);
        Assert.Equal(2, defaults.Count);
        Assert.Single(one);
        Assert.Equal(3, capped.Count);
        Assert.Equal(HttpStatusCode.BadRequest, zeroResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativeResponse.StatusCode);
        Assert.Equal(strongest.ProductId, capped[0].ProductId);
        Assert.Equal(firstTie.ProductId, capped[1].ProductId);
        Assert.Equal(secondTie.ProductId, capped[2].ProductId);
        Assert.Equal(
            capped.Select(item => item.ProductId),
            repeated.Select(item => item.ProductId));
        Assert.Equal(
            capped.Take(2).Select(item => item.ProductId),
            defaults.Select(item => item.ProductId));
        Assert.Equal(strongest.ProductId, one[0].ProductId);
        Assert.All(capped, item => Assert.InRange(item.Score, 0m, 1m));
        Assert.True(capped[0].Score > capped[1].Score);
    }

    private static async Task SeedAffinitiesAsync(
        RecommendationTestDatabase database,
        IReadOnlyList<ProductSnapshot> products,
        IReadOnlyList<RuleSeed> rules)
    {
        await using var context = database.CreateContext();
        var run = new RecommendationRun
        {
            Id = Guid.NewGuid(),
            RunType = RecommendationRunType.Affinity,
            Status = RecommendationRunStatus.Succeeded,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTime.UtcNow,
            InputRecordCount = rules.Count,
            OutputRecordCount = rules.Count,
            ParametersJson = "{}",
            CorrelationId = Guid.NewGuid()
        };
        context.ProductSnapshots.AddRange(products);
        context.RecommendationRuns.Add(run);
        context.ProductAffinities.AddRange(rules.Select(rule =>
            new ProductAffinity
            {
                SourceProductId = rule.SourceProductId,
                RecommendedProductId = rule.RecommendedProductId,
                CoOccurrenceCount = 1,
                SourceOrderCount = 1,
                RecommendedOrderCount = 1,
                TotalOrderCount = 1,
                Support = 1m,
                Confidence = rule.Confidence,
                Lift = rule.Lift,
                Score = rule.Score,
                RunId = run.Id,
                CalculatedAtUtc = DateTime.UtcNow
            }));
        await context.SaveChangesAsync();
    }

    private static RuleSeed Rule(
        ProductSnapshot source,
        ProductSnapshot recommended,
        decimal score,
        decimal confidence,
        decimal lift)
    {
        return new RuleSeed(
            source.ProductId,
            recommended.ProductId,
            score,
            confidence,
            lift);
    }

    private static ProductSnapshot CreateProduct(
        int key,
        bool isActive = true,
        bool isInStock = true)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse($"20000000-0000-0000-0000-{key:D12}"),
            Sku = $"CART-{key:D4}",
            Name = $"Cart product {key}",
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Price = 10m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = isActive,
            IsInStock = isInStock,
            SourceUpdatedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    private static RecommendationWebApplicationFactory CreateFactory(
        RecommendationTestDatabase database,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        return new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey,
            configuration);
    }

    private static HttpClient CreateClient(
        RecommendationWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task<HttpResponseMessage> PostAuthenticatedAsync<T>(
        HttpClient client,
        T body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/recommendations/cart")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAuthenticatedJsonAsync(
        HttpClient client,
        string json)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/recommendations/cart")
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);
        return await client.SendAsync(request);
    }

    private static async Task<List<CartCompletionRecommendationResponse>>
        ReadRecommendationsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<
                List<CartCompletionRecommendationResponse>>()
            ?? throw new InvalidOperationException(
                "Unable to deserialize cart-completion recommendations.");
    }

    private sealed record RuleSeed(
        Guid SourceProductId,
        Guid RecommendedProductId,
        decimal Score,
        decimal Confidence,
        decimal Lift);
}
