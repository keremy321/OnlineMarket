using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;

namespace Recommendation.Api.Application.Services;

public sealed class PopularityRecommendationService(
    IPopularityRecommendationStore store,
    IOptions<PopularityRecommendationOptions> options)
    : IPopularityRecommendationService
{
    public Task<IReadOnlyList<PopularityRecommendationItem>> GetPopularAsync(
        int? requestedLimit,
        CancellationToken cancellationToken = default)
    {
        if (requestedLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLimit),
                "The recommendation count must be positive.");
        }

        var configured = options.Value;
        var limit = Math.Min(
            requestedLimit ?? configured.DefaultLimit,
            configured.MaximumLimit);
        return store.GetPopularAsync(limit, cancellationToken);
    }

    public Task<PopularityRecalculationResult> RecalculateAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        return store.RecalculateAsync(
            new PopularityCalculationSettings(
                configured.WindowDays,
                configured.SoldQuantityWeight,
                configured.DistinctOrderWeight,
                configured.RecencyWeight),
            cancellationToken);
    }

    public static IReadOnlyList<PopularityProjection> Calculate(
        IReadOnlyList<PopularityAggregate> aggregates,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        PopularityCalculationSettings settings)
    {
        if (aggregates.Count == 0)
        {
            return [];
        }

        var maximumSoldQuantity = aggregates.Max(item => item.SoldQuantity);
        var maximumOrderCount = aggregates.Max(item => item.DistinctOrderCount);
        var windowTicks = windowEndUtc.Ticks - windowStartUtc.Ticks;

        return aggregates
            .Select(item =>
            {
                var soldQuantityScore = maximumSoldQuantity == 0
                    ? 0m
                    : (decimal)item.SoldQuantity / maximumSoldQuantity;
                var distinctOrderScore = maximumOrderCount == 0
                    ? 0m
                    : (decimal)item.DistinctOrderCount / maximumOrderCount;
                var elapsedTicks = Math.Clamp(
                    item.LastPurchasedAtUtc.Ticks - windowStartUtc.Ticks,
                    0,
                    windowTicks);
                var recencyScore = windowTicks == 0
                    ? 0m
                    : (decimal)elapsedTicks / windowTicks;
                var score = soldQuantityScore * settings.SoldQuantityWeight
                    + distinctOrderScore * settings.DistinctOrderWeight
                    + recencyScore * settings.RecencyWeight;

                return new PopularityProjection(
                    item.ProductId,
                    item.SoldQuantity,
                    item.DistinctOrderCount,
                    Math.Clamp(
                        decimal.Round(
                            score,
                            6,
                            MidpointRounding.AwayFromZero),
                        0m,
                        1m));
            })
            .ToArray();
    }
}
