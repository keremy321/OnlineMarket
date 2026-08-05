using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;

namespace Recommendation.Api.Application.Services;

public sealed class PersonalizedRecommendationService(
    IPersonalizedRecommendationStore store,
    IRecommendationSubjectIdDeriver subjectIdDeriver,
    IRecommendationModelClient modelClient,
    IOptions<PersonalizedRecommendationOptions> options,
    ILogger<PersonalizedRecommendationService> logger)
    : IPersonalizedRecommendationService
{
    private const string AlsStrategy = "implicit_als";
    private const string HybridStrategy = "HybridPersonalized";

    public async Task<IReadOnlyList<PersonalizedRecommendationItem>> GetAsync(
        Guid customerId,
        int? requestedLimit,
        bool excludePreviouslyPurchased,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A customer ID is required.",
                nameof(customerId));
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
            customerId,
            cancellationToken);
        var subjectId = subjectIdDeriver.Derive(customerId);
        if (!RecommendationSubjectIdContract.IsValid(subjectId))
        {
            throw new InvalidOperationException(
                "Recommendation subject derivation returned an invalid identifier.");
        }

        var modelResult = await modelClient.GetPersonalizedAsync(
            new RecommendationModelPersonalizedRequest(
                subjectId,
                limit,
                excludePreviouslyPurchased,
                configured.ModelStrategy.ToString()),
            cancellationToken);
        if (modelResult.Outcome == RecommendationModelClientOutcome.Succeeded
            && modelResult.Value is not null
            && modelResult.Value.Strategy is AlsStrategy or HybridStrategy)
        {
            var mapped = MapModelResults(
                modelResult.Value,
                context,
                limit,
                excludePreviouslyPurchased);
            if (mapped.Count > 0)
            {
                return mapped;
            }
        }

        logger.LogInformation(
            "Using deterministic C# personalized fallback; model client outcome {Outcome}.",
            modelResult.Outcome);
        return CalculateFallback(
            context,
            configured,
            limit,
            excludePreviouslyPurchased);
    }

    public static IReadOnlyList<PersonalizedRecommendationItem>
        CalculateFallback(
            PersonalizedRecommendationContext context,
            PersonalizedRecommendationOptions options,
            int limit,
            bool excludePreviouslyPurchased)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid())
        {
            throw new InvalidOperationException(
                "Personalized recommendation options are invalid.");
        }

        var purchasedProductIds = context.Purchases
            .Select(purchase => purchase.ProductId)
            .ToHashSet();
        var categoryTotals = context.Purchases
            .GroupBy(purchase => purchase.CategoryId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(purchase => purchase.Quantity));
        var brandTotals = context.Purchases
            .GroupBy(purchase => purchase.BrandId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(purchase => purchase.Quantity));
        var maximumCategoryTotal = categoryTotals.Count == 0
            ? 0
            : categoryTotals.Values.Max();
        var maximumBrandTotal = brandTotals.Count == 0
            ? 0
            : brandTotals.Values.Max();

        return context.Products
            .Where(product => product.IsActive && product.IsInStock)
            .Where(product => !excludePreviouslyPurchased
                || !purchasedProductIds.Contains(product.ProductId))
            .Select(product =>
            {
                var categoryAffinity = Normalize(
                    categoryTotals.GetValueOrDefault(product.CategoryId),
                    maximumCategoryTotal);
                var brandAffinity = Normalize(
                    brandTotals.GetValueOrDefault(product.BrandId),
                    maximumBrandTotal);
                var score = Math.Round(
                    options.CategoryAffinityWeight * categoryAffinity
                    + options.BrandAffinityWeight * brandAffinity
                    + options.PopularityWeight
                        * Math.Clamp(product.PopularityScore, 0m, 1m),
                    6,
                    MidpointRounding.AwayFromZero);
                return new PersonalizedRecommendationItem(
                    product.ProductId,
                    Math.Clamp(score, 0m, 1m),
                    null,
                    PersonalizedRankingSource.CSharpPreferenceFallback,
                    null);
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ProductId)
            .Take(limit)
            .ToArray();
    }

    private static IReadOnlyList<PersonalizedRecommendationItem>
        MapModelResults(
            RecommendationModelPersonalizedClientResponse response,
            PersonalizedRecommendationContext context,
            int limit,
            bool excludePreviouslyPurchased)
    {
        var purchasedProductIds = context.Purchases
            .Select(purchase => purchase.ProductId)
            .ToHashSet();
        var availableProductIds = context.Products
            .Where(product => product.IsActive && product.IsInStock)
            .Where(product => !excludePreviouslyPurchased
                || !purchasedProductIds.Contains(product.ProductId))
            .Select(product => product.ProductId)
            .ToHashSet();
        var rankingSource = response.Strategy == HybridStrategy
            ? PersonalizedRankingSource.PythonHybrid
            : PersonalizedRankingSource.PythonImplicitAls;
        return response.Recommendations
            .Where(item => availableProductIds.Contains(item.ProductId))
            .Select(item => new PersonalizedRecommendationItem(
                item.ProductId,
                item.Score,
                item.Confidence,
                rankingSource,
                response.ModelVersion,
                item.ReasonCode,
                item.ReasonText,
                item.AlsScore,
                item.ContentAffinityScore,
                item.AssociationScore,
                item.PopularityScore,
                item.FinalScore))
            .DistinctBy(item => item.ProductId)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ProductId)
            .Take(limit)
            .ToArray();
    }

    private static decimal Normalize(int value, int maximum)
    {
        return maximum == 0
            ? 0m
            : Math.Clamp((decimal)value / maximum, 0m, 1m);
    }
}
