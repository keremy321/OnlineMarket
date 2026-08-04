using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Tests;

internal static class RecommendationEventTestData
{
    public static readonly DateTime BaseUtc =
        new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    public static ProductSnapshotChangedV1Request CreateProduct(
        Guid? eventId = null,
        Guid? productId = null,
        string? sku = null,
        string? name = null,
        DateTime? sourceUpdatedAtUtc = null)
    {
        return new ProductSnapshotChangedV1Request
        {
            EventId = eventId ?? Guid.NewGuid(),
            OccurredAtUtc = BaseUtc,
            CorrelationId = Guid.NewGuid(),
            ProductId = productId ?? Guid.NewGuid(),
            Sku = sku ?? $"SKU-{Guid.NewGuid():N}",
            Name = name ?? "Recommendation product",
            CategoryId = Guid.NewGuid(),
            ParentCategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Price = 42.50m,
            NetContent = 1.250m,
            UnitType = UnitType.Kilogram,
            IsActive = true,
            IsInStock = true,
            SourceUpdatedAtUtc = sourceUpdatedAtUtc ?? BaseUtc
        };
    }

    public static OrderConfirmedForRecommendationV1Request CreateOrder(
        params (Guid ProductId, int Quantity)[] items)
    {
        return new OrderConfirmedForRecommendationV1Request
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = BaseUtc,
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            CustomerId = Guid.NewGuid(),
            Items = items
                .Select(item => new RecommendationOrderItemV1Request
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity
                })
                .ToArray()
        };
    }
}
