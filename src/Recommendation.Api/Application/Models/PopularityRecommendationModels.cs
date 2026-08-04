namespace Recommendation.Api.Application.Models;

public sealed record PopularityRecommendationItem(
    Guid ProductId,
    decimal Score,
    int SoldQuantity,
    int DistinctOrderCount,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc);

public sealed record PopularityAggregate(
    Guid ProductId,
    int SoldQuantity,
    int DistinctOrderCount,
    DateTime LastPurchasedAtUtc);

public sealed record PopularityProjection(
    Guid ProductId,
    int SoldQuantity,
    int DistinctOrderCount,
    decimal Score);

public sealed record PopularityCalculationSettings(
    int WindowDays,
    decimal SoldQuantityWeight,
    decimal DistinctOrderWeight,
    decimal RecencyWeight);

public enum PopularityRecalculationOutcome
{
    Succeeded,
    AlreadyInProgress
}

public sealed record PopularityRecalculationResult(
    PopularityRecalculationOutcome Outcome,
    Guid? RunId,
    int OutputRecordCount);
