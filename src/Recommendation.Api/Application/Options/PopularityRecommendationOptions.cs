namespace Recommendation.Api.Application.Options;

public sealed class PopularityRecommendationOptions
{
    public const string SectionName = "Recommendation:Popularity";
    public const int AbsoluteMaximumLimit = 100;

    public int WindowDays { get; init; } = 30;

    public int DefaultLimit { get; init; } = 10;

    public int MaximumLimit { get; init; } = 50;

    public decimal SoldQuantityWeight { get; init; } = 0.50m;

    public decimal DistinctOrderWeight { get; init; } = 0.30m;

    public decimal RecencyWeight { get; init; } = 0.20m;

    public bool IsValid()
    {
        return WindowDays is >= 1 and <= 365
            && DefaultLimit > 0
            && MaximumLimit >= DefaultLimit
            && MaximumLimit <= AbsoluteMaximumLimit
            && SoldQuantityWeight >= 0
            && DistinctOrderWeight >= 0
            && RecencyWeight >= 0
            && SoldQuantityWeight
                + DistinctOrderWeight
                + RecencyWeight == 1m;
    }
}
