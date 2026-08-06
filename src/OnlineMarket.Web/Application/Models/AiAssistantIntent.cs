namespace OnlineMarket.Web.Application.Models;

/// <summary>
/// The closed set of intents the assistant may act on. Any value produced by the
/// external provider must map onto one of these members or it is discarded.
/// </summary>
public enum AiAssistantIntent
{
    Unknown = 0,

    /// <summary>"Bana ürün öner" — resolves to Personalized or Popular server-side.</summary>
    GeneralRecommendation,

    /// <summary>Recommendation.Api personalized endpoint (authenticated customers).</summary>
    Personalized,

    /// <summary>Recommendation.Api popular endpoint.</summary>
    Popular,

    /// <summary>Recommendation.Api similar-products endpoint.</summary>
    Similar,

    /// <summary>Recommendation.Api frequently-bought-together endpoint.</summary>
    FrequentlyBoughtTogether,

    /// <summary>Recommendation.Api cart-completion endpoint.</summary>
    CartCompletion,

    OrderStatus,
    Shipping,
    Payment,
    Returns,
    Greeting,
    StoreHelp
}

public static class AiAssistantIntentExtensions
{
    /// <summary>
    /// True when the intent must be answered with Recommendation.Api results.
    /// </summary>
    public static bool IsRecommendationIntent(this AiAssistantIntent intent) =>
        intent is AiAssistantIntent.GeneralRecommendation
            or AiAssistantIntent.Personalized
            or AiAssistantIntent.Popular
            or AiAssistantIntent.Similar
            or AiAssistantIntent.FrequentlyBoughtTogether
            or AiAssistantIntent.CartCompletion;

    /// <summary>
    /// Maps an untrusted provider-supplied label onto the approved enum. Anything
    /// outside the approved set becomes <see cref="AiAssistantIntent.Unknown"/>.
    /// </summary>
    public static AiAssistantIntent ParseApproved(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AiAssistantIntent.Unknown;
        }

        var candidate = value.Trim();
        if (!Enum.TryParse<AiAssistantIntent>(candidate, ignoreCase: true, out var intent))
        {
            return AiAssistantIntent.Unknown;
        }

        return Enum.IsDefined(intent) ? intent : AiAssistantIntent.Unknown;
    }
}
