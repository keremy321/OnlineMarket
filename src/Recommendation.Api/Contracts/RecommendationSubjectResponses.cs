namespace Recommendation.Api.Contracts;

public sealed record RecommendationSubjectBackfillResponse(
    int ScannedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailureCount);
