namespace OnlineMarket.Web.Infrastructure.Http;

/// <summary>
/// Bounds the per-request read timeout used by the storefront's Recommendation.Api
/// client for reads that need more than a fast-path budget: Similar, Personalized and
/// FrequentlyBoughtTogether. Popular and CartCompletion intentionally keep their own
/// short, independent budget and are out of scope for this option.
/// </summary>
public sealed class RecommendationApiClientOptions
{
    public const string SectionName = "Services:RecommendationApiClient";

    public const int MinimumReadTimeoutSeconds = 1;
    public const int MaximumReadTimeoutSeconds = 8;

    /// <summary>
    /// Matches the value Similar and Personalized reads have used reliably; also the
    /// value FrequentlyBoughtTogether now shares instead of its previous isolated 500 ms
    /// budget.
    /// </summary>
    public const int DefaultReadTimeoutSeconds = 4;

    public int ReadTimeoutSeconds { get; set; } = DefaultReadTimeoutSeconds;

    public TimeSpan EffectiveReadTimeout => TimeSpan.FromSeconds(
        Math.Clamp(ReadTimeoutSeconds, MinimumReadTimeoutSeconds, MaximumReadTimeoutSeconds));
}
