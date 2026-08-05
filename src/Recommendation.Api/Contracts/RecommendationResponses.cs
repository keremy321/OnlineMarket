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
    string? ModelVersion);

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
    string? ModelVersion);

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
    RecommendationModelAlsParametersContract AlsParameters);

public sealed record RecommendationModelComponentStatusesContract(
    RecommendationModelComponentStatusContract Tfidf,
    RecommendationModelComponentStatusContract Als);

public sealed record RecommendationModelComponentStatusContract(
    string Status,
    int TrainingDurationMilliseconds);

public sealed record RecommendationModelAlsParametersContract(
    int Factors,
    decimal Regularization,
    int Iterations,
    decimal Alpha,
    int RandomSeed);
