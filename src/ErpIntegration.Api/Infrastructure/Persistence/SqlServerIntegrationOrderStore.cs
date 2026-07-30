using System.Data;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Entities;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ErpIntegration.Api.Infrastructure.Persistence;

public sealed class SqlServerIntegrationOrderStore(
    IntegrationDbContext dbContext,
    ILogger<SqlServerIntegrationOrderStore> logger)
    : IIntegrationOrderStore
{
    public async Task<IntakeStoreResult> AcceptAsync(
        IntegrationBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var existing = await FindAcceptanceAsync(
            batch.EventId,
            cancellationToken);
        if (existing is not null)
        {
            return ResolveExisting(existing, batch.ProcessedEvent.PayloadHash);
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        try
        {
            dbContext.IntegrationBatches.Add(batch);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new IntakeStoreResult(
                IntakeStoreOutcome.Created,
                CreateAccepted(batch));
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            existing = await FindAcceptanceAsync(
                batch.EventId,
                cancellationToken);
            if (existing is not null)
            {
                return ResolveExisting(
                    existing,
                    batch.ProcessedEvent.PayloadHash);
            }

            logger.LogWarning(
                "A unique IntegrationDb identity other than EventId rejected event {EventId} for order {OrderId}.",
                batch.EventId,
                batch.MarketOrderId);
            return new IntakeStoreResult(
                IntakeStoreOutcome.OrderConflict,
                null);
        }
    }

    public Task<IntegrationOrderReadState?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.IntegrationBatches
            .AsNoTracking()
            .Where(batch => batch.MarketOrderId == orderId)
            .Select(batch => new IntegrationOrderReadState(
                batch.Id,
                batch.EventId,
                batch.MarketOrderId,
                batch.OrderNumber,
                batch.CustomerId,
                batch.CorrelationId,
                batch.Status,
                batch.CurrentStepType,
                batch.CreatedAtUtc,
                batch.StartedAtUtc,
                batch.CompletedAtUtc,
                batch.LastErrorCode,
                batch.LastErrorMessage,
                batch.Steps
                    .SelectMany(step => step.Attempts)
                    .Select(attempt => (DateTime?)attempt.StartedAtUtc)
                    .Max(),
                batch.Steps
                    .OrderBy(step => step.SequenceNumber)
                    .Select(step => new IntegrationStepReadState(
                        step.StepType,
                        step.SequenceNumber,
                        step.Status,
                        step.AttemptCount,
                        step.MaxAttempts,
                        step.NextAttemptAtUtc,
                        step.Attempts
                            .Select(attempt =>
                                (DateTime?)attempt.StartedAtUtc)
                            .Max(),
                        step.CompletedAtUtc,
                        step.ExternalReference,
                        step.LastErrorCode,
                        step.LastErrorMessage))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<IntegrationBatch?> GetForRetryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.IntegrationBatches
            .Include(batch => batch.Steps)
            .SingleOrDefaultAsync(
                batch => batch.MarketOrderId == orderId,
                cancellationToken);
    }

    public async Task<bool> SaveRetryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "A concurrent update prevented an ERP integration manual retry transition.");
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<PagedReadResult<IntegrationJobReadState>>
        GetCustomerOrdersAsync(
            Guid customerId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
    {
        var query = dbContext.IntegrationBatches
            .AsNoTracking()
            .Where(batch => batch.CustomerId == customerId);
        return await ReadPageAsync(
            query,
            skip,
            take,
            cancellationToken);
    }

    public async Task<PagedReadResult<IntegrationJobReadState>>
        GetJobsAsync(
            IntegrationBatchStatus? status,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
    {
        var query = dbContext.IntegrationBatches.AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(batch => batch.Status == status.Value);
        }

        return await ReadPageAsync(
            query,
            skip,
            take,
            cancellationToken);
    }

    private async Task<ExistingAcceptance?> FindAcceptanceAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProcessedEvents
            .AsNoTracking()
            .Where(processedEvent => processedEvent.EventId == eventId)
            .Select(processedEvent => new ExistingAcceptance(
                processedEvent.PayloadHash,
                processedEvent.Batch!.Id,
                processedEvent.EventId,
                processedEvent.Batch.MarketOrderId,
                processedEvent.Batch.OrderNumber))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<PagedReadResult<IntegrationJobReadState>>
        ReadPageAsync(
            IQueryable<IntegrationBatch> query,
            int skip,
            int take,
            CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .ThenBy(batch => batch.Id)
            .Skip(skip)
            .Take(take)
            .Select(batch => new IntegrationJobReadState(
                batch.Id,
                batch.EventId,
                batch.MarketOrderId,
                batch.OrderNumber,
                batch.CustomerId,
                batch.CorrelationId,
                batch.Status,
                batch.CurrentStepType,
                batch.CreatedAtUtc,
                batch.Steps
                    .SelectMany(step => step.Attempts)
                    .Select(attempt => (DateTime?)attempt.StartedAtUtc)
                    .Max(),
                batch.LastErrorCode,
                batch.LastErrorMessage))
            .ToListAsync(cancellationToken);

        return new PagedReadResult<IntegrationJobReadState>(
            items,
            totalCount);
    }

    private static IntakeStoreResult ResolveExisting(
        ExistingAcceptance existing,
        string incomingPayloadHash)
    {
        if (!existing.PayloadHash.Equals(
                incomingPayloadHash,
                StringComparison.Ordinal))
        {
            return new IntakeStoreResult(
                IntakeStoreOutcome.PayloadConflict,
                null);
        }

        return new IntakeStoreResult(
            IntakeStoreOutcome.Replay,
            new IntegrationOrderAcceptedResponse(
                existing.EventId,
                existing.BatchId,
                existing.OrderId,
                existing.OrderNumber,
                "Accepted"));
    }

    private static IntegrationOrderAcceptedResponse CreateAccepted(
        IntegrationBatch batch)
    {
        return new IntegrationOrderAcceptedResponse(
            batch.EventId,
            batch.Id,
            batch.MarketOrderId,
            batch.OrderNumber,
            "Accepted");
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException
        {
            Number: 2601 or 2627
        };
    }

    private sealed record ExistingAcceptance(
        string PayloadHash,
        Guid BatchId,
        Guid EventId,
        Guid OrderId,
        string OrderNumber);
}
