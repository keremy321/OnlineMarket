using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IFrequentlyBoughtTogetherRecommendationService
{
    Task<IReadOnlyList<FbtRecommendationItem>> GetAsync(
        Guid productId,
        int? requestedLimit,
        CancellationToken cancellationToken = default);

    Task<FbtRecalculationResult> RecalculateAsync(
        CancellationToken cancellationToken = default);
}

public interface IFrequentlyBoughtTogetherRecommendationStore
{
    Task<IReadOnlyList<FbtRecommendationItem>> GetAsync(
        Guid productId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<FbtRecalculationResult> RecalculateAsync(
        FbtCalculationSettings settings,
        CancellationToken cancellationToken = default);
}
