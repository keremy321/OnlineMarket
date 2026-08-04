using System.Data;
using System.Data.Common;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Entities;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ErpIntegration.Api.Infrastructure.Persistence;

public sealed class SqlServerIntegrationStepStore(
    IntegrationDbContext dbContext,
    ILogger<SqlServerIntegrationStepStore> logger)
    : IIntegrationStepStore
{
    public async Task<ClaimedIntegrationStep?> ClaimNextAsync(
        string workerId,
        DateTime nowUtc,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        var expiredBeforeUtc = nowUtc.Subtract(lockTimeout);
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        var stepId = await ClaimStepIdAsync(
            workerId,
            nowUtc,
            expiredBeforeUtc,
            cancellationToken);
        if (!stepId.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await MarkBatchInProgressAsync(
            stepId.Value,
            nowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        dbContext.ChangeTracker.Clear();
        return await ReadClaimAsync(
            stepId.Value,
            nowUtc,
            cancellationToken);
    }

    public async Task<bool> CompleteAsync(
        ClaimedIntegrationStep claim,
        StepExecutionResult result,
        StepCompletionPlan plan,
        DateTime completedAtUtc,
        int durationMs,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(plan);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var currentOwner = await LockStepAsync(
            claim.StepId,
            cancellationToken);
        if (!string.Equals(
                currentOwner,
                workerId,
                StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Worker {WorkerId} no longer owns ERP integration step {StepId}; its result was ignored.",
                workerId,
                claim.StepId);
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var step = await dbContext.IntegrationSteps
            .Include(item => item.Batch)
            .ThenInclude(batch => batch.Steps)
            .SingleAsync(
                item => item.Id == claim.StepId,
                cancellationToken);
        if (step.Status != IntegrationStepStatus.InProgress
            || step.AttemptCount + 1 != claim.AttemptNumber
            || !string.Equals(
                step.LockedBy,
                workerId,
                StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Worker {WorkerId} produced a stale attempt number for ERP integration step {StepId}; its result was ignored.",
                workerId,
                claim.StepId);
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (result.OutboundCallMade)
        {
            step.AttemptCount = claim.AttemptNumber;
        }
        step.Status = plan.StepStatus;
        step.NextAttemptAtUtc = plan.NextAttemptAtUtc;
        step.LockedAtUtc = null;
        step.LockedBy = null;
        step.LastHttpStatusCode = result.HttpStatusCode;
        step.LastErrorType = result.Succeeded
            ? null
            : result.ResultType;
        step.LastErrorCode = result.Succeeded
            ? null
            : Truncate(result.ErrorCode, 100);
        step.LastErrorMessage = result.Succeeded
            ? null
            : Truncate(result.ErrorMessage, 1000);
        step.CompletedAtUtc = plan.StepStatus
            is IntegrationStepStatus.Succeeded
                or IntegrationStepStatus.FailedPermanent
                or IntegrationStepStatus.WaitingManualRetry
            ? completedAtUtc
            : null;
        if (result.Succeeded)
        {
            step.ExternalReference = Truncate(
                result.ExternalReference,
                100);
        }

        if (result.OutboundCallMade)
        {
            dbContext.IntegrationAttempts.Add(new IntegrationAttempt
            {
                StepId = step.Id,
                AttemptNumber = claim.AttemptNumber,
                StartedAtUtc = claim.ClaimedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationMs = durationMs,
                ResultType = result.ResultType,
                HttpStatusCode = result.HttpStatusCode,
                RequestHash = result.RequestHash,
                RequestPayloadMasked = result.RequestPayloadMasked,
                ResponsePayloadMasked = result.ResponsePayloadMasked,
                ErrorCode = result.Succeeded
                    ? null
                    : Truncate(result.ErrorCode, 100),
                ErrorMessage = result.Succeeded
                    ? null
                    : Truncate(result.ErrorMessage, 1000),
                CorrelationId = claim.CorrelationId,
                Step = step
            });
        }

        if (result.Succeeded
            && step.StepType == IntegrationStepType.EnsureCustomer)
        {
            await UpsertCustomerLinkAsync(
                claim.CustomerId,
                result.ExternalReference
                    ?? throw new InvalidOperationException(
                        "EnsureCustomer succeeded without an ERP customer code."),
                completedAtUtc,
                cancellationToken);
        }

        ApplyBatchTransition(
            step.Batch,
            step,
            result,
            plan,
            completedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Guid?> ClaimStepIdAsync(
        string workerId,
        DateTime nowUtc,
        DateTime expiredBeforeUtc,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            ;WITH Candidate AS
            (
                SELECT TOP (1) candidate.Id
                FROM dbo.IntegrationSteps AS candidate
                    WITH (UPDLOCK, READPAST, ROWLOCK)
                INNER JOIN dbo.IntegrationBatches AS batch
                    ON batch.Id = candidate.BatchId
                WHERE candidate.AttemptCount < candidate.MaxAttempts
                  AND
                  (
                      candidate.Status = @pending
                      OR
                      (
                          candidate.Status = @retrying
                          AND
                          (
                              candidate.NextAttemptAtUtc IS NULL
                              OR candidate.NextAttemptAtUtc <= @nowUtc
                          )
                      )
                      OR
                      (
                          candidate.Status = @inProgress
                          AND candidate.LockedAtUtc <= @expiredBeforeUtc
                      )
                  )
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.IntegrationSteps AS previous
                      WHERE previous.BatchId = candidate.BatchId
                        AND previous.SequenceNumber
                            < candidate.SequenceNumber
                        AND previous.Status <> @succeeded
                  )
                ORDER BY
                    batch.CreatedAtUtc,
                    candidate.SequenceNumber,
                    candidate.Id
            )
            UPDATE step
            SET Status = @inProgress,
                LockedAtUtc = @nowUtc,
                LockedBy = @workerId,
                StartedAtUtc = COALESCE(StartedAtUtc, @nowUtc),
                NextAttemptAtUtc = NULL
            OUTPUT INSERTED.Id
            FROM dbo.IntegrationSteps AS step
            INNER JOIN Candidate ON Candidate.Id = step.Id;
            """;

        await using var command = CreateCommand(commandText);
        command.Parameters.Add(new SqlParameter(
            "@pending",
            SqlDbType.TinyInt)
        {
            Value = (byte)IntegrationStepStatus.Pending
        });
        command.Parameters.Add(new SqlParameter(
            "@retrying",
            SqlDbType.TinyInt)
        {
            Value = (byte)IntegrationStepStatus.Retrying
        });
        command.Parameters.Add(new SqlParameter(
            "@inProgress",
            SqlDbType.TinyInt)
        {
            Value = (byte)IntegrationStepStatus.InProgress
        });
        command.Parameters.Add(new SqlParameter(
            "@succeeded",
            SqlDbType.TinyInt)
        {
            Value = (byte)IntegrationStepStatus.Succeeded
        });
        command.Parameters.Add(new SqlParameter(
            "@nowUtc",
            SqlDbType.DateTime2)
        {
            Scale = 3,
            Value = nowUtc
        });
        command.Parameters.Add(new SqlParameter(
            "@expiredBeforeUtc",
            SqlDbType.DateTime2)
        {
            Scale = 3,
            Value = expiredBeforeUtc
        });
        command.Parameters.Add(new SqlParameter(
            "@workerId",
            SqlDbType.NVarChar,
            100)
        {
            Value = workerId
        });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid value ? value : null;
    }

    private async Task MarkBatchInProgressAsync(
        Guid stepId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            UPDATE batch
            SET Status = @inProgress,
                CurrentStepType = step.StepType,
                StartedAtUtc = COALESCE(batch.StartedAtUtc, @nowUtc),
                CompletedAtUtc = NULL
            FROM dbo.IntegrationBatches AS batch
            INNER JOIN dbo.IntegrationSteps AS step
                ON step.BatchId = batch.Id
            WHERE step.Id = @stepId;
            """;
        await using var command = CreateCommand(commandText);
        command.Parameters.Add(new SqlParameter(
            "@inProgress",
            SqlDbType.TinyInt)
        {
            Value = (byte)IntegrationBatchStatus.InProgress
        });
        command.Parameters.Add(new SqlParameter(
            "@nowUtc",
            SqlDbType.DateTime2)
        {
            Scale = 3,
            Value = nowUtc
        });
        command.Parameters.Add(new SqlParameter(
            "@stepId",
            SqlDbType.UniqueIdentifier)
        {
            Value = stepId
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ClaimedIntegrationStep> ReadClaimAsync(
        Guid stepId,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken)
    {
        var step = await dbContext.IntegrationSteps
            .AsNoTracking()
            .Include(item => item.Batch)
            .ThenInclude(batch => batch.OrderSnapshot)
            .Include(item => item.Batch)
            .ThenInclude(batch => batch.OrderLines)
            .AsSplitQuery()
            .SingleAsync(item => item.Id == stepId, cancellationToken);
        var batch = step.Batch;
        var snapshot = batch.OrderSnapshot
            ?? throw new InvalidOperationException(
                "The integration batch is missing its order snapshot.");
        var erpCustomerCode = await dbContext.ErpCustomerLinks
            .AsNoTracking()
            .Where(link => link.CustomerId == batch.CustomerId)
            .Select(link => link.ErpCustomerCode)
            .SingleOrDefaultAsync(cancellationToken);

        return new ClaimedIntegrationStep(
            step.Id,
            batch.Id,
            step.StepType,
            step.SequenceNumber,
            checked(step.AttemptCount + 1),
            step.MaxAttempts,
            step.IdempotencyKey,
            batch.MarketOrderId,
            batch.OrderNumber,
            batch.CustomerId,
            batch.CorrelationId,
            claimedAtUtc,
            new IntegrationWorkerOrderSnapshot(
                snapshot.CustomerId,
                snapshot.FirstName,
                snapshot.LastName,
                snapshot.RecipientName,
                snapshot.Email,
                snapshot.PhoneNumber,
                snapshot.AddressLine1,
                snapshot.AddressLine2,
                snapshot.District,
                snapshot.City,
                snapshot.PostalCode,
                snapshot.CountryCode,
                AsUtc(snapshot.OrderPlacedAtUtc),
                snapshot.PaymentMethod,
                snapshot.Subtotal,
                snapshot.VatTotal,
                snapshot.GrandTotal,
                snapshot.Currency),
            batch.OrderLines
                .OrderBy(line => line.ProductId)
                .Select(line => new IntegrationWorkerOrderLine(
                    line.ProductId,
                    line.Sku,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.VatRate,
                    line.NetLineAmount,
                    line.VatAmount,
                    line.LineTotal))
                .ToArray(),
            erpCustomerCode);
    }

    private async Task<string?> LockStepAsync(
        Guid stepId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT LockedBy
            FROM dbo.IntegrationSteps WITH (UPDLOCK, ROWLOCK)
            WHERE Id = @stepId;
            """;
        await using var command = CreateCommand(commandText);
        command.Parameters.Add(new SqlParameter(
            "@stepId",
            SqlDbType.UniqueIdentifier)
        {
            Value = stepId
        });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    private async Task UpsertCustomerLinkAsync(
        Guid customerId,
        string erpCustomerCode,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        await AcquireCustomerLinkLockAsync(customerId, cancellationToken);
        var existing = await dbContext.ErpCustomerLinks
            .SingleOrDefaultAsync(
                link => link.CustomerId == customerId,
                cancellationToken);
        if (existing is null)
        {
            dbContext.ErpCustomerLinks.Add(new ErpCustomerLink
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                ErpCustomerCode = erpCustomerCode,
                CreatedAtUtc = verifiedAtUtc,
                LastVerifiedAtUtc = verifiedAtUtc
            });
            return;
        }

        existing.ErpCustomerCode = erpCustomerCode;
        existing.LastVerifiedAtUtc = verifiedAtUtc;
    }

    private async Task AcquireCustomerLinkLockAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @result;
            """;
        await using var command = CreateCommand(commandText);
        command.Parameters.Add(new SqlParameter(
            "@resource",
            SqlDbType.NVarChar,
            255)
        {
            Value = $"Integration:CustomerLink:{customerId:D}"
        });
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException(
                "The ERP customer-link lock could not be acquired.");
        }
    }

    private static void ApplyBatchTransition(
        IntegrationBatch batch,
        IntegrationStep completedStep,
        StepExecutionResult result,
        StepCompletionPlan plan,
        DateTime completedAtUtc)
    {
        if (plan.StepStatus == IntegrationStepStatus.Succeeded)
        {
            var next = batch.Steps
                .OrderBy(step => step.SequenceNumber)
                .FirstOrDefault(step =>
                    step.Status != IntegrationStepStatus.Succeeded);
            if (next is null)
            {
                batch.Status = IntegrationBatchStatus.Succeeded;
                batch.CurrentStepType = null;
                batch.CompletedAtUtc = completedAtUtc;
            }
            else
            {
                batch.Status = IntegrationBatchStatus.InProgress;
                batch.CurrentStepType = next.StepType;
                batch.CompletedAtUtc = null;
            }

            batch.LastErrorCode = null;
            batch.LastErrorMessage = null;
            return;
        }

        batch.CurrentStepType = completedStep.StepType;
        batch.LastErrorCode = Truncate(result.ErrorCode, 100);
        batch.LastErrorMessage = Truncate(result.ErrorMessage, 1000);
        switch (plan.StepStatus)
        {
            case IntegrationStepStatus.Retrying:
                batch.Status = batch.Steps.Any(step =>
                    step.SequenceNumber < completedStep.SequenceNumber
                    && step.Status == IntegrationStepStatus.Succeeded)
                        ? IntegrationBatchStatus.PartiallySucceeded
                        : IntegrationBatchStatus.InProgress;
                batch.CompletedAtUtc = null;
                break;

            case IntegrationStepStatus.WaitingManualRetry:
                batch.Status =
                    IntegrationBatchStatus.WaitingManualRetry;
                batch.CompletedAtUtc = completedAtUtc;
                break;

            case IntegrationStepStatus.FailedPermanent:
                batch.Status = IntegrationBatchStatus.FailedPermanent;
                batch.CompletedAtUtc = completedAtUtc;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported completion status '{plan.StepStatus}'.");
        }
    }

    private DbCommand CreateCommand(string commandText)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database
            .CurrentTransaction!
            .GetDbTransaction();
        command.CommandText = commandText;
        command.CommandTimeout = 30;
        return command;
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength];
    }

    private static DateTime AsUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
