using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class PopularityRecommendationHttpTests(
    RecommendationSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Popular_requires_authentication_and_empty_data_succeeds()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync(
            "/api/v1/recommendations/popular");
        using var authenticated = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/recommendations/popular");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Empty((await authenticated.Content
            .ReadFromJsonAsync<List<RecommendationResponse>>())!);
    }

    [Fact]
    public async Task Popularity_scores_quantity_orders_and_recency_and_filters_products()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTime.UtcNow;
        var quantityHigh = CreateProduct(1);
        var quantityLow = CreateProduct(2);
        var orderHigh = CreateProduct(3);
        var orderLow = CreateProduct(4);
        var recent = CreateProduct(5);
        var old = CreateProduct(6);
        var inactive = CreateProduct(7, isActive: false);
        var outOfStock = CreateProduct(8, isInStock: false);

        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.AddRange(
                quantityHigh,
                quantityLow,
                orderHigh,
                orderLow,
                recent,
                old,
                inactive,
                outOfStock);
            await setup.SaveChangesAsync();

            AddOrder(setup, now.AddMinutes(-2),
                (quantityHigh.ProductId, 10));
            AddOrder(setup, now.AddMinutes(-2),
                (quantityLow.ProductId, 1));
            AddOrder(setup, now.AddDays(-1),
                (orderHigh.ProductId, 1));
            AddOrder(setup, now.AddMinutes(-2),
                (orderHigh.ProductId, 1));
            AddOrder(setup, now.AddMinutes(-2),
                (orderLow.ProductId, 2));
            AddOrder(setup, now.AddMinutes(-1),
                (recent.ProductId, 1));
            AddOrder(setup, now.AddDays(-20),
                (old.ProductId, 1));
            AddOrder(setup, now.AddMinutes(-1),
                (inactive.ProductId, 100));
            AddOrder(setup, now.AddMinutes(-1),
                (outOfStock.ProductId, 100));
            await setup.SaveChangesAsync();
        }

        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate");
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/recommendations/popular?count=100");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content
            .ReadFromJsonAsync<List<RecommendationResponse>>())!;
        Assert.Equal(6, items.Count);
        Assert.Equal(items.Count, items.Select(item => item.ProductId).Distinct().Count());
        Assert.DoesNotContain(items, item => item.ProductId == inactive.ProductId);
        Assert.DoesNotContain(items, item => item.ProductId == outOfStock.ProductId);
        Assert.All(items, item => Assert.InRange(item.Score, 0m, 1m));
        Assert.All(items, item => Assert.Equal("Popular", item.RecommendationType));
        Assert.All(items, item => Assert.NotNull(item.Metrics));

        var byProduct = items.ToDictionary(item => item.ProductId);
        Assert.True(byProduct[quantityHigh.ProductId].Score
            > byProduct[quantityLow.ProductId].Score);
        Assert.True(byProduct[orderHigh.ProductId].Score
            > byProduct[orderLow.ProductId].Score);
        Assert.True(byProduct[recent.ProductId].Score
            > byProduct[old.ProductId].Score);

        await using var verification = database.CreateContext();
        var run = await verification.RecommendationRuns.SingleAsync();
        Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        Assert.Equal(6, run.OutputRecordCount);
        Assert.Equal(6, await verification.ProductPopularity.CountAsync());
    }

    [Fact]
    public async Task Popular_limit_is_capped_and_ties_have_stable_product_order()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.CreateContext())
        {
            var products = new[]
            {
                CreateProduct(101),
                CreateProduct(102),
                CreateProduct(103)
            };
            setup.ProductSnapshots.AddRange(products);
            await setup.SaveChangesAsync();
            AddOrder(
                setup,
                DateTime.UtcNow.AddMinutes(-1),
                products.Select(product => (product.ProductId, 1)).ToArray());
            await setup.SaveChangesAsync();
        }

        var configuration = new Dictionary<string, string?>
        {
            ["Recommendation:Popularity:DefaultLimit"] = "2",
            ["Recommendation:Popularity:MaximumLimit"] = "2"
        };
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey,
            configuration);
        using var client = CreateClient(factory);
        using var recalculation = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate");
        using var firstResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/recommendations/popular?count=500");
        using var secondResponse = await SendAuthenticatedAsync(
            client,
            HttpMethod.Get,
            "/api/v1/recommendations/popular?count=500");

        Assert.Equal(HttpStatusCode.OK, recalculation.StatusCode);
        var first = (await firstResponse.Content
            .ReadFromJsonAsync<List<RecommendationResponse>>())!;
        var second = (await secondResponse.Content
            .ReadFromJsonAsync<List<RecommendationResponse>>())!;
        Assert.Equal(2, first.Count);
        Assert.Equal(
            first.Select(item => item.ProductId),
            second.Select(item => item.ProductId));

        await using var verification = database.CreateContext();
        var expected = await verification.ProductPopularity
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ProductId)
            .Take(2)
            .Select(item => item.ProductId)
            .ToArrayAsync();
        Assert.Equal(expected, first.Select(item => item.ProductId));
    }

    [Fact]
    public async Task Recalculation_rejects_an_overlapping_execution()
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
            await using var factory = new RecommendationWebApplicationFactory(
                database.ConnectionString,
                ApiKey);
            using var client = CreateClient(factory);
            using var response = await SendAuthenticatedAsync(
                client,
                HttpMethod.Post,
                "/api/v1/recommendations/recalculate");

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
    public async Task Failed_recalculation_preserves_previous_popularity_results()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var product = CreateProduct(201);
        var previousRun = new RecommendationRun
        {
            Id = Guid.NewGuid(),
            RunType = RecommendationRunType.Popularity,
            Status = RecommendationRunStatus.Succeeded,
            StartedAtUtc = DateTime.UtcNow.AddDays(-1),
            CompletedAtUtc = DateTime.UtcNow.AddDays(-1).AddMinutes(1),
            InputRecordCount = 1,
            OutputRecordCount = 1,
            ParametersJson = "{}",
            CorrelationId = Guid.NewGuid()
        };
        await using (var setup = database.CreateContext())
        {
            setup.AddRange(product, previousRun);
            await setup.SaveChangesAsync();
            setup.ProductPopularity.Add(new ProductPopularity
            {
                ProductId = product.ProductId,
                WindowStartUtc = DateTime.UtcNow.AddDays(-30),
                WindowEndUtc = DateTime.UtcNow,
                SoldQuantity = 3,
                OrderCount = 1,
                Score = 0.750000m,
                RunId = previousRun.Id,
                CalculatedAtUtc = DateTime.UtcNow
            });
            AddOrder(setup, DateTime.UtcNow.AddMinutes(-1),
                (product.ProductId, 5));
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [dbo].[TR_ProductPopularity_RejectReplacement]
                ON [dbo].[ProductPopularity]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51020, 'Injected popularity replacement failure.', 1;
                END;
                """);
        }

        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        using var response = await SendAuthenticatedAsync(
            client,
            HttpMethod.Post,
            "/api/v1/recommendations/recalculate");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var verification = database.CreateContext();
        var retained = await verification.ProductPopularity.SingleAsync();
        Assert.Equal(previousRun.Id, retained.RunId);
        Assert.Equal(0.750000m, retained.Score);
        var failedRun = await verification.RecommendationRuns.SingleAsync(
            run => run.Id != previousRun.Id);
        Assert.Equal(RecommendationRunStatus.Failed, failedRun.Status);
        Assert.Equal("Popularity recalculation failed.", failedRun.ErrorMessage);
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

    private static ProductSnapshot CreateProduct(
        int key,
        bool isActive = true,
        bool isInStock = true)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse($"00000000-0000-0000-0000-{key:D12}"),
            Sku = $"POPULAR-{key:D4}",
            Name = $"Popularity product {key}",
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
        Recommendation.Api.Infrastructure.Persistence.RecommendationDbContext context,
        DateTime occurredAtUtc,
        params (Guid ProductId, int Quantity)[] items)
    {
        var orderId = Guid.NewGuid();
        var order = new OrderSnapshot
        {
            OrderId = orderId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            CustomerId = Guid.NewGuid(),
            OccurredAtUtc = occurredAtUtc,
            TotalQuantity = items.Sum(item => item.Quantity),
            DistinctProductCount = items.Length,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow
        };
        context.OrderSnapshots.Add(order);
        context.OrderSnapshotItems.AddRange(items.Select(item =>
            new OrderSnapshotItem
            {
                OrderId = orderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity
            }));
    }
}
