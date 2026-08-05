using System.Text.Json.Serialization;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Contracts;

public sealed record ProductSnapshotChangedV1Request
{
    [JsonRequired]
    public Guid EventId { get; init; }

    [JsonRequired]
    public DateTime OccurredAtUtc { get; init; }

    [JsonRequired]
    public Guid CorrelationId { get; init; }

    [JsonRequired]
    public Guid ProductId { get; init; }

    [JsonRequired]
    public string? Sku { get; init; }

    [JsonRequired]
    public string? Name { get; init; }

    public string? Description { get; init; }

    [JsonRequired]
    public Guid CategoryId { get; init; }

    public Guid? ParentCategoryId { get; init; }

    [JsonRequired]
    public Guid BrandId { get; init; }

    [JsonRequired]
    public decimal Price { get; init; }

    [JsonRequired]
    public decimal NetContent { get; init; }

    [JsonRequired]
    public UnitType UnitType { get; init; }

    [JsonRequired]
    public bool IsActive { get; init; }

    [JsonRequired]
    public bool IsInStock { get; init; }

    [JsonRequired]
    public DateTime SourceUpdatedAtUtc { get; init; }
}

public sealed record OrderConfirmedForRecommendationV1Request
{
    [JsonRequired]
    public Guid EventId { get; init; }

    [JsonRequired]
    public DateTime OccurredAtUtc { get; init; }

    [JsonRequired]
    public Guid CorrelationId { get; init; }

    [JsonRequired]
    public Guid OrderId { get; init; }

    [JsonRequired]
    public string? OrderNumber { get; init; }

    [JsonRequired]
    public Guid CustomerId { get; init; }

    [JsonRequired]
    public IReadOnlyList<RecommendationOrderItemV1Request?>? Items { get; init; }
}

public sealed record RecommendationOrderItemV1Request
{
    [JsonRequired]
    public Guid ProductId { get; init; }

    [JsonRequired]
    public int Quantity { get; init; }
}
