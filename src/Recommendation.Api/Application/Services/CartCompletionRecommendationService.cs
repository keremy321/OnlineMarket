using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;

namespace Recommendation.Api.Application.Services;

public sealed class CartCompletionRecommendationService(
    ICartCompletionRecommendationStore store,
    IOptions<CartCompletionRecommendationOptions> options)
    : ICartCompletionRecommendationService
{
    public async Task<IReadOnlyList<CartCompletionRecommendationItem>> GetAsync(
        IReadOnlyList<Guid> productIds,
        int? requestedLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one product ID is required.",
                nameof(productIds));
        }

        if (productIds.Any(productId => productId == Guid.Empty))
        {
            throw new ArgumentException(
                "Cart product IDs cannot be empty.",
                nameof(productIds));
        }

        if (requestedLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLimit),
                "The recommendation limit must be positive.");
        }

        var cartProductIds = productIds.Distinct().ToArray();
        var configured = options.Value;
        var limit = Math.Min(
            requestedLimit ?? configured.DefaultLimit,
            configured.MaximumLimit);
        var candidates = await store.GetCandidatesAsync(
            cartProductIds,
            cancellationToken);

        return Rank(
                candidates,
                cartProductIds.Length,
                new CartCompletionRankingSettings(
                    configured.AffinityScoreWeight,
                    configured.ConfidenceWeight,
                    configured.LiftWeight,
                    configured.SupportingProductWeight))
            .Take(limit)
            .ToArray();
    }

    public static IReadOnlyList<CartCompletionRecommendationItem> Rank(
        IReadOnlyList<CartAffinityCandidate> candidates,
        int distinctCartProductCount,
        CartCompletionRankingSettings settings)
    {
        if (candidates.Count == 0 || distinctCartProductCount <= 0)
        {
            return [];
        }

        var aggregates = candidates
            .GroupBy(candidate => candidate.RecommendedProductId)
            .Select(group =>
            {
                var sourceRules = group
                    .GroupBy(candidate => candidate.SourceProductId)
                    .Select(source => source.First())
                    .ToArray();
                var supportingProductCount = sourceRules.Length;
                var averageAffinityScore = Math.Clamp(
                    sourceRules.Average(candidate => candidate.AffinityScore),
                    0m,
                    1m);
                var averageConfidence = Math.Clamp(
                    sourceRules.Average(candidate => candidate.Confidence),
                    0m,
                    1m);
                var averageLift = Math.Max(
                    0m,
                    sourceRules.Average(candidate => candidate.Lift));
                var normalizedLift = averageLift / (1m + averageLift);
                var sourceCoverage = Math.Min(
                    1m,
                    (decimal)supportingProductCount
                        / distinctCartProductCount);
                var rawScore =
                    settings.AffinityScoreWeight * averageAffinityScore
                    + settings.ConfidenceWeight * averageConfidence
                    + settings.LiftWeight * normalizedLift
                    + settings.SupportingProductWeight * sourceCoverage;

                return new RankedCandidate(
                    group.Key,
                    supportingProductCount,
                    RoundMetric(averageConfidence),
                    RoundMetric(averageLift),
                    rawScore);
            })
            .ToArray();
        var maximumRawScore = aggregates.Max(candidate => candidate.RawScore);

        return aggregates
            .Select(candidate => new CartCompletionRecommendationItem(
                candidate.ProductId,
                maximumRawScore == 0m
                    ? 0m
                    : RoundMetric(candidate.RawScore / maximumRawScore),
                candidate.SupportingCartProductCount,
                candidate.Confidence,
                candidate.Lift))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate =>
                candidate.SupportingCartProductCount)
            .ThenBy(candidate => candidate.ProductId)
            .ToArray();
    }

    private static decimal RoundMetric(decimal value)
    {
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private sealed record RankedCandidate(
        Guid ProductId,
        int SupportingCartProductCount,
        decimal Confidence,
        decimal Lift,
        decimal RawScore);
}
