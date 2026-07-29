using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public sealed class SqlServerOutboxStore : IOutboxStore
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

    private readonly OnlineMarketDbContext _dbContext;

    public SqlServerOutboxStore(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ClaimedOutboxMessageDto>> ClaimAsync(
        int batchSize,
        string workerId,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var boundedBatchSize = Math.Min(batchSize, 100);
        var expiredLockUtc = claimedAtUtc - LockTimeout;
        var claimedIds = new List<long>(boundedBatchSize);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            const string sql = """
                ;WITH [EligibleMessages] AS
                (
                    SELECT TOP (@BatchSize) *
                    FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [AttemptCount] < @MaximumAttempts
                      AND [AvailableAtUtc] <= @ClaimedAtUtc
                      AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= @ClaimedAtUtc)
                      AND
                      (
                          [Status] IN (@PendingStatus, @RetryingStatus)
                          OR
                          (
                              [Status] = @ProcessingStatus
                              AND ([LockedAtUtc] IS NULL OR [LockedAtUtc] < @ExpiredLockUtc)
                          )
                      )
                    ORDER BY [Id]
                )
                UPDATE [EligibleMessages]
                SET [Status] = @ProcessingStatus,
                    [LockedAtUtc] = @ClaimedAtUtc,
                    [LockedBy] = @WorkerId
                OUTPUT INSERTED.[Id];
                """;

            var connection = _dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = sql;

            AddParameter(command, "@BatchSize", DbType.Int32, boundedBatchSize);
            AddParameter(command, "@MaximumAttempts", DbType.Int32, MaximumAttempts);
            AddParameter(command, "@ClaimedAtUtc", DbType.DateTime2, claimedAtUtc);
            AddParameter(command, "@ExpiredLockUtc", DbType.DateTime2, expiredLockUtc);
            AddParameter(command, "@PendingStatus", DbType.Byte, (byte)OutboxStatus.Pending);
            AddParameter(command, "@ProcessingStatus", DbType.Byte, (byte)OutboxStatus.Processing);
            AddParameter(command, "@RetryingStatus", DbType.Byte, (byte)OutboxStatus.Retrying);
            AddParameter(command, "@WorkerId", DbType.String, workerId, 100);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimedIds.Add(reader.GetInt64(0));
            }

            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
        }

        if (claimedIds.Count == 0)
        {
            return [];
        }

        foreach (var trackedEntry in _dbContext.ChangeTracker
                     .Entries<OutboxMessage>()
                     .Where(entry => claimedIds.Contains(entry.Entity.Id))
                     .ToList())
        {
            trackedEntry.State = EntityState.Detached;
        }

        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                claimedIds.Contains(message.Id)
                && message.Status == OutboxStatus.Processing
                && message.LockedBy == workerId)
            .OrderBy(message => message.Id)
            .Select(message => new ClaimedOutboxMessageDto(
                message.Id,
                message.EventType,
                message.Destination,
                message.Payload))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordDeliveryResultAsync(
        OutboxDeliveryResultDto result,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result.WorkerId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == result.MessageId
                    && candidate.Status == OutboxStatus.Processing
                    && candidate.LockedBy == result.WorkerId,
                cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        message.AttemptCount++;
        message.LockedAtUtc = null;
        message.LockedBy = null;

        if (result.Succeeded)
        {
            message.Status = OutboxStatus.Processed;
            message.ProcessedAtUtc = result.CompletedAtUtc;
            message.NextAttemptAtUtc = null;
            message.LastErrorCode = null;
            message.LastError = null;
        }
        else
        {
            message.ProcessedAtUtc = null;
            message.LastErrorCode = Truncate(result.ErrorCode, 100) ?? "Delivery.Failed";
            message.LastError = Truncate(result.MaskedError, 2000) ?? "Downstream delivery failed.";

            if (result.Retryable && message.AttemptCount < MaximumAttempts)
            {
                message.Status = OutboxStatus.Retrying;
                message.NextAttemptAtUtc = result.CompletedAtUtc.AddSeconds(
                    Math.Pow(2, message.AttemptCount) * 5);
            }
            else
            {
                message.Status = OutboxStatus.FailedPermanent;
                message.NextAttemptAtUtc = null;

                if (result.Retryable && message.AttemptCount >= MaximumAttempts)
                {
                    message.LastErrorCode = "Delivery.RetryExhausted";
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value,
        int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (size.HasValue)
        {
            parameter.Size = size.Value;
        }

        command.Parameters.Add(parameter);
    }
}
