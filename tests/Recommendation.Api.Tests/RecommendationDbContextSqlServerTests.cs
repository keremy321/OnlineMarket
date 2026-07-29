using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationDbContextSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    private static readonly string[] ExpectedTables =
    [
        "CustomerPreferenceScores",
        "OrderSnapshotItems",
        "OrderSnapshots",
        "ProcessedEvents",
        "ProductAffinities",
        "ProductPopularity",
        "ProductSimilarities",
        "ProductSnapshots",
        "RecommendationRuns"
    ];

    [Fact]
    public async Task Empty_database_can_apply_all_recommendation_migrations()
    {
        await using var database = await fixture.CreateDatabaseAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Expected_tables_exist()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var actualTables = new HashSet<string>(StringComparer.Ordinal);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.tables
            WHERE [schema_id] = SCHEMA_ID(N'dbo')
              AND [name] <> N'__EFMigrationsHistory';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        Assert.True(
            actualTables.SetEquals(ExpectedTables),
            $"Unexpected table set: {string.Join(", ", actualTables.Order())}");
    }

    [Fact]
    public async Task Unique_product_snapshot_sku_is_enforced()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        const string duplicateSku = "DUPLICATE-SKU";

        context.ProductSnapshots.AddRange(
            CreateProduct(duplicateSku),
            CreateProduct(duplicateSku));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_price_cannot_be_negative()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        product.Price = -0.01m;
        context.ProductSnapshots.Add(product);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_net_content_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        product.NetContent = 0;
        context.ProductSnapshots.Add(product);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_total_quantity_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder();
        order.TotalQuantity = 0;
        context.OrderSnapshots.Add(order);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_distinct_product_count_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder();
        order.DistinctProductCount = 0;
        context.OrderSnapshots.Add(order);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_snapshot_item_quantity_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        var order = CreateOrder();
        context.AddRange(product, order);
        await context.SaveChangesAsync();

        context.OrderSnapshotItems.Add(new OrderSnapshotItem
        {
            OrderId = order.OrderId,
            ProductId = product.ProductId,
            Quantity = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Missing_product_snapshot_prevents_order_snapshot_item_insert()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder();
        context.OrderSnapshots.Add(order);
        await context.SaveChangesAsync();

        context.OrderSnapshotItems.Add(new OrderSnapshotItem
        {
            OrderId = order.OrderId,
            ProductId = Guid.NewGuid(),
            Quantity = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_affinity_cannot_recommend_itself()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        var run = CreateRun();
        context.AddRange(product, run);
        await context.SaveChangesAsync();

        context.ProductAffinities.Add(
            CreateAffinity(product.ProductId, product.ProductId, run.Id));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    public async Task Affinity_support_and_confidence_bounds_are_enforced(
        double support,
        double confidence)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var source = CreateProduct();
        var recommended = CreateProduct();
        var run = CreateRun();
        context.AddRange(source, recommended, run);
        await context.SaveChangesAsync();

        var affinity = CreateAffinity(
            source.ProductId,
            recommended.ProductId,
            run.Id);
        affinity.Support = (decimal)support;
        affinity.Confidence = (decimal)confidence;
        context.ProductAffinities.Add(affinity);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Affinity_lift_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var source = CreateProduct();
        var recommended = CreateProduct();
        var run = CreateRun();
        context.AddRange(source, recommended, run);
        await context.SaveChangesAsync();

        var affinity = CreateAffinity(
            source.ProductId,
            recommended.ProductId,
            run.Id);
        affinity.Lift = 0;
        context.ProductAffinities.Add(affinity);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_similarity_cannot_target_itself()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        var run = CreateRun();
        context.AddRange(product, run);
        await context.SaveChangesAsync();

        context.ProductSimilarities.Add(
            CreateSimilarity(product.ProductId, product.ProductId, run.Id));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0.10, 0.10, 0.10, 0.10, 0.40)]
    [InlineData(0.20, 0.05, 0.10, 0.10, 0.45)]
    [InlineData(0.20, 0.10, 0.16, 0.10, 0.56)]
    [InlineData(0.20, 0.10, 0.10, 0.11, 0.51)]
    public async Task Similarity_component_bounds_are_enforced(
        double categoryScore,
        double brandScore,
        double priceScore,
        double amountScore,
        double similarityScore)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var source = CreateProduct();
        var recommended = CreateProduct();
        var run = CreateRun();
        context.AddRange(source, recommended, run);
        await context.SaveChangesAsync();

        var similarity = CreateSimilarity(
            source.ProductId,
            recommended.ProductId,
            run.Id);
        similarity.CategoryScore = (decimal)categoryScore;
        similarity.BrandScore = (decimal)brandScore;
        similarity.PriceScore = (decimal)priceScore;
        similarity.AmountScore = (decimal)amountScore;
        similarity.SimilarityScore = (decimal)similarityScore;
        context.ProductSimilarities.Add(similarity);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Similarity_total_must_equal_its_components()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var source = CreateProduct();
        var recommended = CreateProduct();
        var run = CreateRun();
        context.AddRange(source, recommended, run);
        await context.SaveChangesAsync();

        var similarity = CreateSimilarity(
            source.ProductId,
            recommended.ProductId,
            run.Id);
        similarity.SimilarityScore = 0.60m;
        context.ProductSimilarities.Add(similarity);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0, 0.5, 1, 1)]
    [InlineData(1, 1.01, 1, 1)]
    [InlineData(1, 0.5, -1, 1)]
    public async Task Popularity_window_counts_and_score_constraints_are_enforced(
        int windowLengthDays,
        double score,
        int soldQuantity,
        int orderCount)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var product = CreateProduct();
        var run = CreateRun();
        context.AddRange(product, run);
        await context.SaveChangesAsync();

        var windowStart = DateTime.UtcNow.AddDays(-1);
        context.ProductPopularity.Add(new ProductPopularity
        {
            ProductId = product.ProductId,
            WindowStartUtc = windowStart,
            WindowEndUtc = windowStart.AddDays(windowLengthDays),
            SoldQuantity = soldQuantity,
            OrderCount = orderCount,
            Score = (decimal)score,
            RunId = run.Id,
            CalculatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-0.01, 0.5, 0.5)]
    [InlineData(0.5, 1.01, 0.5)]
    [InlineData(0.5, 0.5, 1.01)]
    public async Task Customer_preference_score_bounds_are_enforced(
        double frequencyScore,
        double recencyScore,
        double score)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        context.CustomerPreferenceScores.Add(new CustomerPreferenceScore
        {
            CustomerId = Guid.NewGuid(),
            PreferenceType = PreferenceType.Category,
            ReferenceId = Guid.NewGuid(),
            PurchaseCount = 1,
            TotalQuantity = 1,
            LastPurchasedAtUtc = DateTime.UtcNow,
            FrequencyScore = (decimal)frequencyScore,
            RecencyScore = (decimal)recencyScore,
            Score = (decimal)score,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Processed_event_id_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var eventId = Guid.NewGuid();
        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Recommendation_run_parameters_json_must_be_valid()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var run = CreateRun();
        run.ParametersJson = "not-json";
        context.RecommendationRuns.Add(run);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Recommendation_run_completion_cannot_precede_start()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var run = CreateRun();
        run.CompletedAtUtc = run.StartedAtUtc.AddMilliseconds(-1);
        context.RecommendationRuns.Add(run);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_row_version_is_a_sql_server_concurrency_token()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        Guid productId;
        byte[] initialRowVersion;

        await using (var seedContext = database.CreateContext())
        {
            var product = CreateProduct();
            productId = product.ProductId;
            seedContext.ProductSnapshots.Add(product);
            await seedContext.SaveChangesAsync();
            initialRowVersion = product.RowVersion.ToArray();
        }

        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstProduct = await firstContext.ProductSnapshots.SingleAsync(
            product => product.ProductId == productId);
        var staleProduct = await staleContext.ProductSnapshots.SingleAsync(
            product => product.ProductId == productId);

        firstProduct.Name = "First update";
        await firstContext.SaveChangesAsync();

        Assert.NotEqual(initialRowVersion, firstProduct.RowVersion);

        staleProduct.Name = "Stale update";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
    }

    private static ProductSnapshot CreateProduct(string? sku = null)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.NewGuid(),
            Sku = sku ?? $"SKU-{Guid.NewGuid():N}",
            Name = "Recommendation product snapshot",
            CategoryId = Guid.NewGuid(),
            ParentCategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Price = 10.00m,
            NetContent = 1.000m,
            UnitType = UnitType.Piece,
            IsActive = true,
            IsInStock = true,
            SourceUpdatedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    private static OrderSnapshot CreateOrder()
    {
        return new OrderSnapshot
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            CustomerId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            TotalQuantity = 1,
            DistinctProductCount = 1,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    private static RecommendationRun CreateRun()
    {
        return new RecommendationRun
        {
            Id = Guid.NewGuid(),
            RunType = RecommendationRunType.Full,
            Status = RecommendationRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            ParametersJson = "{}",
            CorrelationId = Guid.NewGuid()
        };
    }

    private static ProductAffinity CreateAffinity(
        Guid sourceProductId,
        Guid recommendedProductId,
        Guid runId)
    {
        return new ProductAffinity
        {
            SourceProductId = sourceProductId,
            RecommendedProductId = recommendedProductId,
            CoOccurrenceCount = 1,
            SourceOrderCount = 1,
            RecommendedOrderCount = 1,
            TotalOrderCount = 1,
            Support = 1,
            Confidence = 1,
            Lift = 1,
            Score = 1,
            RunId = runId,
            CalculatedAtUtc = DateTime.UtcNow
        };
    }

    private static ProductSimilarity CreateSimilarity(
        Guid sourceProductId,
        Guid recommendedProductId,
        Guid runId)
    {
        return new ProductSimilarity
        {
            SourceProductId = sourceProductId,
            RecommendedProductId = recommendedProductId,
            CategoryScore = 0.20m,
            BrandScore = 0.10m,
            PriceScore = 0.10m,
            AmountScore = 0.10m,
            SimilarityScore = 0.50m,
            RunId = runId,
            CalculatedAtUtc = DateTime.UtcNow
        };
    }

    private static ProcessedEvent CreateProcessedEvent(Guid eventId)
    {
        return new ProcessedEvent
        {
            EventId = eventId,
            EventType = "OrderConfirmedForRecommendationV1",
            PayloadHash = new string('A', 64),
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = DateTime.UtcNow
        };
    }
}
