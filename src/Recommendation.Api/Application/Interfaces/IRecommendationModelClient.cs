using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationModelClient
{
    Task<RecommendationModelClientResult<
        RecommendationModelTrainingClientResponse>> TrainAsync(
            RecommendationModelTrainingRequest request,
            CancellationToken cancellationToken = default);

    Task<RecommendationModelClientResult<
        RecommendationModelSimilarClientResponse>> GetSimilarAsync(
            RecommendationModelSimilarRequest request,
            CancellationToken cancellationToken = default);

    Task<RecommendationModelClientResult<
        RecommendationModelPersonalizedClientResponse>> GetPersonalizedAsync(
            RecommendationModelPersonalizedRequest request,
            CancellationToken cancellationToken = default);

    Task<RecommendationModelClientResult<
        RecommendationModelEvaluationClientResponse>> EvaluateAsync(
            RecommendationModelEvaluationRequest request,
            CancellationToken cancellationToken = default);
}
