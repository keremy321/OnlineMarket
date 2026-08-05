using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerRecommendationSubjectBackfillStore(
    RecommendationDbContext dbContext)
    : IRecommendationSubjectBackfillStore
{
    public async Task<IReadOnlyList<RecommendationSubjectBackfillCandidate>>
        GetBatchAsync(
            Guid? afterOrderId,
            int batchSize,
            CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var query = dbContext.OrderSnapshots.AsNoTracking();
        if (afterOrderId.HasValue)
        {
            query = query.Where(order =>
                order.OrderId.CompareTo(afterOrderId.Value) > 0);
        }

        return await query
            .OrderBy(order => order.OrderId)
            .Take(batchSize)
            .Select(order => new RecommendationSubjectBackfillCandidate(
                order.OrderId,
                order.CustomerId,
                order.SubjectId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> TrySetSubjectIdAsync(
        Guid orderId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (!RecommendationSubjectIdContract.IsValid(subjectId))
        {
            throw new ArgumentException(
                "A valid recommendation SubjectId is required.",
                nameof(subjectId));
        }

        var affectedRows = await dbContext.OrderSnapshots
            .Where(order => order.OrderId == orderId
                && order.SubjectId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    order => order.SubjectId,
                    subjectId),
                cancellationToken);
        return affectedRows == 1;
    }
}
