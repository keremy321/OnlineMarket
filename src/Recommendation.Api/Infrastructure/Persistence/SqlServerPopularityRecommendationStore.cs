using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerPopularityRecommendationStore(
    RecommendationDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<SqlServerPopularityRecommendationStore> logger)
    : IPopularityRecommendationStore
{
    private const string RecalculationLockName =
        "RecommendationRecalculation";
    private const string FailedRunMessage =
        "Popularity recalculation failed.";

    public async Task<IReadOnlyList<PopularityRecommendationItem>>
        GetPopularAsync(
            int limit,
            CancellationToken cancellationToken = default)
    {
        return await dbContext.ProductPopularity
            .AsNoTracking()
            .Where(item => item.Product.IsActive && item.Product.IsInStock)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ProductId)
            .Take(limit)
            .Select(item => new PopularityRecommendationItem(
                item.ProductId,
                item.Score,
                item.SoldQuantity,
                item.OrderCount,
                item.WindowStartUtc,
                item.WindowEndUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<PopularityRecalculationResult> RecalculateAsync(
        PopularityCalculationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var connectionWasOpen = connection.State == ConnectionState.Open;
        if (!connectionWasOpen)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var lockAcquired = false;
        var runCreated = false;
        var runId = Guid.NewGuid();
        try
        {
            lockAcquired = await TryAcquireLockAsync(
                connection,
                cancellationToken);
            if (!lockAcquired)
            {
                logger.LogInformation(
                    "Rejected overlapping popularity recalculation request.");
                return new PopularityRecalculationResult(
                    PopularityRecalculationOutcome.AlreadyInProgress,
                    null,
                    0);
            }

            var startedAtUtc = GetUtcNow();
            var run = new RecommendationRun
            {
                Id = runId,
                RunType = RecommendationRunType.Popularity,
                Status = RecommendationRunStatus.Running,
                StartedAtUtc = startedAtUtc,
                ParametersJson = JsonSerializer.Serialize(settings),
                CorrelationId = Guid.NewGuid()
            };
            dbContext.RecommendationRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
            runCreated = true;

            var windowEndUtc = startedAtUtc;
            var windowStartUtc = windowEndUtc.AddDays(-settings.WindowDays);
            var aggregates = await ReadAggregatesAsync(
                windowStartUtc,
                windowEndUtc,
                cancellationToken);
            var projections = PopularityRecommendationService.Calculate(
                aggregates,
                windowStartUtc,
                windowEndUtc,
                settings);
            var inputRecordCount = checked((int)aggregates.Sum(
                item => (long)item.DistinctOrderCount));

            await ReplaceProjectionAsync(
                run,
                projections,
                inputRecordCount,
                windowStartUtc,
                windowEndUtc,
                cancellationToken);

            logger.LogInformation(
                "Completed popularity recalculation run {RunId} with {InputRecordCount} input records and {OutputRecordCount} outputs.",
                runId,
                inputRecordCount,
                projections.Count);
            return new PopularityRecalculationResult(
                PopularityRecalculationOutcome.Succeeded,
                runId,
                projections.Count);
        }
        catch (Exception exception)
        {
            if (runCreated)
            {
                await MarkRunFailedAsync(runId);
            }

            logger.LogError(
                exception,
                "Popularity recalculation run {RunId} failed; the previous projection was retained.",
                runId);
            throw;
        }
        finally
        {
            if (lockAcquired)
            {
                await ReleaseLockAsync(connection);
            }

            if (!connectionWasOpen)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<IReadOnlyList<PopularityAggregate>>
        ReadAggregatesAsync(
            DateTime windowStartUtc,
            DateTime windowEndUtc,
            CancellationToken cancellationToken)
    {
        var raw = await dbContext.OrderSnapshotItems
            .AsNoTracking()
            .Where(item =>
                item.Order.OccurredAtUtc >= windowStartUtc
                && item.Order.OccurredAtUtc < windowEndUtc
                && item.Product.IsActive
                && item.Product.IsInStock)
            .GroupBy(item => item.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                SoldQuantity = group.Sum(item => (long)item.Quantity),
                DistinctOrderCount = group
                    .Select(item => item.OrderId)
                    .Distinct()
                    .Count(),
                LastPurchasedAtUtc = group.Max(
                    item => item.Order.OccurredAtUtc)
            })
            .ToListAsync(cancellationToken);

        return raw.Select(item => new PopularityAggregate(
                item.ProductId,
                checked((int)item.SoldQuantity),
                item.DistinctOrderCount,
                item.LastPurchasedAtUtc))
            .ToArray();
    }

    private async Task ReplaceProjectionAsync(
        RecommendationRun run,
        IReadOnlyList<PopularityProjection> projections,
        int inputRecordCount,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await dbContext.ProductPopularity.ExecuteDeleteAsync(
                cancellationToken);
            dbContext.ProductPopularity.AddRange(projections.Select(item =>
                new ProductPopularity
                {
                    ProductId = item.ProductId,
                    WindowStartUtc = windowStartUtc,
                    WindowEndUtc = windowEndUtc,
                    SoldQuantity = item.SoldQuantity,
                    OrderCount = item.DistinctOrderCount,
                    Score = item.Score,
                    RunId = run.Id,
                    CalculatedAtUtc = GetUtcNow()
                }));

            run.Status = RecommendationRunStatus.Succeeded;
            run.CompletedAtUtc = GetUtcNow();
            run.InputRecordCount = inputRecordCount;
            run.OutputRecordCount = projections.Count;
            run.ErrorMessage = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task MarkRunFailedAsync(Guid runId)
    {
        dbContext.ChangeTracker.Clear();
        var run = await dbContext.RecommendationRuns.SingleAsync(
            item => item.Id == runId,
            CancellationToken.None);
        run.Status = RecommendationRunStatus.Failed;
        run.CompletedAtUtc = GetUtcNow();
        run.ErrorMessage = FailedRunMessage;
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<bool> TryAcquireLockAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0,
                @DbPrincipal = 'public';
            SELECT @Result;
            """;
        AddParameter(command, "@Resource", RecalculationLockName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) >= 0;
    }

    private static async Task ReleaseLockAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_releaseapplock
                @Resource = @Resource,
                @LockOwner = 'Session',
                @DbPrincipal = 'public';
            """;
        AddParameter(command, "@Resource", RecalculationLockName);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }
}
