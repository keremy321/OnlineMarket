using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;
using ErpIntegration.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ErpIntegration.Api.Tests;

[Collection(IntegrationSqlServerCollection.CollectionName)]
public sealed class IntegrationWorkerSqlServerTests(
    IntegrationSqlServerFixture fixture)
{
    [Fact]
    public async Task Concurrent_workers_claim_one_step_once_and_expired_lock_is_reclaimed()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var request = IntegrationApiTestData.CreateValidRequest();
        await IntegrationWorkerTestSupport.AcceptAsync(database, request);
        var nowUtc = DateTime.UtcNow;

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstStore = CreateStore(firstContext);
        var secondStore = CreateStore(secondContext);
        var claims = await Task.WhenAll(
            firstStore.ClaimNextAsync(
                "worker-one",
                nowUtc,
                TimeSpan.FromSeconds(5)),
            secondStore.ClaimNextAsync(
                "worker-two",
                nowUtc,
                TimeSpan.FromSeconds(5)));

        var original = Assert.Single(
            claims,
            item => item is not null)!;
        Assert.Equal(IntegrationStepType.EnsureCustomer, original.StepType);
        Assert.Equal(1, original.AttemptNumber);

        await using var reclaimContext = database.CreateContext();
        var reclaimed = await CreateStore(reclaimContext).ClaimNextAsync(
            "worker-restarted",
            nowUtc.AddSeconds(6),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(reclaimed);
        Assert.Equal(original.StepId, reclaimed.StepId);
        Assert.Equal(original.AttemptNumber, reclaimed.AttemptNumber);

        var staleResultSaved = await (claims[0] is not null
                ? firstStore
                : secondStore)
            .CompleteAsync(
                original,
                IntegrationWorkerTestSupport.Success(original),
                new StepCompletionPlan(
                    IntegrationStepStatus.Succeeded,
                    null),
                nowUtc.AddSeconds(7),
                1,
                original == claims[0] ? "worker-one" : "worker-two");
        Assert.False(staleResultSaved);

        var reclaimedResultSaved = await CreateStore(reclaimContext)
            .CompleteAsync(
                reclaimed,
                IntegrationWorkerTestSupport.Success(reclaimed),
                new StepCompletionPlan(
                    IntegrationStepStatus.Succeeded,
                    null),
                nowUtc.AddSeconds(7),
                1,
                "worker-restarted");
        Assert.True(reclaimedResultSaved);

        await using var verification = database.CreateContext();
        Assert.Single(await verification.IntegrationAttempts.ToListAsync());
        Assert.Equal(
            1,
            await verification.IntegrationSteps
                .Where(step => step.Id == original.StepId)
                .Select(step => step.AttemptCount)
                .SingleAsync());
    }

    [Fact]
    public async Task Http_execution_observes_the_committed_claim()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new ClaimVisibilityMockErpClient(database);
        await using var context = database.CreateContext();
        var processor = IntegrationWorkerTestSupport.CreateProcessor(
            context,
            client);

        Assert.True(await processor.ProcessNextAsync("visibility-worker"));
        Assert.True(client.ClaimWasVisible);
        Assert.True(client.BatchWasInProgress);
        Assert.Equal(0, client.AttemptCountDuringHttp);
    }

    [Fact]
    public async Task Transient_failure_retries_after_restart_with_same_key_and_durable_attempts()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new RecordingMockErpClient((step, callNumber) =>
            callNumber == 1
                ? IntegrationWorkerTestSupport.Transient(step)
                : IntegrationWorkerTestSupport.Success(step));

        await using (var firstContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(firstContext, client)
                .ProcessNextAsync("worker-before-restart"));
        }

        await using (var restartedContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(restartedContext, client)
                .ProcessNextAsync("worker-after-restart"));
        }

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal(
            client.Calls[0].IdempotencyKey,
            client.Calls[1].IdempotencyKey);
        await using var verification = database.CreateContext();
        var step = await verification.IntegrationSteps
            .AsNoTracking()
            .SingleAsync(item =>
                item.StepType == IntegrationStepType.EnsureCustomer);
        var attempts = await verification.IntegrationAttempts
            .AsNoTracking()
            .Where(item => item.StepId == step.Id)
            .OrderBy(item => item.AttemptNumber)
            .ToArrayAsync();
        Assert.Equal(IntegrationStepStatus.Succeeded, step.Status);
        Assert.Equal(2, step.AttemptCount);
        Assert.Equal([1, 2], attempts.Select(item => item.AttemptNumber));
        Assert.Equal(
            [
                IntegrationResultType.TransientFailure,
                IntegrationResultType.Succeeded
            ],
            attempts.Select(item => item.ResultType));
        Assert.All(attempts, item => Assert.NotNull(item.CompletedAtUtc));
        Assert.Single(await verification.ErpCustomerLinks.ToListAsync());
    }

    [Fact]
    public async Task Circuit_open_releases_claim_without_consuming_an_attempt()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new RecordingMockErpClient((step, callNumber) =>
            callNumber == 1
                ? IntegrationWorkerTestSupport.CircuitOpen(step)
                : IntegrationWorkerTestSupport.Success(step));

        await using (var openContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(openContext, client)
                .ProcessNextAsync("circuit-open-worker"));
        }

        await using (var deferredContext = database.CreateContext())
        {
            var deferredStep = await deferredContext.IntegrationSteps
                .AsNoTracking()
                .SingleAsync(step =>
                    step.StepType == IntegrationStepType.EnsureCustomer);
            Assert.Equal(0, deferredStep.AttemptCount);
            Assert.Equal(IntegrationStepStatus.Retrying, deferredStep.Status);
            Assert.Equal(
                "MockErp.CircuitOpen",
                deferredStep.LastErrorCode);
            Assert.Equal(
                0,
                await deferredContext.IntegrationAttempts.CountAsync());
        }

        await using (var recoveryContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(recoveryContext, client)
                .ProcessNextAsync("circuit-recovery-worker"));
        }

        await using var verification = database.CreateContext();
        var recoveredStep = await verification.IntegrationSteps
            .AsNoTracking()
            .SingleAsync(step =>
                step.StepType == IntegrationStepType.EnsureCustomer);
        var attempt = await verification.IntegrationAttempts
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(1, recoveredStep.AttemptCount);
        Assert.Equal(IntegrationStepStatus.Succeeded, recoveredStep.Status);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(2, client.Calls.Count);
    }

    [Fact]
    public async Task Rate_limit_is_durably_retried_with_bounded_jittered_delay()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new RecordingMockErpClient(
            (step, _) => IntegrationWorkerTestSupport.RateLimited(step));
        var workerOptions = new IntegrationWorkerOptions
        {
            PollInterval = TimeSpan.FromSeconds(3),
            LockTimeout = TimeSpan.FromSeconds(5),
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(1),
            ClaimBatchSize = 2,
            MaximumParallelism = 2
        };
        var beforeExecutionUtc = DateTime.UtcNow;

        await using (var context = database.CreateContext())
        {
            var processor = IntegrationWorkerTestSupport.CreateProcessor(
                context,
                client,
                workerOptions);
            Assert.True(await processor.ProcessNextAsync("rate-limit-worker"));
            Assert.False(await processor.ProcessNextAsync("rate-limit-worker"));
        }

        await using var verification = database.CreateContext();
        var step = await verification.IntegrationSteps
            .AsNoTracking()
            .SingleAsync(item =>
                item.StepType == IntegrationStepType.EnsureCustomer);
        Assert.Equal(IntegrationStepStatus.Retrying, step.Status);
        Assert.Equal(1, step.AttemptCount);
        Assert.NotNull(step.NextAttemptAtUtc);
        Assert.InRange(
            step.NextAttemptAtUtc!.Value,
            beforeExecutionUtc.AddSeconds(10),
            beforeExecutionUtc.AddSeconds(13));
        Assert.Single(client.Calls);
        Assert.Single(await verification.IntegrationAttempts.ToListAsync());
    }

    [Fact]
    public async Task Permanent_failure_is_not_retried()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new RecordingMockErpClient(
            (step, _) => IntegrationWorkerTestSupport.Permanent(step));

        await using var context = database.CreateContext();
        var processor = IntegrationWorkerTestSupport.CreateProcessor(
            context,
            client);
        Assert.True(await processor.ProcessNextAsync("permanent-worker"));
        Assert.False(await processor.ProcessNextAsync("permanent-worker"));

        Assert.Single(client.Calls);
        await using var verification = database.CreateContext();
        var batch = await verification.IntegrationBatches
            .AsNoTracking()
            .SingleAsync();
        var step = await verification.IntegrationSteps
            .AsNoTracking()
            .SingleAsync(item =>
                item.StepType == IntegrationStepType.EnsureCustomer);
        Assert.Equal(
            IntegrationBatchStatus.FailedPermanent,
            batch.Status);
        Assert.Equal(IntegrationStepStatus.FailedPermanent, step.Status);
        Assert.Equal(1, await verification.IntegrationAttempts.CountAsync());
    }

    [Fact]
    public async Task Later_transient_failure_marks_batch_partially_succeeded_without_rerunning_prior_step()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await IntegrationWorkerTestSupport.AcceptAsync(
            database,
            IntegrationApiTestData.CreateValidRequest());
        var client = new RecordingMockErpClient((step, callNumber) =>
            callNumber == 2
                ? IntegrationWorkerTestSupport.Transient(step)
                : IntegrationWorkerTestSupport.Success(step));

        await using (var firstContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(firstContext, client)
                .ProcessNextAsync("partial-worker-one"));
        }

        await using (var secondContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(secondContext, client)
                .ProcessNextAsync("partial-worker-two"));
        }

        await using (var partialContext = database.CreateContext())
        {
            var batch = await partialContext.IntegrationBatches
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal(
                IntegrationBatchStatus.PartiallySucceeded,
                batch.Status);
            Assert.Equal(
                IntegrationStepType.CreateOrder,
                batch.CurrentStepType);
        }

        await using (var retryContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(retryContext, client)
                .ProcessNextAsync("partial-worker-three"));
        }

        Assert.Equal(
            [
                IntegrationStepType.EnsureCustomer,
                IntegrationStepType.CreateOrder,
                IntegrationStepType.CreateOrder
            ],
            client.Calls.Select(call => call.StepType));
        Assert.Equal(
            client.Calls[1].IdempotencyKey,
            client.Calls[2].IdempotencyKey);
        await using var verification = database.CreateContext();
        var steps = await verification.IntegrationSteps
            .AsNoTracking()
            .OrderBy(step => step.SequenceNumber)
            .ToArrayAsync();
        var batchAfterRetry = await verification.IntegrationBatches
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(IntegrationStepStatus.Succeeded, steps[0].Status);
        Assert.Equal(1, steps[0].AttemptCount);
        Assert.Equal(IntegrationStepStatus.Succeeded, steps[1].Status);
        Assert.Equal(2, steps[1].AttemptCount);
        Assert.Equal(
            IntegrationBatchStatus.InProgress,
            batchAfterRetry.Status);
        Assert.Equal(
            IntegrationStepType.CreateStockMovement,
            batchAfterRetry.CurrentStepType);
    }

    [Fact]
    public async Task Manual_retry_resumes_only_the_exhausted_step()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var request = IntegrationApiTestData.CreateValidRequest();
        await IntegrationWorkerTestSupport.AcceptAsync(database, request);
        await using (var setup = database.CreateContext())
        {
            var first = await setup.IntegrationSteps.SingleAsync(step =>
                step.StepType == IntegrationStepType.EnsureCustomer);
            first.MaxAttempts = 1;
            await setup.SaveChangesAsync();
        }

        var client = new RecordingMockErpClient((step, callNumber) =>
            callNumber == 1
                ? IntegrationWorkerTestSupport.Transient(step)
                : IntegrationWorkerTestSupport.Success(step));
        await using (var failureContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(failureContext, client)
                .ProcessNextAsync("automatic-worker"));
        }

        await using (var exhaustedContext = database.CreateContext())
        {
            var batch = await exhaustedContext.IntegrationBatches
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal(
                IntegrationBatchStatus.WaitingManualRetry,
                batch.Status);
        }

        await using (var retryContext = database.CreateContext())
        {
            var retry = await IntegrationWorkerTestSupport
                .CreateOrderService(retryContext)
                .RetryAsync(request.OrderId);
            Assert.True(retry.Succeeded);
        }

        await using (var resumeContext = database.CreateContext())
        {
            Assert.True(await IntegrationWorkerTestSupport
                .CreateProcessor(resumeContext, client)
                .ProcessNextAsync("manual-retry-worker"));
        }

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal(client.Calls[0].StepId, client.Calls[1].StepId);
        Assert.Equal(
            client.Calls[0].IdempotencyKey,
            client.Calls[1].IdempotencyKey);
        await using var verification = database.CreateContext();
        var steps = await verification.IntegrationSteps
            .AsNoTracking()
            .OrderBy(step => step.SequenceNumber)
            .ToArrayAsync();
        Assert.Equal(IntegrationStepStatus.Succeeded, steps[0].Status);
        Assert.Equal(IntegrationStepStatus.Pending, steps[1].Status);
        Assert.Equal(2, steps[0].AttemptCount);
        var attemptNumbers = await verification.IntegrationAttempts
            .OrderBy(item => item.AttemptNumber)
            .Select(item => item.AttemptNumber)
            .ToArrayAsync();
        Assert.Equal(
            [1, 2],
            attemptNumbers);
    }

    private static SqlServerIntegrationStepStore CreateStore(
        IntegrationDbContext context)
    {
        return new SqlServerIntegrationStepStore(
            context,
            NullLogger<SqlServerIntegrationStepStore>.Instance);
    }

    private sealed class ClaimVisibilityMockErpClient(
        IntegrationTestDatabase database)
        : IMockErpClient
    {
        public bool ClaimWasVisible { get; private set; }

        public bool BatchWasInProgress { get; private set; }

        public int AttemptCountDuringHttp { get; private set; }

        public async Task<StepExecutionResult> ExecuteAsync(
            ClaimedIntegrationStep step,
            CancellationToken cancellationToken = default)
        {
            await using var context = database.CreateContext();
            var state = await context.IntegrationSteps
                .AsNoTracking()
                .Where(item => item.Id == step.StepId)
                .Select(item => new
                {
                    item.Status,
                    item.LockedBy,
                    item.AttemptCount,
                    BatchStatus = item.Batch.Status
                })
                .SingleAsync(cancellationToken);
            ClaimWasVisible =
                state.Status == IntegrationStepStatus.InProgress
                && state.LockedBy == "visibility-worker";
            BatchWasInProgress =
                state.BatchStatus == IntegrationBatchStatus.InProgress;
            AttemptCountDuringHttp = state.AttemptCount;
            return IntegrationWorkerTestSupport.Success(step);
        }
    }
}
