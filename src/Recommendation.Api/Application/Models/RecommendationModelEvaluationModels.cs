namespace Recommendation.Api.Application.Models;

public sealed record ModelEvaluationSnapshot(
    IReadOnlyList<Guid> CatalogueProductIds,
    IReadOnlyList<Guid> CandidateProductIds,
    IReadOnlyList<ModelEvaluationOrder> Interactions);

public sealed record ModelEvaluationOrder(
    Guid OrderId,
    string SubjectId,
    DateTime OccurredAtUtc,
    IReadOnlyList<ModelOrderInteractionItem> Items);

public sealed record RecommendationModelEvaluationRequest(
    string EvaluationVersion,
    IReadOnlyList<Guid> CatalogueProductIds,
    IReadOnlyList<Guid> CandidateProductIds,
    IReadOnlyList<RecommendationModelEvaluationOrderRequest> Interactions);

public sealed record RecommendationModelEvaluationOrderRequest(
    Guid OrderId,
    string SubjectId,
    DateTime OccurredAtUtc,
    IReadOnlyList<RecommendationModelOrderInteractionItemRequest> Items);

public sealed record RecommendationModelEvaluationClientResponse(
    string Status,
    string EvaluationVersion,
    DateTime EvaluatedAtUtc,
    string InputHash,
    RecommendationModelEvaluationDatasetResponse Dataset,
    RecommendationModelEvaluationSplitResponse Split,
    RecommendationModelEvaluationExcludedDataResponse ExcludedData,
    RecommendationModelEvaluationModelsResponse Models,
    string ReportIdentifier,
    RecommendationModelEvaluationReportFilesResponse Reports,
    IReadOnlyList<string> Limitations);

public sealed record RecommendationModelEvaluationDatasetResponse(
    int ProductCount,
    int CandidateProductCount,
    int SubjectCount,
    int OrderCount,
    int InteractionCount);

public sealed record RecommendationModelEvaluationSplitResponse(
    string Strategy,
    string Description,
    int K,
    int MinimumHistoricalOrdersPerSubject,
    int HoldoutOrderCount,
    bool ExcludePreviouslyPurchased,
    int RandomSeed,
    int EligibleSubjectCount,
    int ExcludedSubjectCount,
    int TrainingOrderCount,
    int TestOrderCount,
    int TrainingInteractionCount,
    int TestInteractionCount);

public sealed record RecommendationModelEvaluationExcludedDataResponse(
    int InsufficientHistorySubjectCount,
    int NoUsableTrainingHistorySubjectCount,
    int NoUsableTestInteractionsSubjectCount,
    int DevelopmentCapSubjectCount,
    int ProductsAbsentFromTrainingInteractions,
    int UnknownProductInteractionCount,
    int PopularityFallbackSubjectCount);

public sealed record RecommendationModelEvaluationModelsResponse(
    RecommendationModelEvaluationModelResponse Popularity,
    RecommendationModelEvaluationModelResponse Als,
    RecommendationModelEvaluationModelResponse Tfidf,
    RecommendationModelEvaluationModelResponse Fbt);

public sealed record RecommendationModelEvaluationModelResponse(
    string Status,
    string? Reason,
    RecommendationModelEvaluationMetricsResponse? Metrics,
    RecommendationModelEvaluationParametersResponse? Parameters);

public sealed record RecommendationModelEvaluationMetricsResponse(
    decimal PrecisionAtK,
    decimal RecallAtK,
    decimal HitRateAtK,
    decimal NdcgAtK,
    decimal CatalogueCoverage,
    decimal KnownSubjectCatalogueCoverage,
    decimal CatalogueCoverageIncludingFallback,
    int EligibleSubjectCount,
    int TrainingInteractionCount,
    int TestInteractionCount,
    decimal TrainingDurationMilliseconds,
    decimal AverageInferenceLatencyMilliseconds,
    decimal P95InferenceLatencyMilliseconds,
    int FallbackSubjectCount);

public sealed record RecommendationModelEvaluationParametersResponse(
    string InteractionWeighting,
    bool ExcludePreviouslyPurchased,
    int? Factors,
    decimal? Regularization,
    int? Iterations,
    decimal? Alpha,
    int? RandomSeed);

public sealed record RecommendationModelEvaluationReportFilesResponse(
    string JsonFile,
    string MarkdownFile);
