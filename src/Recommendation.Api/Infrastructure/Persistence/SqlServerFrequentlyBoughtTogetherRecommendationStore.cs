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

public sealed class SqlServerFrequentlyBoughtTogetherRecommendationStore(
    RecommendationDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<SqlServerFrequentlyBoughtTogetherRecommendationStore> logger)
    : IFrequentlyBoughtTogetherRecommendationStore
{
    private const string RecalculationLockName =
        "RecommendationRecalculation";
    private const string FailedRunMessage =
        "Frequently bought together recalculation failed.";

    public async Task<IReadOnlyList<FbtRecommendationItem>> GetAsync(
        Guid productId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ProductAffinities
            .AsNoTracking()
            .Where(rule =>
                rule.SourceProductId == productId
                && rule.RecommendedProduct.IsActive
                && rule.RecommendedProduct.IsInStock)
            .OrderByDescending(rule => rule.Score)
            .ThenByDescending(rule => rule.Confidence)
            .ThenBy(rule => rule.RecommendedProductId)
            .Take(limit)
            .Select(rule => new FbtRecommendationItem(
                rule.RecommendedProductId,
                rule.Score,
                rule.CoOccurrenceCount,
                rule.Support,
                rule.Confidence,
                rule.Lift))
            .ToListAsync(cancellationToken);
    }

    public async Task<FbtRecalculationResult> RecalculateAsync(
        FbtCalculationSettings settings,
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
                    "Rejected overlapping frequently bought together recalculation request.");
                return new FbtRecalculationResult(
                    FbtRecalculationOutcome.AlreadyInProgress,
                    null,
                    0);
            }

            var run = new RecommendationRun
            {
                Id = runId,
                RunType = RecommendationRunType.Affinity,
                Status = RecommendationRunStatus.Running,
                StartedAtUtc = GetUtcNow(),
                ParametersJson = JsonSerializer.Serialize(settings),
                CorrelationId = Guid.NewGuid()
            };
            dbContext.RecommendationRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
            runCreated = true;

            var totalOrderCount = await dbContext.OrderSnapshots
                .AsNoTracking()
                .CountAsync(cancellationToken);
            var orderProducts = await dbContext.OrderSnapshotItems
                .AsNoTracking()
                .Select(item => new FbtOrderProduct(
                    item.OrderId,
                    item.ProductId))
                .Distinct()
                .ToListAsync(cancellationToken);
            var availableTargetProductIds = (await dbContext.ProductSnapshots
                    .AsNoTracking()
                    .Where(product => product.IsActive && product.IsInStock)
                    .Select(product => product.ProductId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var projections =
                FrequentlyBoughtTogetherRecommendationService.Calculate(
                    orderProducts,
                    totalOrderCount,
                    availableTargetProductIds,
                    settings);

            await ReplaceProjectionAsync(
                run,
                projections,
                orderProducts.Count,
                cancellationToken);

            logger.LogInformation(
                "Completed frequently bought together recalculation run {RunId} with {InputRecordCount} input records and {OutputRecordCount} directional rules.",
                runId,
                orderProducts.Count,
                projections.Count);
            return new FbtRecalculationResult(
                FbtRecalculationOutcome.Succeeded,
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
                "Frequently bought together recalculation run {RunId} failed; the previous rules were retained.",
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

    private async Task ReplaceProjectionAsync(
        RecommendationRun run,
        IReadOnlyList<FbtProjection> projections,
        int inputRecordCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await dbContext.ProductAffinities.ExecuteDeleteAsync(
                cancellationToken);
            var calculatedAtUtc = GetUtcNow();
            dbContext.ProductAffinities.AddRange(projections.Select(item =>
                new ProductAffinity
                {
                    SourceProductId = item.SourceProductId,
                    RecommendedProductId = item.RecommendedProductId,
                    CoOccurrenceCount = item.PairOrderCount,
                    SourceOrderCount = item.SourceOrderCount,
                    RecommendedOrderCount = item.RecommendedOrderCount,
                    TotalOrderCount = item.TotalOrderCount,
                    Support = item.Support,
                    Confidence = item.Confidence,
                    Lift = item.Lift,
                    Score = item.Score,
                    RunId = run.Id,
                    CalculatedAtUtc = calculatedAtUtc
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
