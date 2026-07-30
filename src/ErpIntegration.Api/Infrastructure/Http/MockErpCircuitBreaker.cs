namespace ErpIntegration.Api.Infrastructure.Http;

public sealed class MockErpCircuitBreaker
{
    private readonly object gate = new();
    private int consecutiveFailures;
    private DateTime? openUntilUtc;
    private bool probeInProgress;

    public bool TryAcquire(
        DateTime nowUtc,
        out DateTime? retryAfterUtc)
    {
        lock (gate)
        {
            if (openUntilUtc is null)
            {
                retryAfterUtc = null;
                return true;
            }

            if (openUntilUtc > nowUtc)
            {
                retryAfterUtc = openUntilUtc;
                return false;
            }

            if (probeInProgress)
            {
                retryAfterUtc = nowUtc.AddMilliseconds(100);
                return false;
            }

            probeInProgress = true;
            retryAfterUtc = null;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            consecutiveFailures = 0;
            openUntilUtc = null;
            probeInProgress = false;
        }
    }

    public void RecordTransientFailure(
        DateTime nowUtc,
        int failureThreshold,
        TimeSpan breakDuration)
    {
        lock (gate)
        {
            consecutiveFailures++;
            if (probeInProgress
                || consecutiveFailures >= failureThreshold)
            {
                openUntilUtc = nowUtc.Add(breakDuration);
            }

            probeInProgress = false;
        }
    }
}
