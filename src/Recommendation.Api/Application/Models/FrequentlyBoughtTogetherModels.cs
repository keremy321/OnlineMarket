namespace Recommendation.Api.Application.Models;

public sealed record FbtRecommendationItem(
    Guid ProductId,
    decimal Score,
    int PairOrderCount,
    decimal Support,
    decimal Confidence,
    decimal Lift);

public sealed record FbtOrderProduct(Guid OrderId, Guid ProductId);

public sealed record FbtProjection(
    Guid SourceProductId,
    Guid RecommendedProductId,
    int PairOrderCount,
    int SourceOrderCount,
    int RecommendedOrderCount,
    int TotalOrderCount,
    decimal Support,
    decimal Confidence,
    decimal Lift,
    decimal Score);

public sealed record FbtCalculationSettings(
    int MinimumPairOrderCount,
    decimal MinimumSupport,
    decimal MinimumConfidence,
    decimal MinimumLift);

public enum FbtRecalculationOutcome
{
    Succeeded,
    AlreadyInProgress
}

public sealed record FbtRecalculationResult(
    FbtRecalculationOutcome Outcome,
    Guid? RunId,
    int OutputRecordCount);
