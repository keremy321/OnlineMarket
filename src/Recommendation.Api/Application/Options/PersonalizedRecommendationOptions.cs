namespace Recommendation.Api.Application.Options;

public sealed class PersonalizedRecommendationOptions
{
    public const string SectionName = "Recommendation:Personalized";
    public const int AbsoluteMaximumLimit = 100;

    public int DefaultLimit { get; init; } = 8;

    public int MaximumLimit { get; init; } = 50;

    public PersonalizedModelStrategy ModelStrategy { get; init; }
        = PersonalizedModelStrategy.Als;

    public decimal CategoryAffinityWeight { get; init; } = 0.50m;

    public decimal BrandAffinityWeight { get; init; } = 0.20m;

    public decimal PopularityWeight { get; init; } = 0.30m;

    public bool IsValid()
    {
        return DefaultLimit > 0
            && MaximumLimit >= DefaultLimit
            && MaximumLimit <= AbsoluteMaximumLimit
            && Enum.IsDefined(ModelStrategy)
            && CategoryAffinityWeight >= 0m
            && BrandAffinityWeight >= 0m
            && PopularityWeight >= 0m
            && CategoryAffinityWeight
                + BrandAffinityWeight
                + PopularityWeight == 1m;
    }
}

public enum PersonalizedModelStrategy
{
    Als,
    Hybrid
}
