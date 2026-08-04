using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationEventStore
{
    Task<RecommendationEventStoreResult> AcceptProductAsync(
        ProductEventIntake intake,
        CancellationToken cancellationToken = default);

    Task<RecommendationEventStoreResult> AcceptOrderAsync(
        OrderEventIntake intake,
        CancellationToken cancellationToken = default);
}
