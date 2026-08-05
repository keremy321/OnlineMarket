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

public sealed record RecommendationRecalculationResponse(
    Guid RunId,
    string Status,
    int OutputRecordCount);

public sealed record RecommendationModelRecalculationResponse(
    string ModelVersion,
    string Status,
    DateTime TrainedAtUtc,
    int ProductCount,
    string InputHash,
    string Algorithm);
