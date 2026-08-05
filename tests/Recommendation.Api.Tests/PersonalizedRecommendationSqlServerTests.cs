using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class PersonalizedRecommendationSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    [Fact]
    public async Task Store_returns_customer_history_and_current_product_state()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var customerId = Guid.NewGuid();
        var purchased = Product(1, isActive: true, isInStock: true);
        var inactive = Product(2, isActive: false, isInStock: true);
        var order = new OrderSnapshot
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "PERSONALIZED-STORE-0001",
            CustomerId = customerId,
            SubjectId = $"v1.{new string('A', 43)}",
            OccurredAtUtc = RecommendationEventTestData.BaseUtc,
            TotalQuantity = 3,
            DistinctProductCount = 1,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc
        };
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.AddRange(purchased, inactive);
            setup.OrderSnapshots.Add(order);
            setup.OrderSnapshotItems.Add(new OrderSnapshotItem
            {
                OrderId = order.OrderId,
                ProductId = purchased.ProductId,
                Quantity = 3
            });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var store = new SqlServerPersonalizedRecommendationStore(context);

        var result = await store.GetContextAsync(customerId);
        var unknown = await store.GetContextAsync(Guid.NewGuid());

        Assert.Equal(2, result.Products.Count);
        Assert.False(result.Products.Single(
            product => product.ProductId == inactive.ProductId).IsActive);
        var purchase = Assert.Single(result.Purchases);
        Assert.Equal(purchased.ProductId, purchase.ProductId);
        Assert.Equal(3, purchase.Quantity);
        Assert.Empty(unknown.Purchases);
    }

    private static ProductSnapshot Product(
        int key,
        bool isActive,
        bool isInStock)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse(
                $"00000000-0000-0000-0000-{key:D12}"),
            Sku = $"PERSONALIZED-{key:D4}",
            Name = $"Personalized product {key}",
            CategoryId = Guid.Parse(
                $"10000000-0000-0000-0000-{key:D12}"),
            BrandId = Guid.Parse(
                $"20000000-0000-0000-0000-{key:D12}"),
            Price = 10m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = isActive,
            IsInStock = isInStock,
            SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc,
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc
        };
    }
}
