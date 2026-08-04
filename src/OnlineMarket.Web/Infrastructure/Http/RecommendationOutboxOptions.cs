namespace OnlineMarket.Web.Infrastructure.Http;

public sealed class RecommendationOutboxOptions
{
    public const string SectionName = "Services:RecommendationOutbox";
    public const string ApiKeyHeaderName = "X-Api-Key";

    public string ApiKey { get; init; } = string.Empty;
}
