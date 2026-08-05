using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationModelOrchestrationService
{
    Task<RecommendationModelRecalculationResult> RecalculateAsync(
        CancellationToken cancellationToken = default);
}

public interface IModelTrainingSnapshotStore
{
    Task<ModelTrainingSnapshot> GetTrainingSnapshotAsync(
        CancellationToken cancellationToken = default);
}
