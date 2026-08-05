using System.Text.Json;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationModelTrainingSnapshotSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    [Fact]
    public async Task Training_snapshot_is_built_from_recommendation_db_without_pii()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var first = Product(1);
        var second = Product(2);
        var customerId = Guid.Parse(
            "90000000-0000-0000-0000-000000000001");
        var order = new OrderSnapshot
        {
            OrderId = Guid.Parse(
                "80000000-0000-0000-0000-000000000001"),
            OrderNumber = "ORD-MODEL-0001",
            CustomerId = customerId,
            SubjectId = $"v1.{new string('A', 43)}",
            OccurredAtUtc = DateTime.UtcNow,
            TotalQuantity = 3,
            DistinctProductCount = 2,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow
        };
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.AddRange(first, second);
            setup.OrderSnapshots.Add(order);
            setup.OrderSnapshotItems.AddRange(
                new OrderSnapshotItem
                {
                    OrderId = order.OrderId,
                    ProductId = first.ProductId,
                    Quantity = 1
                },
                new OrderSnapshotItem
                {
                    OrderId = order.OrderId,
                    ProductId = second.ProductId,
                    Quantity = 2
                });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var store = new SqlServerModelTrainingSnapshotStore(context);
        var snapshot = await store.GetTrainingSnapshotAsync();
        var request = RecommendationModelOrchestrationService.CreateRequest(
            snapshot,
            "tfidf-test-v1",
            Guid.NewGuid());
        var json = JsonSerializer.Serialize(request);

        Assert.Equal(2, request.Products.Count);
        Assert.Single(request.Interactions);
        Assert.Equal(2, request.Interactions[0].Items.Count);
        Assert.Equal(order.SubjectId, request.Interactions[0].SubjectId);
        Assert.Contains(first.Description!, json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            customerId.ToString(),
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderNumber", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Training_snapshot_requires_completed_subject_backfill()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var product = Product(1);
        var order = new OrderSnapshot
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-MODEL-MISSING-SUBJECT",
            CustomerId = Guid.NewGuid(),
            SubjectId = null,
            OccurredAtUtc = DateTime.UtcNow,
            TotalQuantity = 1,
            DistinctProductCount = 1,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow
        };
        await using (var setup = database.CreateContext())
        {
            setup.AddRange(
                product,
                order,
                new OrderSnapshotItem
                {
                    OrderId = order.OrderId,
                    ProductId = product.ProductId,
                    Quantity = 1
                });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var store = new SqlServerModelTrainingSnapshotStore(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetTrainingSnapshotAsync());

        Assert.Contains(
            "SubjectId backfill",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ProductSnapshot Product(int key)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse(
                $"00000000-0000-0000-0000-{key:D12}"),
            Sku = $"MODEL-{key:D4}",
            Name = $"Model product {key}",
            Description = $"Model product {key} description",
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Price = 10m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = true,
            IsInStock = true,
            SourceUpdatedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }
}
