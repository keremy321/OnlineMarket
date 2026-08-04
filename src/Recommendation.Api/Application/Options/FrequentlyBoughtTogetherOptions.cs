namespace Recommendation.Api.Application.Options;

public sealed class FrequentlyBoughtTogetherOptions
{
    public const string SectionName =
        "Recommendation:FrequentlyBoughtTogether";
    public const int AbsoluteMaximumLimit = 100;

    public int DefaultLimit { get; init; } = 10;

    public int MaximumLimit { get; init; } = 50;

    public int MinimumPairOrderCount { get; init; } = 2;

    public decimal MinimumSupport { get; init; } = 0.01m;

    public decimal MinimumConfidence { get; init; } = 0.05m;

    public decimal MinimumLift { get; init; } = 1m;

    public bool IsValid()
    {
        return DefaultLimit > 0
            && MaximumLimit >= DefaultLimit
            && MaximumLimit <= AbsoluteMaximumLimit
            && MinimumPairOrderCount > 0
            && MinimumSupport is >= 0 and <= 1
            && MinimumConfidence is >= 0 and <= 1
            && MinimumLift > 0;
    }
}
