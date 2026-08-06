namespace OnlineMarket.Web.Application.Models;

/// <summary>
/// Inbound chat request. Only <see cref="CurrentProductId"/> and <see cref="PageType"/>
/// carry page context, and both are re-validated server-side before use. Product,
/// cart and customer data supplied by the browser is never trusted.
/// </summary>
public record AiSupportRequestDto(
    string Message,
    string? ConversationId = null,
    string? CurrentUrl = null,
    List<AiChatMessageDto>? History = null,
    Guid? CurrentProductId = null,
    string? PageType = null);

/// <summary>
/// Assistant response. <see cref="Products"/> is always built server-side from
/// Recommendation.Api results joined with current CatalogService data; the external
/// provider can only influence <see cref="Reply"/>.
/// </summary>
public record AiSupportResponseDto(
    string Reply,
    string ConversationId,
    DateTime TimestampUtc,
    List<string>? SuggestedActions = null,
    bool Success = true,
    string? ErrorMessage = null,
    string Intent = nameof(AiAssistantIntent.Unknown),
    bool UsedFallback = true,
    List<AiRecommendedProductDto>? Products = null);

/// <summary>
/// A single recommendation card. Sourced from Recommendation.Api plus the current
/// OnlineMarketDb catalog snapshot — never from provider output.
/// </summary>
public sealed record AiRecommendedProductDto(
    Guid Id,
    string Name,
    decimal Price,
    string ImageUrl,
    string DetailsUrl,
    string? Reason,
    string CategoryName,
    string BrandName);

public record AiChatMessageDto(
    string Sender,
    string Text,
    DateTime TimestampUtc);
