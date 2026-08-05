using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationSubjectBackfillService
{
    Task<RecommendationSubjectBackfillResult> BackfillAsync(
        CancellationToken cancellationToken = default);
}

public interface IRecommendationSubjectBackfillStore
{
    Task<IReadOnlyList<RecommendationSubjectBackfillCandidate>> GetBatchAsync(
        Guid? afterOrderId,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> TrySetSubjectIdAsync(
        Guid orderId,
        string subjectId,
        CancellationToken cancellationToken = default);
}
