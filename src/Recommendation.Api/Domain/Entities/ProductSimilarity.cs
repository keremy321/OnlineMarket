namespace Recommendation.Api.Domain.Entities;

public sealed class ProductSimilarity
{
    public Guid SourceProductId { get; set; }

    public Guid RecommendedProductId { get; set; }

    public decimal CategoryScore { get; set; }

    public decimal BrandScore { get; set; }

    public decimal PriceScore { get; set; }

    public decimal AmountScore { get; set; }

    public decimal SimilarityScore { get; set; }

    public Guid RunId { get; set; }

    public DateTime CalculatedAtUtc { get; set; }

    public ProductSnapshot SourceProduct { get; set; } = null!;

    public ProductSnapshot RecommendedProduct { get; set; } = null!;

    public RecommendationRun Run { get; set; } = null!;
}
