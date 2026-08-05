namespace Recommendation.Api.Application.Options;

public sealed class SimilarRecommendationOptions
{
    public const string SectionName = "Recommendation:Similar";
    public const int AbsoluteMaximumLimit = 100;

    public int DefaultLimit { get; init; } = 10;

    public int MaximumLimit { get; init; } = 50;

    public bool IsValid()
    {
        return DefaultLimit > 0
            && MaximumLimit >= DefaultLimit
            && MaximumLimit <= AbsoluteMaximumLimit;
    }
}
