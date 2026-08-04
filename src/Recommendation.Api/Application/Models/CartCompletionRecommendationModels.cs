namespace Recommendation.Api.Application.Models;

public sealed record CartAffinityCandidate(
    Guid SourceProductId,
    Guid RecommendedProductId,
    decimal AffinityScore,
    decimal Confidence,
    decimal Lift);

public sealed record CartCompletionRecommendationItem(
    Guid ProductId,
    decimal Score,
    int SupportingCartProductCount,
    decimal Confidence,
    decimal Lift);

public sealed record CartCompletionRankingSettings(
    decimal AffinityScoreWeight,
    decimal ConfidenceWeight,
    decimal LiftWeight,
    decimal SupportingProductWeight);
