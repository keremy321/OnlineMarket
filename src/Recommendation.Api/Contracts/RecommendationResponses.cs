namespace Recommendation.Api.Contracts;

public sealed record RecommendationResponse(
    Guid ProductId,
    decimal Score,
    string RecommendationType,
    string ReasonCode,
    string ReasonText,
    PopularityMetricsResponse? Metrics);

public sealed record PopularityMetricsResponse(
    int SoldQuantity,
    int DistinctOrderCount,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc);

public sealed record FrequentlyBoughtTogetherRecommendationResponse(
    Guid ProductId,
    decimal Score,
    string RecommendationType,
    string ReasonCode,
    string ReasonText,
    AssociationMetricsResponse? Metrics);

public sealed record AssociationMetricsResponse(
    int PairOrderCount,
    decimal Support,
    decimal Confidence,
    decimal Lift);

public sealed record CartCompletionRecommendationResponse(
    Guid ProductId,
    decimal Score,
    string RecommendationType,
    string ReasonCode,
    string ReasonText,
    CartCompletionMetricsResponse? Metrics);

public sealed record CartCompletionMetricsResponse(
    int SupportingCartProductCount,
    decimal Confidence,
    decimal Lift);

public sealed record SimilarRecommendationResponse(
    Guid ProductId,
    decimal Score,
    string RecommendationType,
    string ReasonCode,
    string ReasonText,
    SimilarityMetricsResponse Metrics);

public sealed record SimilarityMetricsResponse(
    decimal? TfidfScore,
    decimal PopularityScore,
    decimal FrequentlyBoughtTogetherScore,
    decimal? FallbackScore,
    string RankingSource,
    string? ModelVersion,
    decimal? CoPurchaseScore);

public sealed record PersonalizedRecommendationResponse(
    Guid ProductId,
    decimal Score,
    string RecommendationType,
    string ReasonCode,
    string ReasonText,
    PersonalizedMetricsResponse Metrics);

public sealed record PersonalizedMetricsResponse(
    decimal? Confidence,
    string RankingSource,
    string? ModelVersion,
    decimal? AlsScore,
    decimal? ContentAffinityScore,
    decimal? AssociationScore,
    decimal? PopularityScore,
    decimal? FinalScore);

public sealed record RecommendationRecalculationResponse(
    Guid RunId,
    string Status,
    int OutputRecordCount);

public sealed record RecommendationModelRecalculationResponse(
    string ModelVersion,
    string Status,
    DateTime TrainedAtUtc,
    int ProductCount,
    int SubjectCount,
    int InteractionCount,
    string InputHash,
    string Algorithm,
    IReadOnlyList<string> AlgorithmComponents,
    RecommendationModelComponentStatusesContract Components,
    RecommendationModelAlsParametersContract AlsParameters,
    RecommendationModelHybridParametersContract? HybridParameters);

public sealed record RecommendationModelComponentStatusesContract(
    RecommendationModelComponentStatusContract Tfidf,
    RecommendationModelComponentStatusContract Als,
    RecommendationModelComponentStatusContract? Popularity,
    RecommendationModelComponentStatusContract? Association,
    RecommendationModelComponentStatusContract? Hybrid);

public sealed record RecommendationModelComponentStatusContract(
    string Status,
    int TrainingDurationMilliseconds);

public sealed record RecommendationModelAlsParametersContract(
    int Factors,
    decimal Regularization,
    int Iterations,
    decimal Alpha,
    int RandomSeed);

public sealed record RecommendationModelHybridParametersContract(
    RecommendationModelPersonalizedHybridWeightsContract PersonalizedWeights,
    RecommendationModelSimilarHybridWeightsContract SimilarWeights,
    int CandidatePoolMultiplier,
    int CandidatePoolCap,
    string ContentAffinityAggregation,
    string MissingComponentPolicy);

public sealed record RecommendationModelPersonalizedHybridWeightsContract(
    decimal Als,
    decimal ContentAffinity,
    decimal Association,
    decimal Popularity);

public sealed record RecommendationModelSimilarHybridWeightsContract(
    decimal ContentSimilarity,
    decimal CoPurchaseSimilarity,
    decimal Popularity);

public sealed record RecommendationModelEvaluationResponse(
    string Status,
    string EvaluationVersion,
    DateTime EvaluatedAtUtc,
    string InputHash,
    RecommendationModelEvaluationDatasetContract Dataset,
    RecommendationModelEvaluationSplitContract Split,
    RecommendationModelEvaluationExcludedDataContract ExcludedData,
    RecommendationModelEvaluationModelsContract Models,
    RecommendationModelEvaluationComparisonContract? Comparison,
    string ReportIdentifier,
    RecommendationModelEvaluationReportFilesContract Reports,
    IReadOnlyList<string> Limitations);

public sealed record RecommendationModelEvaluationDatasetContract(
    int ProductCount,
    int CandidateProductCount,
    int SubjectCount,
    int OrderCount,
    int InteractionCount);

public sealed record RecommendationModelEvaluationSplitContract(
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

public sealed record RecommendationModelEvaluationExcludedDataContract(
    int InsufficientHistorySubjectCount,
    int NoUsableTrainingHistorySubjectCount,
    int NoUsableTestInteractionsSubjectCount,
    int DevelopmentCapSubjectCount,
    int ProductsAbsentFromTrainingInteractions,
    int UnknownProductInteractionCount,
    int PopularityFallbackSubjectCount);

public sealed record RecommendationModelEvaluationModelsContract(
    RecommendationModelEvaluationModelContract Popularity,
    RecommendationModelEvaluationModelContract Als,
    RecommendationModelEvaluationModelContract Hybrid,
    RecommendationModelEvaluationModelContract Tfidf,
    RecommendationModelEvaluationModelContract Fbt);

public sealed record RecommendationModelEvaluationModelContract(
    string Status,
    string? Reason,
    RecommendationModelEvaluationMetricsContract? Metrics,
    RecommendationModelEvaluationParametersContract? Parameters);

public sealed record RecommendationModelEvaluationMetricsContract(
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

public sealed record RecommendationModelEvaluationParametersContract(
    string InteractionWeighting,
    bool ExcludePreviouslyPurchased,
    int? Factors,
    decimal? Regularization,
    int? Iterations,
    decimal? Alpha,
    int? RandomSeed,
    RecommendationModelHybridParametersContract? Hybrid);

public sealed record RecommendationModelEvaluationComparisonContract(
    decimal HybridMinusAlsPrecisionAt5,
    decimal HybridMinusAlsRecallAt5,
    decimal HybridMinusAlsHitRateAt5,
    decimal HybridMinusAlsNdcgAt5,
    decimal HybridMinusAlsCoverage);

public sealed record RecommendationModelEvaluationReportFilesContract(
    string JsonFile,
    string MarkdownFile);
