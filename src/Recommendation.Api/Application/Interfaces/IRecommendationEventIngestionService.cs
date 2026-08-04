using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationEventIngestionService
{
    Task<RecommendationEventIngestionResult> IngestProductAsync(
        ProductSnapshotChangedV1Request request,
        CancellationToken cancellationToken = default);

    Task<RecommendationEventIngestionResult> IngestOrderAsync(
        OrderConfirmedForRecommendationV1Request request,
        CancellationToken cancellationToken = default);
}
