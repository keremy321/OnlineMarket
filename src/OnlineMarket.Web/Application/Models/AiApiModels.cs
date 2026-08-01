namespace OnlineMarket.Web.Application.Models;

public record AiChatMessage(
    string Role,
    string Content);

public record AiApiCompletionRequest(
    string Model,
    List<AiChatMessage> Messages,
    double Temperature = 0.7,
    int MaxTokens = 500);

public record AiApiCompletionChoice(
    int Index,
    AiChatMessage Message,
    string? FinishReason);

public record AiApiCompletionResponse(
    string Id,
    string Model,
    List<AiApiCompletionChoice> Choices);
