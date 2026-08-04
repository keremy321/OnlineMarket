using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Infrastructure.Http;
using ErpIntegration.Api.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Tests;

public sealed class IntegrationWorkerThrottlingTests
{
    [Fact]
    public async Task Cycle_respects_claim_batch_size_and_maximum_parallelism()
    {
        var processor = new ConcurrentRecordingProcessor();
        using var services = CreateServices(processor);
        var options = new IntegrationWorkerOptions
        {
            PollInterval = TimeSpan.FromSeconds(3),
            LockTimeout = TimeSpan.FromMinutes(2),
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryDelay = TimeSpan.FromMinutes(5),
            ClaimBatchSize = 5,
            MaximumParallelism = 2
        };
        var worker = CreateWorker(
            services,
            options,
            new MockErpCircuitBreaker(),
            TimeProvider.System);

        var processed = await worker.ProcessCycleAsync("batch-worker");

        Assert.Equal(5, processed);
        Assert.Equal(5, processor.CallCount);
        Assert.InRange(processor.MaximumConcurrency, 1, 2);
    }

    [Fact]
    public async Task Bulk_ready_work_is_released_only_one_small_batch_per_cycle()
    {
        var processor = new ConcurrentRecordingProcessor();
        using var services = CreateServices(processor);
        var options = new IntegrationWorkerOptions
        {
            PollInterval = TimeSpan.FromSeconds(3),
            LockTimeout = TimeSpan.FromMinutes(2),
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryDelay = TimeSpan.FromMinutes(5),
            ClaimBatchSize = 2,
            MaximumParallelism = 2
        };
        var worker = CreateWorker(
            services,
            options,
            new MockErpCircuitBreaker(),
            TimeProvider.System);

        var processed = await worker.ProcessCycleAsync("bulk-retry-worker");

        Assert.Equal(2, processed);
        Assert.Equal(2, processor.CallCount);
    }

    [Fact]
    public async Task Open_circuit_pauses_claiming_and_recovery_allows_one_probe_before_normal_batching()
    {
        var now = new DateTimeOffset(
            2026,
            8,
            5,
            12,
            0,
            0,
            TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var circuitBreaker = new MockErpCircuitBreaker();
        circuitBreaker.RecordTransientFailure(
            now.UtcDateTime,
            1,
            TimeSpan.FromSeconds(30));
        var processor = new ConcurrentRecordingProcessor(
            onCall: callNumber =>
            {
                if (callNumber == 1)
                {
                    circuitBreaker.RecordSuccess();
                }
            });
        using var services = CreateServices(processor);
        var options = new IntegrationWorkerOptions
        {
            PollInterval = TimeSpan.FromSeconds(3),
            LockTimeout = TimeSpan.FromMinutes(2),
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryDelay = TimeSpan.FromMinutes(5),
            ClaimBatchSize = 3,
            MaximumParallelism = 3
        };
        var worker = CreateWorker(
            services,
            options,
            circuitBreaker,
            timeProvider);

        var whileOpen = await worker.ProcessCycleAsync("recovery-worker");
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        var probeCycle = await worker.ProcessCycleAsync("recovery-worker");
        var recoveredCycle = await worker.ProcessCycleAsync("recovery-worker");

        Assert.Equal(0, whileOpen);
        Assert.Equal(1, probeCycle);
        Assert.Equal(3, recoveredCycle);
        Assert.Equal(4, processor.CallCount);
        Assert.Equal(
            MockErpCircuitState.Closed,
            circuitBreaker.GetSnapshot(
                timeProvider.GetUtcNow().UtcDateTime).State);
    }

    private static ServiceProvider CreateServices(
        IIntegrationStepProcessor processor)
    {
        return new ServiceCollection()
            .AddSingleton(processor)
            .AddSingleton<IIntegrationStepProcessor>(processor)
            .BuildServiceProvider();
    }

    private static IntegrationWorker CreateWorker(
        ServiceProvider services,
        IntegrationWorkerOptions options,
        MockErpCircuitBreaker circuitBreaker,
        TimeProvider timeProvider)
    {
        return new IntegrationWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            circuitBreaker,
            timeProvider,
            NullLogger<IntegrationWorker>.Instance);
    }

    private sealed class ConcurrentRecordingProcessor(
        Action<int>? onCall = null)
        : IIntegrationStepProcessor
    {
        private int callCount;
        private int activeCount;
        private int maximumConcurrency;

        public int CallCount => Volatile.Read(ref callCount);

        public int MaximumConcurrency => Volatile.Read(
            ref maximumConcurrency);

        public async Task<bool> ProcessNextAsync(
            string workerId,
            CancellationToken cancellationToken = default)
        {
            var currentCall = Interlocked.Increment(ref callCount);
            var active = Interlocked.Increment(ref activeCount);
            UpdateMaximum(active);
            try
            {
                onCall?.Invoke(currentCall);
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    cancellationToken);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        private void UpdateMaximum(int active)
        {
            var observed = Volatile.Read(ref maximumConcurrency);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref maximumConcurrency,
                    active,
                    observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc)
        : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}
