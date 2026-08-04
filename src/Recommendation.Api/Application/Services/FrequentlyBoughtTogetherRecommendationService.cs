using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;

namespace Recommendation.Api.Application.Services;

public sealed class FrequentlyBoughtTogetherRecommendationService(
    IFrequentlyBoughtTogetherRecommendationStore store,
    IOptions<FrequentlyBoughtTogetherOptions> options)
    : IFrequentlyBoughtTogetherRecommendationService
{
    public Task<IReadOnlyList<FbtRecommendationItem>> GetAsync(
        Guid productId,
        int? requestedLimit,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException(
                "A product ID is required.",
                nameof(productId));
        }

        if (requestedLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLimit),
                "The recommendation limit must be positive.");
        }

        var configured = options.Value;
        var limit = Math.Min(
            requestedLimit ?? configured.DefaultLimit,
            configured.MaximumLimit);
        return store.GetAsync(productId, limit, cancellationToken);
    }

    public Task<FbtRecalculationResult> RecalculateAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        return store.RecalculateAsync(
            new FbtCalculationSettings(
                configured.MinimumPairOrderCount,
                configured.MinimumSupport,
                configured.MinimumConfidence,
                configured.MinimumLift),
            cancellationToken);
    }

    public static IReadOnlyList<FbtProjection> Calculate(
        IReadOnlyList<FbtOrderProduct> orderProducts,
        int totalOrderCount,
        IReadOnlySet<Guid> availableTargetProductIds,
        FbtCalculationSettings settings)
    {
        if (totalOrderCount <= 0 || orderProducts.Count == 0)
        {
            return [];
        }

        var baskets = orderProducts
            .GroupBy(item => item.OrderId)
            .Select(group => group
                .Select(item => item.ProductId)
                .Where(productId => productId != Guid.Empty)
                .Distinct()
                .OrderBy(productId => productId)
                .ToArray())
            .Where(products => products.Length > 0)
            .ToArray();
        var productOrderCounts = new Dictionary<Guid, int>();
        var pairOrderCounts = new Dictionary<(Guid First, Guid Second), int>();

        foreach (var basket in baskets)
        {
            foreach (var productId in basket)
            {
                productOrderCounts[productId] = productOrderCounts.GetValueOrDefault(productId) + 1;
            }

            for (var firstIndex = 0; firstIndex < basket.Length; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1;
                     secondIndex < basket.Length;
                     secondIndex++)
                {
                    var pair = (basket[firstIndex], basket[secondIndex]);
                    pairOrderCounts[pair] = pairOrderCounts.GetValueOrDefault(pair) + 1;
                }
            }
        }

        var candidates = new List<FbtCandidate>(pairOrderCounts.Count * 2);
        foreach (var pair in pairOrderCounts)
        {
            AddDirection(pair.Key.First, pair.Key.Second, pair.Value);
            AddDirection(pair.Key.Second, pair.Key.First, pair.Value);
        }

        return candidates
            .GroupBy(candidate => candidate.SourceProductId)
            .SelectMany(group =>
            {
                var maximumRawScore = group.Max(candidate => candidate.RawScore);
                return group.Select(candidate => new FbtProjection(
                    candidate.SourceProductId,
                    candidate.RecommendedProductId,
                    candidate.PairOrderCount,
                    candidate.SourceOrderCount,
                    candidate.RecommendedOrderCount,
                    totalOrderCount,
                    candidate.Support,
                    candidate.Confidence,
                    candidate.Lift,
                    maximumRawScore == 0m
                        ? 0m
                        : RoundMetric(candidate.RawScore / maximumRawScore)));
            })
            .ToArray();

        void AddDirection(
            Guid sourceProductId,
            Guid recommendedProductId,
            int pairOrderCount)
        {
            if (sourceProductId == recommendedProductId
                || !availableTargetProductIds.Contains(recommendedProductId))
            {
                return;
            }

            var sourceOrderCount = productOrderCounts[sourceProductId];
            var recommendedOrderCount = productOrderCounts[recommendedProductId];
            var support = RoundMetric(
                (decimal)pairOrderCount / totalOrderCount);
            var confidence = RoundMetric(
                (decimal)pairOrderCount / sourceOrderCount);
            var lift = RoundMetric(
                (decimal)pairOrderCount * totalOrderCount
                / (sourceOrderCount * recommendedOrderCount));
            if (pairOrderCount < settings.MinimumPairOrderCount
                || support < settings.MinimumSupport
                || confidence < settings.MinimumConfidence
                || lift < settings.MinimumLift)
            {
                return;
            }

            var rawScore = confidence
                * (decimal)Math.Log(1d + (double)lift)
                * support;
            candidates.Add(new FbtCandidate(
                sourceProductId,
                recommendedProductId,
                pairOrderCount,
                sourceOrderCount,
                recommendedOrderCount,
                support,
                confidence,
                lift,
                rawScore));
        }
    }

    private static decimal RoundMetric(decimal value)
    {
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private sealed record FbtCandidate(
        Guid SourceProductId,
        Guid RecommendedProductId,
        int PairOrderCount,
        int SourceOrderCount,
        int RecommendedOrderCount,
        decimal Support,
        decimal Confidence,
        decimal Lift,
        decimal RawScore);
}
