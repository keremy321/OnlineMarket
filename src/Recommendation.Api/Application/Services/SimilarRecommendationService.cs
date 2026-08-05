using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Application.Services;

public sealed class SimilarRecommendationService(
    ISimilarProductStore store,
    IRecommendationModelClient modelClient,
    IOptions<SimilarRecommendationOptions> options,
    ILogger<SimilarRecommendationService> logger)
    : ISimilarRecommendationService
{
    public async Task<IReadOnlyList<SimilarRecommendationItem>> GetAsync(
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
        var context = await store.GetContextAsync(
            productId,
            cancellationToken);
        if (context.Source is null)
        {
            return [];
        }

        var modelResult = await modelClient.GetSimilarAsync(
            new RecommendationModelSimilarRequest(productId, limit),
            cancellationToken);
        if (modelResult.Outcome == RecommendationModelClientOutcome.Succeeded
            && modelResult.Value is not null)
        {
            return MapModelResults(modelResult.Value, context, limit);
        }

        logger.LogInformation(
            "Using deterministic C# similarity fallback for product {ProductId}; model client outcome {Outcome}.",
            productId,
            modelResult.Outcome);
        return CalculateFallback(context.Source, context.Candidates, limit);
    }

    public static IReadOnlyList<SimilarRecommendationItem> CalculateFallback(
        SimilarProductSnapshot source,
        IReadOnlyList<SimilarProductSnapshot> candidates,
        int limit)
    {
        return candidates
            .Where(candidate =>
                candidate.ProductId != source.ProductId
                && candidate.IsActive
                && candidate.IsInStock)
            .Select(candidate =>
            {
                var categoryScore = candidate.CategoryId == source.CategoryId
                    ? 0.65m
                    : candidate.ParentCategoryId.HasValue
                        && source.ParentCategoryId.HasValue
                        && candidate.ParentCategoryId
                            == source.ParentCategoryId
                            ? 0.20m
                            : 0m;
                var brandScore = candidate.BrandId == source.BrandId
                    ? 0.10m
                    : 0m;
                var priceScore = 0.15m * Proximity(
                    source.Price,
                    candidate.Price);
                var amountScore = CompatibleAmount(
                    source,
                    candidate,
                    out var sourceAmount,
                    out var candidateAmount)
                        ? 0.10m * Proximity(
                            sourceAmount,
                            candidateAmount)
                        : 0m;
                var score = RoundScore(
                    categoryScore
                    + brandScore
                    + priceScore
                    + amountScore);
                return new SimilarRecommendationItem(
                    candidate.ProductId,
                    score,
                    null,
                    candidate.PopularityScore,
                    candidate.FrequentlyBoughtTogetherScore,
                    score,
                    SimilarRankingSource.CSharpContentFallback,
                    null);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProductId)
            .Take(limit)
            .ToArray();
    }

    private static IReadOnlyList<SimilarRecommendationItem> MapModelResults(
        RecommendationModelSimilarClientResponse response,
        SimilarProductContext context,
        int limit)
    {
        var currentCandidates = context.Candidates
            .Where(candidate =>
                candidate.IsActive
                && candidate.IsInStock
                && candidate.ProductId != context.Source!.ProductId)
            .ToDictionary(candidate => candidate.ProductId);
        return response.Items
            .Where(item => currentCandidates.ContainsKey(item.ProductId))
            .Select(item =>
            {
                var candidate = currentCandidates[item.ProductId];
                return new SimilarRecommendationItem(
                    item.ProductId,
                    item.TfidfScore,
                    item.TfidfScore,
                    candidate.PopularityScore,
                    candidate.FrequentlyBoughtTogetherScore,
                    null,
                    SimilarRankingSource.PythonTfidf,
                    response.ModelVersion);
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ProductId)
            .Take(limit)
            .ToArray();
    }

    private static decimal Proximity(decimal first, decimal second)
    {
        var maximum = Math.Max(first, second);
        return maximum == 0m
            ? 1m
            : Math.Clamp(1m - Math.Abs(first - second) / maximum, 0m, 1m);
    }

    private static bool CompatibleAmount(
        SimilarProductSnapshot first,
        SimilarProductSnapshot second,
        out decimal firstAmount,
        out decimal secondAmount)
    {
        firstAmount = NormalizeAmount(first.NetContent, first.UnitType);
        secondAmount = NormalizeAmount(second.NetContent, second.UnitType);
        return UnitFamily(first.UnitType) == UnitFamily(second.UnitType);
    }

    private static int UnitFamily(UnitType unitType)
    {
        return unitType switch
        {
            UnitType.Gram or UnitType.Kilogram => 1,
            UnitType.Millilitre or UnitType.Litre => 2,
            UnitType.Piece => 3,
            UnitType.Package => 4,
            _ => 0
        };
    }

    private static decimal NormalizeAmount(
        decimal netContent,
        UnitType unitType)
    {
        return unitType is UnitType.Kilogram or UnitType.Litre
            ? netContent * 1000m
            : netContent;
    }

    private static decimal RoundScore(decimal value)
    {
        return Math.Clamp(
            decimal.Round(value, 6, MidpointRounding.AwayFromZero),
            0m,
            1m);
    }
}
