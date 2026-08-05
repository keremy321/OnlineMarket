namespace Recommendation.Api.Application.Options;

public sealed class RecommendationModelServiceOptions
{
    public const string SectionName =
        "Services:RecommendationModelService";

    public string BaseAddress { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan EvaluationTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public bool IsValid()
    {
        return Uri.TryCreate(
                BaseAddress,
                UriKind.Absolute,
                out var baseAddress)
            && baseAddress.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(ApiKey)
            && Timeout >= TimeSpan.FromMilliseconds(100)
            && Timeout <= TimeSpan.FromSeconds(30)
            && EvaluationTimeout >= TimeSpan.FromSeconds(1)
            && EvaluationTimeout <= TimeSpan.FromMinutes(30)
            && EvaluationTimeout > Timeout;
    }
}
