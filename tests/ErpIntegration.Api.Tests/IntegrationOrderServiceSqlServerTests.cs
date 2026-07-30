using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Domain.Enums;
using ErpIntegration.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ErpIntegration.Api.Tests;

[Collection(IntegrationSqlServerCollection.CollectionName)]
public sealed class IntegrationOrderServiceSqlServerTests(
    IntegrationSqlServerFixture fixture)
{
    [Fact]
    public async Task Intake_commits_the_complete_aggregate_and_replays_the_accepted_result()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);
        var request = IntegrationApiTestData.CreateValidRequest();

        var accepted = await service.AcceptAsync(request);
        var replay = await service.AcceptAsync(request);

        Assert.True(accepted.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.Equal(accepted.Value, replay.Value);

        await using var verification = database.CreateContext();
        var batch = await verification.IntegrationBatches
            .AsNoTracking()
            .Include(item => item.ProcessedEvent)
            .Include(item => item.OrderSnapshot)
            .Include(item => item.OrderLines)
            .Include(item => item.Steps)
            .SingleAsync();
        var steps = batch.Steps
            .OrderBy(step => step.SequenceNumber)
            .ToArray();

        Assert.Equal(request.EventId, batch.EventId);
        Assert.Equal(request.PaymentMethod, batch.OrderSnapshot!.PaymentMethod);
        Assert.Single(batch.OrderLines);
        Assert.Equal(4, steps.Length);
        Assert.Equal(
            [
                IntegrationStepType.EnsureCustomer,
                IntegrationStepType.CreateOrder,
                IntegrationStepType.CreateStockMovement,
                IntegrationStepType.CreateAccountingEntry
            ],
            steps.Select(step => step.StepType));
        Assert.Equal([1, 2, 3, 4], steps.Select(step => (int)step.SequenceNumber));
        Assert.Equal(
            [
                $"customer:{request.OrderId}",
                $"order:{request.OrderId}",
                $"stock:{request.OrderId}",
                $"accounting:{request.OrderId}"
            ],
            steps.Select(step => step.IdempotencyKey));
        Assert.Equal(1, await verification.ProcessedEvents.CountAsync());
        Assert.Equal(1, await verification.IntegrationBatches.CountAsync());
        Assert.Equal(1, await verification.IntegrationOrderSnapshots.CountAsync());
        Assert.Equal(1, await verification.IntegrationOrderLines.CountAsync());
        Assert.Equal(4, await verification.IntegrationSteps.CountAsync());
    }

    [Fact]
    public async Task Same_event_with_different_canonical_payload_is_a_non_retryable_conflict()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);
        var request = IntegrationApiTestData.CreateValidRequest();
        var changed = request with
        {
            PaymentMethod = PaymentMethod.TransferSimulation
        };

        Assert.True((await service.AcceptAsync(request)).Succeeded);
        var conflict = await service.AcceptAsync(changed);

        Assert.False(conflict.Succeeded);
        Assert.Equal("Idempotency.PayloadConflict", conflict.Error!.Code);
        Assert.False(conflict.Error.Retryable);

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ProcessedEvents.CountAsync());
        Assert.Equal(1, await verification.IntegrationBatches.CountAsync());
    }

    [Fact]
    public async Task Conflicting_order_identity_rolls_back_the_second_processed_event()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var orderId = Guid.NewGuid();
        var first = IntegrationApiTestData.CreateValidRequest(orderId: orderId);
        var second = IntegrationApiTestData.CreateValidRequest(orderId: orderId);

        await using (var firstContext = database.CreateContext())
        {
            Assert.True((await CreateService(firstContext).AcceptAsync(first)).Succeeded);
        }

        await using (var secondContext = database.CreateContext())
        {
            var conflict = await CreateService(secondContext).AcceptAsync(second);
            Assert.False(conflict.Succeeded);
            Assert.Equal("Integration.OrderAlreadyAccepted", conflict.Error!.Code);
        }

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ProcessedEvents.CountAsync());
        Assert.Equal(1, await verification.IntegrationBatches.CountAsync());
        Assert.Equal(1, await verification.IntegrationOrderSnapshots.CountAsync());
        Assert.Single(await verification.IntegrationOrderLines.ToListAsync());
        Assert.Equal(4, await verification.IntegrationSteps.CountAsync());
    }

    [Fact]
    public async Task Concurrent_same_event_requests_create_one_batch_and_return_one_result()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);
        var request = IntegrationApiTestData.CreateValidRequest();

        var results = await Task.WhenAll(
            firstService.AcceptAsync(request),
            secondService.AcceptAsync(request));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(results[0].Value, results[1].Value);

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ProcessedEvents.CountAsync());
        Assert.Equal(1, await verification.IntegrationBatches.CountAsync());
        Assert.Equal(4, await verification.IntegrationSteps.CountAsync());
    }

    [Fact]
    public async Task Manual_retry_moves_only_the_first_allowed_failed_step_and_keeps_its_key()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var request = IntegrationApiTestData.CreateValidRequest();
        await using (var intakeContext = database.CreateContext())
        {
            Assert.True(
                (await CreateService(intakeContext).AcceptAsync(request))
                .Succeeded);
        }

        string stableKey;
        await using (var setup = database.CreateContext())
        {
            var batch = await setup.IntegrationBatches
                .Include(item => item.Steps)
                .SingleAsync();
            var steps = batch.Steps.OrderBy(item => item.SequenceNumber).ToArray();
            steps[0].Status = IntegrationStepStatus.Succeeded;
            steps[0].CompletedAtUtc = DateTime.UtcNow;
            steps[1].Status = IntegrationStepStatus.WaitingManualRetry;
            steps[1].AttemptCount = 5;
            steps[1].MaxAttempts = 5;
            steps[1].LastErrorCode = "MockErp.Unavailable";
            batch.Status = IntegrationBatchStatus.WaitingManualRetry;
            batch.CurrentStepType = IntegrationStepType.CreateOrder;
            batch.LastErrorCode = "MockErp.Unavailable";
            stableKey = steps[1].IdempotencyKey;
            await setup.SaveChangesAsync();
        }

        await using (var retryContext = database.CreateContext())
        {
            var service = CreateService(retryContext);
            var retry = await service.RetryAsync(request.OrderId);
            Assert.True(retry.Succeeded);
            Assert.Equal("Processing", retry.Value!.Status);

            var secondRetry = await service.RetryAsync(request.OrderId);
            Assert.False(secondRetry.Succeeded);
            Assert.Equal(
                "Integration.ManualRetryNotAllowed",
                secondRetry.Error!.Code);
        }

        await using var verification = database.CreateContext();
        var persisted = await verification.IntegrationBatches
            .AsNoTracking()
            .Include(item => item.Steps)
            .SingleAsync();
        var persistedSteps = persisted.Steps
            .OrderBy(item => item.SequenceNumber)
            .ToArray();

        Assert.Equal(IntegrationStepStatus.Succeeded, persistedSteps[0].Status);
        Assert.Equal(IntegrationStepStatus.Pending, persistedSteps[1].Status);
        Assert.Equal(5, persistedSteps[1].AttemptCount);
        Assert.Equal(10, persistedSteps[1].MaxAttempts);
        Assert.Equal(stableKey, persistedSteps[1].IdempotencyKey);
        Assert.Equal(IntegrationStepStatus.Pending, persistedSteps[2].Status);
        Assert.Equal(IntegrationStepStatus.Pending, persistedSteps[3].Status);
        Assert.Equal(
            IntegrationBatchStatus.PartiallySucceeded,
            persisted.Status);
    }

    [Fact]
    public async Task Order_customer_and_job_queries_return_persisted_status()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);
        var customerId = Guid.NewGuid();
        var request = IntegrationApiTestData.CreateValidRequest(
            customerId: customerId);
        Assert.True((await service.AcceptAsync(request)).Succeeded);

        var order = await service.GetOrderAsync(request.OrderId);
        var customerOrders = await service.GetCustomerOrdersAsync(
            customerId,
            1,
            20);
        var jobs = await service.GetJobsAsync(
            IntegrationBatchStatus.Pending,
            1,
            20);

        Assert.NotNull(order);
        Assert.Equal("Pending", order.Status);
        Assert.Equal("EnsureCustomer", order.CurrentStep);
        Assert.Equal(4, order.Steps.Count);
        Assert.Single(customerOrders.Items);
        Assert.Equal(1, customerOrders.TotalCount);
        Assert.Single(jobs.Items);
        Assert.Equal(request.OrderId, jobs.Items[0].OrderId);
    }

    private static IntegrationOrderService CreateService(
        IntegrationDbContext context)
    {
        var store = new SqlServerIntegrationOrderStore(
            context,
            NullLogger<SqlServerIntegrationOrderStore>.Instance);
        return new IntegrationOrderService(
            store,
            new OrderReadyForErpV1Validator(),
            TimeProvider.System,
            NullLogger<IntegrationOrderService>.Instance);
    }
}
