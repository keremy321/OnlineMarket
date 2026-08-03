namespace OnlineMarket.Web.Application.Models;

public record AiSupportRequestDto(
    string Message,
    string? ConversationId = null,
    string? CurrentUrl = null);

public record AiSupportResponseDto(
    string Reply,
    string ConversationId,
    DateTime TimestampUtc,
    List<string>? SuggestedActions = null,
    bool Success = true,
    string? ErrorMessage = null);

public record AiChatMessageDto(
    string Sender,
    string Text,
    DateTime TimestampUtc);
