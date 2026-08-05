using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Services;

public sealed class RecommendationSubjectBackfillService(
    IRecommendationSubjectBackfillStore store,
    IRecommendationSubjectIdDeriver subjectIdDeriver,
    ILogger<RecommendationSubjectBackfillService> logger)
    : IRecommendationSubjectBackfillService
{
    private const int BatchSize = 100;

    public async Task<RecommendationSubjectBackfillResult> BackfillAsync(
        CancellationToken cancellationToken = default)
    {
        var scannedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        var failureCount = 0;
        Guid? afterOrderId = null;

        while (true)
        {
            var candidates = await store.GetBatchAsync(
                afterOrderId,
                BatchSize,
                cancellationToken);
            if (candidates.Count == 0)
            {
                break;
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedCount++;
                afterOrderId = candidate.OrderId;
                if (candidate.SubjectId is not null)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    var subjectId = subjectIdDeriver.Derive(
                        candidate.CustomerId);
                    if (!RecommendationSubjectIdContract.IsValid(subjectId))
                    {
                        throw new InvalidOperationException(
                            "Subject derivation returned an invalid identifier.");
                    }

                    if (await store.TrySetSubjectIdAsync(
                            candidate.OrderId,
                            subjectId,
                            cancellationToken))
                    {
                        updatedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failureCount++;
                    logger.LogWarning(
                        "SubjectId backfill failed for OrderId {OrderId} with failure type {FailureType}.",
                        candidate.OrderId,
                        exception.GetType().Name);
                }
            }

            if (candidates.Count < BatchSize)
            {
                break;
            }
        }

        logger.LogInformation(
            "SubjectId backfill completed with {ScannedCount} scanned, {UpdatedCount} updated, {SkippedCount} skipped, and {FailureCount} failed.",
            scannedCount,
            updatedCount,
            skippedCount,
            failureCount);
        return new RecommendationSubjectBackfillResult(
            scannedCount,
            updatedCount,
            skippedCount,
            failureCount);
    }
}
