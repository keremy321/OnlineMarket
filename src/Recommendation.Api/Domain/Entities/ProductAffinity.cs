namespace Recommendation.Api.Domain.Entities;

public sealed class ProductAffinity
{
    public Guid SourceProductId { get; set; }

    public Guid RecommendedProductId { get; set; }

    public int CoOccurrenceCount { get; set; }

    public int SourceOrderCount { get; set; }

    public int RecommendedOrderCount { get; set; }

    public int TotalOrderCount { get; set; }

    public decimal Support { get; set; }

    public decimal Confidence { get; set; }

    public decimal Lift { get; set; }

    public decimal Score { get; set; }

    public Guid RunId { get; set; }

    public DateTime CalculatedAtUtc { get; set; }

    public ProductSnapshot SourceProduct { get; set; } = null!;

    public ProductSnapshot RecommendedProduct { get; set; } = null!;

    public RecommendationRun Run { get; set; } = null!;
}
