using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Interfaces;

/// <summary>
/// Deterministic intent classification for the assistant. Implementations must map a
/// message onto the approved <see cref="AiAssistantIntent"/> set without consulting an
/// external provider.
/// </summary>
public interface IAiIntentRouter
{
    AiAssistantIntent Route(string? message);
}

/// <summary>
/// Safe page context accepted from the browser plus the server-resolved identity.
/// </summary>
/// <param name="CustomerId">Resolved server-side from the authentication cookie; never from the browser body.</param>
/// <param name="CurrentProductId">Browser-supplied hint. Always re-validated against CatalogService.</param>
/// <param name="Message">The sanitized shopper message, used to resolve a named product.</param>
public sealed record AiAssistantContext(
    Guid? CustomerId,
    Guid? CurrentProductId,
    string Message);

public enum AiRecommendationStatus
{
    /// <summary>Recommendation.Api returned at least one product that passed catalog validation.</summary>
    Success,

    /// <summary>Recommendation.Api answered, but nothing suitable remained. No product may be invented.</summary>
    Empty,

    /// <summary>The requested product could not be resolved unambiguously; ask the shopper.</summary>
    NeedsProductClarification,

    /// <summary>The intent requires a signed-in shopper (cart completion).</summary>
    NeedsAuthentication,

    /// <summary>The signed-in shopper has an empty cart.</summary>
    EmptyCart,

    /// <summary>Recommendation.Api could not be reached.</summary>
    Unavailable
}

/// <param name="ResolvedIntent">The intent actually served (for example GeneralRecommendation resolves to Personalized or Popular).</param>
public sealed record AiRecommendationOutcome(
    AiAssistantIntent ResolvedIntent,
    AiRecommendationStatus Status,
    IReadOnlyList<AiRecommendedProductDto> Products,
    string? ResolvedProductName = null);

/// <summary>
/// Maps an approved intent onto the existing Recommendation.Api client and rebuilds
/// every returned product from current OnlineMarketDb catalog data.
/// </summary>
public interface IAiRecommendationOrchestrator
{
    Task<AiRecommendationOutcome> ResolveAsync(
        AiAssistantIntent intent,
        AiAssistantContext context,
        CancellationToken cancellationToken = default);
}
