using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface ICartCompletionRecommendationService
{
    Task<IReadOnlyList<CartCompletionRecommendationItem>> GetAsync(
        IReadOnlyList<Guid> productIds,
        int? requestedLimit,
        CancellationToken cancellationToken = default);
}

public interface ICartCompletionRecommendationStore
{
    Task<IReadOnlyList<CartAffinityCandidate>> GetCandidatesAsync(
        IReadOnlyCollection<Guid> cartProductIds,
        CancellationToken cancellationToken = default);
}
