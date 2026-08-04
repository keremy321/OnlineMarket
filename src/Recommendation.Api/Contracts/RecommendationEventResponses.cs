namespace Recommendation.Api.Contracts;

public sealed record RecommendationEventAcceptedResponse(
    Guid EventId,
    string Status);

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    bool Retryable);

public sealed record ApiValidationErrorResponse(
    string Code,
    string Message,
    bool Retryable,
    IReadOnlyDictionary<string, string[]> Errors);
