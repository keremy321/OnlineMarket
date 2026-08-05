using Microsoft.Extensions.Logging;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;

namespace Recommendation.Api.Tests;

public sealed class RecommendationSubjectBackfillServiceTests
{
    [Fact]
    public async Task Backfill_reports_failures_without_logging_subject_mappings()
    {
        var successfulCustomerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var failingCustomerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");
        var subjectId = $"v1.{new string('A', 43)}";
        var store = new StubStore(
            [
                new RecommendationSubjectBackfillCandidate(
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    successfulCustomerId,
                    null),
                new RecommendationSubjectBackfillCandidate(
                    Guid.Parse("20000000-0000-0000-0000-000000000002"),
                    failingCustomerId,
                    null)
            ]);
        var logger = new CapturingLogger<
            RecommendationSubjectBackfillService>();
        var service = new RecommendationSubjectBackfillService(
            store,
            new StubDeriver(failingCustomerId, subjectId),
            logger);

        var result = await service.BackfillAsync();

        Assert.Equal(2, result.ScannedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(subjectId, Assert.Single(store.Updates).SubjectId);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(
                successfulCustomerId.ToString(),
                StringComparison.OrdinalIgnoreCase)
                || entry.Contains(
                    failingCustomerId.ToString(),
                    StringComparison.OrdinalIgnoreCase)
                || entry.Contains(subjectId, StringComparison.Ordinal));
    }

    private sealed class StubDeriver(
        Guid failingCustomerId,
        string subjectId)
        : IRecommendationSubjectIdDeriver
    {
        public string Derive(Guid customerId)
        {
            return customerId == failingCustomerId
                ? throw new InvalidOperationException("Injected failure.")
                : subjectId;
        }
    }

    private sealed class StubStore(
        IReadOnlyList<RecommendationSubjectBackfillCandidate> candidates)
        : IRecommendationSubjectBackfillStore
    {
        public List<(Guid OrderId, string SubjectId)> Updates { get; } = [];

        public Task<IReadOnlyList<RecommendationSubjectBackfillCandidate>>
            GetBatchAsync(
                Guid? afterOrderId,
                int batchSize,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(afterOrderId.HasValue
                ? (IReadOnlyList<RecommendationSubjectBackfillCandidate>)[]
                : candidates);
        }

        public Task<bool> TrySetSubjectIdAsync(
            Guid orderId,
            string subjectId,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((orderId, subjectId));
            return Task.FromResult(true);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
        }
    }
}
