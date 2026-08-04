using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Workers;

public sealed class IntegrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrationWorkerOptions> options,
    MockErpCircuitBreaker circuitBreaker,
    TimeProvider timeProvider,
    ILogger<IntegrationWorker> logger)
    : BackgroundService
{
    private readonly IntegrationWorkerOptions options = options.Value;
    private readonly string workerId = CreateWorkerId();

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The ERP integration worker is disabled.");
            return;
        }

        logger.LogInformation(
            "ERP integration worker {WorkerId} started with claim batch size {ClaimBatchSize} and maximum parallelism {MaximumParallelism}.",
            workerId,
            options.ClaimBatchSize,
            options.MaximumParallelism);
        var pausedForCircuit = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var beforeCycle = circuitBreaker.GetSnapshot(GetUtcNow());
                if (IsPaused(beforeCycle.State))
                {
                    if (!pausedForCircuit)
                    {
                        logger.LogWarning(
                            "ERP integration worker {WorkerId} paused while the Mock ERP circuit is {CircuitState}; retry after {RetryAfterUtc}.",
                            workerId,
                            beforeCycle.State,
                            beforeCycle.RetryAfterUtc);
                        pausedForCircuit = true;
                    }

                    await DelayUntilNextCycleAsync(
                        beforeCycle.RetryAfterUtc,
                        stoppingToken);
                    continue;
                }

                await ProcessCycleAsync(workerId, stoppingToken);
                var afterCycle = circuitBreaker.GetSnapshot(GetUtcNow());
                if (pausedForCircuit
                    && afterCycle.State == MockErpCircuitState.Closed)
                {
                    logger.LogInformation(
                        "ERP integration worker {WorkerId} resumed after the Mock ERP circuit recovered.",
                        workerId);
                    pausedForCircuit = false;
                }
                else if (IsPaused(afterCycle.State))
                {
                    if (!pausedForCircuit)
                    {
                        logger.LogWarning(
                            "ERP integration worker {WorkerId} paused after the Mock ERP circuit opened; retry after {RetryAfterUtc}.",
                            workerId,
                            afterCycle.RetryAfterUtc);
                    }

                    pausedForCircuit = true;
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The ERP integration worker loop failed.");
            }

            await Task.Delay(
                options.PollInterval,
                timeProvider,
                stoppingToken);
        }
    }

    public async Task<int> ProcessCycleAsync(
        string cycleWorkerId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = circuitBreaker.GetSnapshot(GetUtcNow());
        if (IsPaused(snapshot.State))
        {
            return 0;
        }

        var claimLimit = snapshot.State == MockErpCircuitState.HalfOpenReady
            ? 1
            : options.ClaimBatchSize;
        var parallelism = Math.Min(
            snapshot.State == MockErpCircuitState.HalfOpenReady
                ? 1
                : options.MaximumParallelism,
            claimLimit);
        var processedCount = 0;
        while (processedCount < claimLimit)
        {
            var waveSize = Math.Min(
                parallelism,
                claimLimit - processedCount);
            var results = await Task.WhenAll(
                Enumerable.Range(0, waveSize)
                    .Select(_ => ProcessOneAsync(
                        cycleWorkerId,
                        cancellationToken)));
            var waveProcessedCount = results.Count(processed => processed);
            processedCount += waveProcessedCount;
            if (waveProcessedCount < waveSize)
            {
                break;
            }

            if (circuitBreaker.GetSnapshot(GetUtcNow()).State
                != MockErpCircuitState.Closed)
            {
                break;
            }
        }

        return processedCount;
    }

    private async Task<bool> ProcessOneAsync(
        string cycleWorkerId,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider
            .GetRequiredService<IIntegrationStepProcessor>();
        return await processor.ProcessNextAsync(
            cycleWorkerId,
            cancellationToken);
    }

    private async Task DelayUntilNextCycleAsync(
        DateTime? retryAfterUtc,
        CancellationToken cancellationToken)
    {
        var delay = options.PollInterval;
        if (retryAfterUtc.HasValue)
        {
            var remaining = retryAfterUtc.Value - GetUtcNow();
            if (remaining > TimeSpan.Zero && remaining < delay)
            {
                delay = remaining;
            }
        }

        await Task.Delay(delay, timeProvider, cancellationToken);
    }

    private static bool IsPaused(MockErpCircuitState state)
    {
        return state is MockErpCircuitState.Open
            or MockErpCircuitState.HalfOpenProbeInProgress;
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }

    private static string CreateWorkerId()
    {
        var value = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        return value.Length <= 100 ? value : value[..100];
    }
}
