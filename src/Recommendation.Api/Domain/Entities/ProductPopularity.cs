namespace Recommendation.Api.Domain.Entities;

public sealed class ProductPopularity
{
    public Guid ProductId { get; set; }

    public DateTime WindowStartUtc { get; set; }

    public DateTime WindowEndUtc { get; set; }

    public int SoldQuantity { get; set; }

    public int OrderCount { get; set; }

    public decimal Score { get; set; }

    public Guid RunId { get; set; }

    public DateTime CalculatedAtUtc { get; set; }

    public ProductSnapshot Product { get; set; } = null!;

    public RecommendationRun Run { get; set; } = null!;
}
