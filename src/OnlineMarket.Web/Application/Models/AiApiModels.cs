using System.Text.Json.Serialization;

namespace OnlineMarket.Web.Application.Models;

public record AiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public record AiApiCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<AiChatMessage> Messages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("temperature")] double? Temperature = 0.7,
    [property: JsonPropertyName("max_completion_tokens")] int MaxCompletionTokens = 2000);

public record AiApiCompletionChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] AiChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record AiApiCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] List<AiApiCompletionChoice> Choices);
