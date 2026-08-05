using Recommendation.Api.Contracts;

namespace Recommendation.Api.Application.Models;

public sealed record ApplicationError(
    string Code,
    string Message,
    bool Retryable);

public sealed record RecommendationEventIngestionResult(
    RecommendationEventAcceptedResponse? Value,
    ApplicationError? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors)
{
    public bool Succeeded => Value is not null;
}

public enum RecommendationEventStoreOutcome
{
    Created,
    Replay,
    PayloadConflict,
    ProductSnapshotMissing,
    ResourceConflict
}

public sealed record RecommendationEventStoreResult(
    RecommendationEventStoreOutcome Outcome);

public sealed record ProductEventIntake(
    ProductSnapshotChangedV1Request Event,
    string PayloadHash,
    DateTime ReceivedAtUtc);

public sealed record OrderEventIntake(
    OrderConfirmedForRecommendationV1Request Event,
    string PayloadHash,
    DateTime ReceivedAtUtc,
    int TotalQuantity,
    string SubjectId);
