using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IPersonalizedRecommendationService
{
    Task<IReadOnlyList<PersonalizedRecommendationItem>> GetAsync(
        Guid customerId,
        int? requestedLimit,
        bool excludePreviouslyPurchased,
        CancellationToken cancellationToken = default);
}

public interface IPersonalizedRecommendationStore
{
    Task<PersonalizedRecommendationContext> GetContextAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
