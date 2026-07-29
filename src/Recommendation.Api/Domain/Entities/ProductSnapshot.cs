using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Domain.Entities;

public sealed class ProductSnapshot
{
    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }

    public Guid? ParentCategoryId { get; set; }

    public Guid BrandId { get; set; }

    public decimal Price { get; set; }

    public decimal NetContent { get; set; }

    public UnitType UnitType { get; set; }

    public bool IsActive { get; set; }

    public bool IsInStock { get; set; }

    public DateTime SourceUpdatedAtUtc { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrderSnapshotItem> OrderSnapshotItems { get; } = [];

    public ICollection<ProductAffinity> SourceAffinities { get; } = [];

    public ICollection<ProductAffinity> RecommendedAffinities { get; } = [];

    public ICollection<ProductSimilarity> SourceSimilarities { get; } = [];

    public ICollection<ProductSimilarity> RecommendedSimilarities { get; } = [];

    public ProductPopularity? Popularity { get; set; }
}
