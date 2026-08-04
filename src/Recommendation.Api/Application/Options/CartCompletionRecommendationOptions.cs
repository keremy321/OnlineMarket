namespace Recommendation.Api.Application.Options;

public sealed class CartCompletionRecommendationOptions
{
    public const string SectionName = "Recommendation:CartCompletion";
    public const int AbsoluteMaximumLimit = 100;

    public int DefaultLimit { get; init; } = 10;

    public int MaximumLimit { get; init; } = 50;

    public decimal AffinityScoreWeight { get; init; } = 0.45m;

    public decimal ConfidenceWeight { get; init; } = 0.25m;

    public decimal LiftWeight { get; init; } = 0.15m;

    public decimal SupportingProductWeight { get; init; } = 0.15m;

    public bool IsValid()
    {
        return DefaultLimit > 0
            && MaximumLimit >= DefaultLimit
            && MaximumLimit <= AbsoluteMaximumLimit
            && AffinityScoreWeight is >= 0m and <= 1m
            && ConfidenceWeight is >= 0m and <= 1m
            && LiftWeight is >= 0m and <= 1m
            && SupportingProductWeight is >= 0m and <= 1m
            && AffinityScoreWeight
                + ConfidenceWeight
                + LiftWeight
                + SupportingProductWeight > 0m;
    }
}
