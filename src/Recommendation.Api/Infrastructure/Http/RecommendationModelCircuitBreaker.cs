namespace Recommendation.Api.Infrastructure.Http;

public sealed class RecommendationModelCircuitBreaker(TimeProvider timeProvider)
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);
    private readonly object sync = new();
    private int consecutiveFailures;
    private DateTimeOffset? openUntil;
    private bool halfOpenProbeInProgress;

    public bool TryEnter()
    {
        lock (sync)
        {
            var now = timeProvider.GetUtcNow();
            if (!openUntil.HasValue)
            {
                return true;
            }

            if (now < openUntil.Value)
            {
                return false;
            }

            if (halfOpenProbeInProgress)
            {
                return false;
            }

            halfOpenProbeInProgress = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (sync)
        {
            consecutiveFailures = 0;
            openUntil = null;
            halfOpenProbeInProgress = false;
        }
    }

    public void RecordFailure()
    {
        lock (sync)
        {
            consecutiveFailures++;
            if (halfOpenProbeInProgress
                || consecutiveFailures >= FailureThreshold)
            {
                openUntil = timeProvider.GetUtcNow() + BreakDuration;
            }

            halfOpenProbeInProgress = false;
        }
    }

    public void RecordCancellation()
    {
        lock (sync)
        {
            halfOpenProbeInProgress = false;
        }
    }
}
