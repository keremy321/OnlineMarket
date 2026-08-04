namespace ErpIntegration.Api.Infrastructure.Http;

public enum MockErpCircuitState
{
    Closed,
    Open,
    HalfOpenReady,
    HalfOpenProbeInProgress
}

public sealed record MockErpCircuitSnapshot(
    MockErpCircuitState State,
    DateTime? RetryAfterUtc);

public sealed record MockErpCircuitLease(
    bool Acquired,
    bool IsHalfOpenProbe,
    DateTime? RetryAfterUtc);

public sealed record MockErpCircuitTransition(
    bool Opened,
    DateTime? OpenUntilUtc);

public sealed class MockErpCircuitBreaker
{
    private readonly object gate = new();
    private int consecutiveFailures;
    private DateTime? openUntilUtc;
    private bool probeInProgress;

    public MockErpCircuitSnapshot GetSnapshot(DateTime nowUtc)
    {
        lock (gate)
        {
            if (openUntilUtc is null)
            {
                return new MockErpCircuitSnapshot(
                    MockErpCircuitState.Closed,
                    null);
            }

            if (openUntilUtc > nowUtc)
            {
                return new MockErpCircuitSnapshot(
                    MockErpCircuitState.Open,
                    openUntilUtc);
            }

            return new MockErpCircuitSnapshot(
                probeInProgress
                    ? MockErpCircuitState.HalfOpenProbeInProgress
                    : MockErpCircuitState.HalfOpenReady,
                probeInProgress
                    ? nowUtc.AddMilliseconds(100)
                    : null);
        }
    }

    public MockErpCircuitLease TryAcquire(DateTime nowUtc)
    {
        lock (gate)
        {
            if (openUntilUtc is null)
            {
                return new MockErpCircuitLease(true, false, null);
            }

            if (openUntilUtc > nowUtc)
            {
                return new MockErpCircuitLease(
                    false,
                    false,
                    openUntilUtc);
            }

            if (probeInProgress)
            {
                return new MockErpCircuitLease(
                    false,
                    false,
                    nowUtc.AddMilliseconds(100));
            }

            probeInProgress = true;
            return new MockErpCircuitLease(true, true, null);
        }
    }

    public bool RecordSuccess()
    {
        lock (gate)
        {
            var closedCircuit = openUntilUtc is not null;
            consecutiveFailures = 0;
            openUntilUtc = null;
            probeInProgress = false;
            return closedCircuit;
        }
    }

    public MockErpCircuitTransition RecordTransientFailure(
        DateTime nowUtc,
        int failureThreshold,
        TimeSpan breakDuration)
    {
        lock (gate)
        {
            var wasActivelyOpen = openUntilUtc > nowUtc;
            var wasProbe = probeInProgress;
            consecutiveFailures++;
            if (wasProbe
                || consecutiveFailures >= failureThreshold)
            {
                openUntilUtc = nowUtc.Add(breakDuration);
            }

            probeInProgress = false;
            var opened = openUntilUtc > nowUtc
                && (!wasActivelyOpen || wasProbe);
            return new MockErpCircuitTransition(
                opened,
                openUntilUtc > nowUtc ? openUntilUtc : null);
        }
    }
}
