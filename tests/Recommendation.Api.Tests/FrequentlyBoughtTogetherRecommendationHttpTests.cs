using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class FrequentlyBoughtTogetherRecommendationHttpTests(
    RecommendationSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Fbt_requires_authentication_and_empty_recalculation_succeeds()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var sourceProductId = Guid.NewGuid();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync(
            $"/api/v1/recommendations/fbt/{sourceProductId}");
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");
        using var query = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{sourceProductId}");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Empty((await query.Content.ReadFromJsonAsync<
            List<FrequentlyBoughtTogetherRecommendationResponse>>())!);

        await using var verification = database.CreateContext();
        var run = await verification.RecommendationRuns.SingleAsync();
        Assert.Equal(RecommendationRunType.Affinity, run.RunType);
        Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        Assert.Equal(0, run.OutputRecordCount);
    }

    [Fact]
    public void Calculator_deduplicates_products_per_order_and_never_creates_self_pairs()
    {
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var orderProducts = new[]
        {
            new FbtOrderProduct(firstOrderId, firstProductId),
            new FbtOrderProduct(firstOrderId, firstProductId),
            new FbtOrderProduct(firstOrderId, secondProductId),
            new FbtOrderProduct(firstOrderId, secondProductId),
            new FbtOrderProduct(secondOrderId, firstProductId),
            new FbtOrderProduct(secondOrderId, secondProductId)
        };

        var results = FrequentlyBoughtTogetherRecommendationService.Calculate(
            orderProducts,
            2,
            new HashSet<Guid> { firstProductId, secondProductId },
            LowThresholdSettings());

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(
            results,
            item => item.SourceProductId == item.RecommendedProductId);
        Assert.All(results, item =>
        {
            Assert.Equal(2, item.PairOrderCount);
            Assert.Equal(2, item.SourceOrderCount);
            Assert.Equal(2, item.RecommendedOrderCount);
            Assert.Equal(1m, item.Support);
            Assert.Equal(1m, item.Confidence);
            Assert.Equal(1m, item.Lift);
        });
    }

    [Fact]
    public async Task Recalculation_persists_directional_pair_metrics_and_query_results()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var first = CreateProduct(1);
        var second = CreateProduct(2);
        var third = CreateProduct(3);
        await SeedMetricDatasetAsync(database, first, second, third);

        await using var factory = CreateFactory(
            database,
            LowThresholdConfiguration());
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{first.ProductId}?limit=10");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recommendations = (await response.Content.ReadFromJsonAsync<
            List<FrequentlyBoughtTogetherRecommendationResponse>>())!;
        Assert.Equal(2, recommendations.Count);
        Assert.All(recommendations, item =>
        {
            Assert.Equal("FrequentlyBoughtTogether", item.RecommendationType);
            Assert.NotNull(item.Metrics);
        });
        var secondRule = recommendations.Single(
            item => item.ProductId == second.ProductId);
        Assert.Equal(2, secondRule.Metrics!.PairOrderCount);
        Assert.Equal(0.500000m, secondRule.Metrics.Support);
        Assert.Equal(0.666667m, secondRule.Metrics.Confidence);
        Assert.Equal(0.888889m, secondRule.Metrics.Lift);
        var thirdRule = recommendations.Single(
            item => item.ProductId == third.ProductId);
        Assert.Equal(2, thirdRule.Metrics!.PairOrderCount);
        Assert.Equal(0.500000m, thirdRule.Metrics.Support);
        Assert.Equal(0.666667m, thirdRule.Metrics.Confidence);
        Assert.Equal(1.333333m, thirdRule.Metrics.Lift);

        await using var verification = database.CreateContext();
        Assert.Equal(6, await verification.ProductAffinities.CountAsync());
        Assert.False(await verification.ProductAffinities.AnyAsync(
            rule => rule.SourceProductId == rule.RecommendedProductId));
        var run = await verification.RecommendationRuns.SingleAsync();
        Assert.Equal(RecommendationRunType.Affinity, run.RunType);
        Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        Assert.Equal(6, run.OutputRecordCount);
    }

    [Fact]
    public async Task Recalculation_applies_all_configured_thresholds()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var first = CreateProduct(11);
        var second = CreateProduct(12);
        var third = CreateProduct(13);
        await SeedMetricDatasetAsync(database, first, second, third);
        var configuration = new Dictionary<string, string?>
        {
            ["Recommendation:FrequentlyBoughtTogether:MinimumPairOrderCount"] = "2",
            ["Recommendation:FrequentlyBoughtTogether:MinimumSupport"] = "0.5",
            ["Recommendation:FrequentlyBoughtTogether:MinimumConfidence"] = "0.6",
            ["Recommendation:FrequentlyBoughtTogether:MinimumLift"] = "1.0"
        };

        await using var factory = CreateFactory(database, configuration);
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{first.ProductId}");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        var recommendations = (await response.Content.ReadFromJsonAsync<
            List<FrequentlyBoughtTogetherRecommendationResponse>>())!;
        Assert.Single(recommendations);
        Assert.Equal(third.ProductId, recommendations[0].ProductId);

        await using var verification = database.CreateContext();
        var rules = await verification.ProductAffinities
            .OrderBy(rule => rule.SourceProductId)
            .ThenBy(rule => rule.RecommendedProductId)
            .ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.True(rule.CoOccurrenceCount >= 2);
            Assert.True(rule.Support >= 0.5m);
            Assert.True(rule.Confidence >= 0.6m);
            Assert.True(rule.Lift >= 1m);
        });
    }

    [Fact]
    public async Task Fbt_excludes_inactive_and_out_of_stock_targets()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var source = CreateProduct(21);
        var available = CreateProduct(22);
        var inactive = CreateProduct(23, isActive: false);
        var outOfStock = CreateProduct(24, isInStock: false);
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.AddRange(
                source,
                available,
                inactive,
                outOfStock);
            await setup.SaveChangesAsync();
            AddOrder(setup, source.ProductId, available.ProductId);
            AddOrder(setup, source.ProductId, inactive.ProductId);
            AddOrder(setup, source.ProductId, outOfStock.ProductId);
            await setup.SaveChangesAsync();
        }

        await using var factory = CreateFactory(
            database,
            LowThresholdConfiguration());
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        var recommendations = (await response.Content.ReadFromJsonAsync<
            List<FrequentlyBoughtTogetherRecommendationResponse>>())!;
        var recommendation = Assert.Single(recommendations);
        Assert.Equal(available.ProductId, recommendation.ProductId);

        await using var verification = database.CreateContext();
        Assert.False(await verification.ProductAffinities.AnyAsync(rule =>
            rule.RecommendedProductId == inactive.ProductId
            || rule.RecommendedProductId == outOfStock.ProductId));
    }

    [Fact]
    public async Task Fbt_limit_is_validated_capped_and_deterministically_ordered()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var source = CreateProduct(31);
        var targets = new[]
        {
            CreateProduct(32),
            CreateProduct(33),
            CreateProduct(34),
            CreateProduct(35)
        };
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.Add(source);
            setup.ProductSnapshots.AddRange(targets);
            await setup.SaveChangesAsync();
            AddOrder(
                setup,
                new[] { source.ProductId }
                    .Concat(targets.Select(product => product.ProductId))
                    .ToArray());
            await setup.SaveChangesAsync();
        }

        var configuration = LowThresholdConfiguration();
        configuration["Recommendation:FrequentlyBoughtTogether:DefaultLimit"] = "2";
        configuration["Recommendation:FrequentlyBoughtTogether:MaximumLimit"] = "3";
        await using var factory = CreateFactory(database, configuration);
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");
        using var defaultResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}");
        using var oneResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}?limit=1");
        using var cappedResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}?limit=1000");
        using var repeatedResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}?limit=1000");
        using var zeroResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}?limit=0");
        using var negativeResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/recommendations/fbt/{source.ProductId}?limit=-1");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        var defaults = await ReadRecommendationsAsync(defaultResponse);
        var one = await ReadRecommendationsAsync(oneResponse);
        var capped = await ReadRecommendationsAsync(cappedResponse);
        var repeated = await ReadRecommendationsAsync(repeatedResponse);
        Assert.Equal(2, defaults.Count);
        Assert.Single(one);
        Assert.Equal(3, capped.Count);
        Assert.Equal(HttpStatusCode.BadRequest, zeroResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativeResponse.StatusCode);
        Assert.Equal(
            capped.Select(item => item.ProductId),
            repeated.Select(item => item.ProductId));

        await using var verification = database.CreateContext();
        var expected = await verification.ProductAffinities
            .Where(rule => rule.SourceProductId == source.ProductId)
            .OrderByDescending(rule => rule.Score)
            .ThenByDescending(rule => rule.Confidence)
            .ThenBy(rule => rule.RecommendedProductId)
            .Take(3)
            .Select(rule => rule.RecommendedProductId)
            .ToArrayAsync();
        Assert.Equal(expected, capped.Select(item => item.ProductId));
        Assert.Equal(expected.Take(1), one.Select(item => item.ProductId));
    }

    [Fact]
    public async Task Fbt_recalculation_rejects_an_overlapping_execution()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var lockConnection = new SqlConnection(
            database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var acquireCommand = lockConnection.CreateCommand();
        acquireCommand.CommandText = """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = N'RecommendationRecalculation',
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0,
                @DbPrincipal = 'public';
            SELECT @Result;
            """;
        Assert.True(Convert.ToInt32(await acquireCommand.ExecuteScalarAsync()) >= 0);

        try
        {
            await using var factory = CreateFactory(database);
            using var client = CreateClient(factory);
            using var response = await SendAuthenticatedAsync(
                client,
                HttpMethod.Post,
                "/api/v1/recommendations/recalculate-fbt");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("Recommendation.RunAlreadyInProgress", error!.Code);
            Assert.False(error.Retryable);
        }
        finally
        {
            await using var releaseCommand = lockConnection.CreateCommand();
            releaseCommand.CommandText = """
                EXEC sys.sp_releaseapplock
                    @Resource = N'RecommendationRecalculation',
                    @LockOwner = 'Session',
                    @DbPrincipal = 'public';
                """;
            await releaseCommand.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Failed_fbt_recalculation_preserves_previous_rules()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var source = CreateProduct(41);
        var target = CreateProduct(42);
        var previousRun = CreateSucceededAffinityRun();
        await using (var setup = database.CreateContext())
        {
            setup.AddRange(source, target, previousRun);
            await setup.SaveChangesAsync();
            setup.ProductAffinities.Add(new ProductAffinity
            {
                SourceProductId = source.ProductId,
                RecommendedProductId = target.ProductId,
                CoOccurrenceCount = 1,
                SourceOrderCount = 1,
                RecommendedOrderCount = 1,
                TotalOrderCount = 1,
                Support = 1m,
                Confidence = 1m,
                Lift = 1m,
                Score = 0.750000m,
                RunId = previousRun.Id,
                CalculatedAtUtc = DateTime.UtcNow
            });
            AddOrder(setup, source.ProductId, target.ProductId);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [dbo].[TR_ProductAffinities_RejectReplacement]
                ON [dbo].[ProductAffinities]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51030, 'Injected affinity replacement failure.', 1;
                END;
                """);
        }

        await using var factory = CreateFactory(
            database,
            LowThresholdConfiguration());
        using var client = CreateClient(factory);
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate-fbt");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var verification = database.CreateContext();
        var retained = await verification.ProductAffinities.SingleAsync();
        Assert.Equal(previousRun.Id, retained.RunId);
        Assert.Equal(0.750000m, retained.Score);
        var failedRun = await verification.RecommendationRuns.SingleAsync(
            run => run.Id != previousRun.Id);
        Assert.Equal(RecommendationRunStatus.Failed, failedRun.Status);
        Assert.Equal(
            "Frequently bought together recalculation failed.",
            failedRun.ErrorMessage);
    }

    private static async Task SeedMetricDatasetAsync(
        RecommendationTestDatabase database,
        ProductSnapshot first,
        ProductSnapshot second,
        ProductSnapshot third)
    {
        await using var setup = database.CreateContext();
        setup.ProductSnapshots.AddRange(first, second, third);
        await setup.SaveChangesAsync();
        AddOrder(setup, first.ProductId, second.ProductId);
        AddOrder(setup, first.ProductId, second.ProductId, third.ProductId);
        AddOrder(setup, first.ProductId, third.ProductId);
        AddOrder(setup, second.ProductId);
        await setup.SaveChangesAsync();
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

    private static async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);
        return await client.SendAsync(request);
    }

    private static async Task<List<FrequentlyBoughtTogetherRecommendationResponse>>
        ReadRecommendationsAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<
                List<FrequentlyBoughtTogetherRecommendationResponse>>()
            ?? throw new InvalidOperationException(
                "Unable to deserialize FBT recommendations.");
    }

    private static ProductSnapshot CreateProduct(
        int key,
        bool isActive = true,
        bool isInStock = true)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse($"10000000-0000-0000-0000-{key:D12}"),
            Sku = $"FBT-{key:D4}",
            Name = $"FBT product {key}",
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

    private static void AddOrder(
        RecommendationDbContext context,
        params Guid[] productIds)
    {
        var distinctProductIds = productIds.Distinct().ToArray();
        var orderId = Guid.NewGuid();
        context.OrderSnapshots.Add(new OrderSnapshot
        {
            OrderId = orderId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            CustomerId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1),
            TotalQuantity = distinctProductIds.Length,
            DistinctProductCount = distinctProductIds.Length,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow
        });
        context.OrderSnapshotItems.AddRange(distinctProductIds.Select(
            productId => new OrderSnapshotItem
            {
                OrderId = orderId,
                ProductId = productId,
                Quantity = 1
            }));
    }

    private static Dictionary<string, string?> LowThresholdConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["Recommendation:FrequentlyBoughtTogether:MinimumPairOrderCount"] = "1",
            ["Recommendation:FrequentlyBoughtTogether:MinimumSupport"] = "0",
            ["Recommendation:FrequentlyBoughtTogether:MinimumConfidence"] = "0",
            ["Recommendation:FrequentlyBoughtTogether:MinimumLift"] = "0.000001"
        };
    }

    private static FbtCalculationSettings LowThresholdSettings()
    {
        return new FbtCalculationSettings(1, 0m, 0m, 0.000001m);
    }

    private static RecommendationRun CreateSucceededAffinityRun()
    {
        var startedAtUtc = DateTime.UtcNow.AddDays(-1);
        return new RecommendationRun
        {
            Id = Guid.NewGuid(),
            RunType = RecommendationRunType.Affinity,
            Status = RecommendationRunStatus.Succeeded,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc.AddMinutes(1),
            InputRecordCount = 2,
            OutputRecordCount = 1,
            ParametersJson = "{}",
            CorrelationId = Guid.NewGuid()
        };
    }
}
