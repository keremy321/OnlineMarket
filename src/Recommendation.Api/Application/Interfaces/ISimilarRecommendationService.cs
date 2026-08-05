using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface ISimilarRecommendationService
{
    Task<IReadOnlyList<SimilarRecommendationItem>> GetAsync(
        Guid productId,
        int? requestedLimit,
        CancellationToken cancellationToken = default);
}

public interface ISimilarProductStore
{
    Task<SimilarProductContext> GetContextAsync(
        Guid sourceProductId,
        CancellationToken cancellationToken = default);
}
