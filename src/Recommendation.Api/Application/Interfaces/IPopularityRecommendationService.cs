using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IPopularityRecommendationService
{
    Task<IReadOnlyList<PopularityRecommendationItem>> GetPopularAsync(
        int? requestedLimit,
        CancellationToken cancellationToken = default);

    Task<PopularityRecalculationResult> RecalculateAsync(
        CancellationToken cancellationToken = default);
}

public interface IPopularityRecommendationStore
{
    Task<IReadOnlyList<PopularityRecommendationItem>> GetPopularAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<PopularityRecalculationResult> RecalculateAsync(
        PopularityCalculationSettings settings,
        CancellationToken cancellationToken = default);
}
