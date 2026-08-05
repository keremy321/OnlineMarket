using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationModelEvaluationService
{
    Task<RecommendationModelClientResult<
        RecommendationModelEvaluationClientResponse>> EvaluateAsync(
            CancellationToken cancellationToken = default);
}

public interface IModelEvaluationSnapshotStore
{
    Task<ModelEvaluationSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
