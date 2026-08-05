using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerRecommendationEventStore(
    RecommendationDbContext dbContext,
    ILogger<SqlServerRecommendationEventStore> logger)
    : IRecommendationEventStore
{
    private const int MaximumConcurrencyAttempts = 5;
    private const string ProductEventType = "ProductSnapshotChangedV1";
    private const string OrderEventType =
        "OrderConfirmedForRecommendationV1";

    public async Task<RecommendationEventStoreResult> AcceptProductAsync(
        ProductEventIntake intake,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intake);

        var existingEvent = await FindProcessedEventAsync(
            intake.Event.EventId,
            cancellationToken);
        if (existingEvent is not null)
        {
            return ResolveExisting(existingEvent, intake.PayloadHash);
        }

        for (var attempt = 1; attempt <= MaximumConcurrencyAttempts; attempt++)
        {
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                existingEvent = await FindProcessedEventAsync(
                    intake.Event.EventId,
                    cancellationToken);
                if (existingEvent is not null)
                {
                    await RollbackAndClearAsync(transaction);
                    return ResolveExisting(existingEvent, intake.PayloadHash);
                }

                var product = await dbContext.ProductSnapshots
                    .SingleOrDefaultAsync(
                        snapshot => snapshot.ProductId == intake.Event.ProductId,
                        cancellationToken);
                var shouldWriteProduct = product is null
                    || intake.Event.SourceUpdatedAtUtc
                        > product.SourceUpdatedAtUtc;

                if (shouldWriteProduct
                    && await HasSkuConflictAsync(intake, cancellationToken))
                {
                    await RollbackAndClearAsync(transaction);
                    return Outcome(
                        RecommendationEventStoreOutcome.ResourceConflict);
                }

                if (product is null)
                {
                    dbContext.ProductSnapshots.Add(CreateProduct(intake));
                }
                else if (shouldWriteProduct)
                {
                    UpdateProduct(product, intake);
                }

                dbContext.ProcessedEvents.Add(CreateProcessedEvent(
                    intake.Event.EventId,
                    ProductEventType,
                    intake.PayloadHash,
                    intake.Event.CorrelationId,
                    intake.ReceivedAtUtc));

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Outcome(RecommendationEventStoreOutcome.Created);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await RollbackAndClearAsync(transaction);
                var resolved = await ResolveAfterWriteRaceAsync(
                    intake.Event.EventId,
                    intake.PayloadHash,
                    cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }

                if (attempt == MaximumConcurrencyAttempts)
                {
                    logger.LogWarning(
                        exception,
                        "Product event {EventId} exhausted concurrent persistence retries.",
                        intake.Event.EventId);
                    return Outcome(
                        RecommendationEventStoreOutcome.ResourceConflict);
                }
            }
            catch (DbUpdateException exception)
                when (IsUniqueConstraintViolation(exception))
            {
                await RollbackAndClearAsync(transaction);
                var resolved = await ResolveAfterWriteRaceAsync(
                    intake.Event.EventId,
                    intake.PayloadHash,
                    cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }

                if (attempt == MaximumConcurrencyAttempts)
                {
                    logger.LogWarning(
                        "Product event {EventId} conflicts with a unique RecommendationDb identity.",
                        intake.Event.EventId);
                    return Outcome(
                        RecommendationEventStoreOutcome.ResourceConflict);
                }
            }
            catch
            {
                await RollbackAndClearAsync(transaction);
                throw;
            }
        }

        throw new InvalidOperationException(
            "The product event persistence retry loop exited unexpectedly.");
    }

    public async Task<RecommendationEventStoreResult> AcceptOrderAsync(
        OrderEventIntake intake,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intake);

        var existingEvent = await FindProcessedEventAsync(
            intake.Event.EventId,
            cancellationToken);
        if (existingEvent is not null)
        {
            return ResolveExisting(existingEvent, intake.PayloadHash);
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        try
        {
            existingEvent = await FindProcessedEventAsync(
                intake.Event.EventId,
                cancellationToken);
            if (existingEvent is not null)
            {
                await RollbackAndClearAsync(transaction);
                return ResolveExisting(existingEvent, intake.PayloadHash);
            }

            var productIds = intake.Event.Items!
                .Select(item => item!.ProductId)
                .ToArray();
            var existingProductCount = await dbContext.ProductSnapshots
                .AsNoTracking()
                .CountAsync(
                    product => productIds.Contains(product.ProductId),
                    cancellationToken);
            if (existingProductCount != productIds.Length)
            {
                await RollbackAndClearAsync(transaction);
                return Outcome(
                    RecommendationEventStoreOutcome.ProductSnapshotMissing);
            }

            var orderConflict = await dbContext.OrderSnapshots
                .AsNoTracking()
                .AnyAsync(
                    order => order.OrderId == intake.Event.OrderId
                        || order.OrderNumber == intake.Event.OrderNumber
                        || order.CorrelationId == intake.Event.CorrelationId,
                    cancellationToken);
            if (orderConflict)
            {
                await RollbackAndClearAsync(transaction);
                existingEvent = await FindProcessedEventAsync(
                    intake.Event.EventId,
                    cancellationToken);
                if (existingEvent is not null)
                {
                    return ResolveExisting(
                        existingEvent,
                        intake.PayloadHash);
                }

                return Outcome(
                    RecommendationEventStoreOutcome.ResourceConflict);
            }

            dbContext.OrderSnapshots.Add(CreateOrder(intake));
            dbContext.ProcessedEvents.Add(CreateProcessedEvent(
                intake.Event.EventId,
                OrderEventType,
                intake.PayloadHash,
                intake.Event.CorrelationId,
                intake.ReceivedAtUtc));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Outcome(RecommendationEventStoreOutcome.Created);
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            await RollbackAndClearAsync(transaction);
            existingEvent = await FindProcessedEventAsync(
                intake.Event.EventId,
                cancellationToken);
            if (existingEvent is not null)
            {
                return ResolveExisting(existingEvent, intake.PayloadHash);
            }

            logger.LogWarning(
                "Order event {EventId} conflicts with a unique RecommendationDb identity.",
                intake.Event.EventId);
            return Outcome(RecommendationEventStoreOutcome.ResourceConflict);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private Task<bool> HasSkuConflictAsync(
        ProductEventIntake intake,
        CancellationToken cancellationToken)
    {
        return dbContext.ProductSnapshots
            .AsNoTracking()
            .AnyAsync(
                product => product.Sku == intake.Event.Sku
                    && product.ProductId != intake.Event.ProductId,
                cancellationToken);
    }

    private async Task<ProcessedEventState?> FindProcessedEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProcessedEvents
            .AsNoTracking()
            .Where(processedEvent => processedEvent.EventId == eventId)
            .Select(processedEvent => new ProcessedEventState(
                processedEvent.PayloadHash))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<RecommendationEventStoreResult?>
        ResolveAfterWriteRaceAsync(
            Guid eventId,
            string payloadHash,
            CancellationToken cancellationToken)
    {
        var existing = await FindProcessedEventAsync(
            eventId,
            cancellationToken);
        return existing is null
            ? null
            : ResolveExisting(existing, payloadHash);
    }

    private static ProductSnapshot CreateProduct(ProductEventIntake intake)
    {
        return new ProductSnapshot
        {
            ProductId = intake.Event.ProductId,
            Sku = intake.Event.Sku!,
            Name = intake.Event.Name!,
            Description = intake.Event.Description,
            CategoryId = intake.Event.CategoryId,
            ParentCategoryId = intake.Event.ParentCategoryId,
            BrandId = intake.Event.BrandId,
            Price = intake.Event.Price,
            NetContent = intake.Event.NetContent,
            UnitType = intake.Event.UnitType,
            IsActive = intake.Event.IsActive,
            IsInStock = intake.Event.IsInStock,
            SourceUpdatedAtUtc = intake.Event.SourceUpdatedAtUtc,
            ReceivedAtUtc = intake.ReceivedAtUtc
        };
    }

    private static void UpdateProduct(
        ProductSnapshot product,
        ProductEventIntake intake)
    {
        product.Sku = intake.Event.Sku!;
        product.Name = intake.Event.Name!;
        product.Description = intake.Event.Description;
        product.CategoryId = intake.Event.CategoryId;
        product.ParentCategoryId = intake.Event.ParentCategoryId;
        product.BrandId = intake.Event.BrandId;
        product.Price = intake.Event.Price;
        product.NetContent = intake.Event.NetContent;
        product.UnitType = intake.Event.UnitType;
        product.IsActive = intake.Event.IsActive;
        product.IsInStock = intake.Event.IsInStock;
        product.SourceUpdatedAtUtc = intake.Event.SourceUpdatedAtUtc;
        product.ReceivedAtUtc = intake.ReceivedAtUtc;
    }

    private static OrderSnapshot CreateOrder(OrderEventIntake intake)
    {
        var order = new OrderSnapshot
        {
            OrderId = intake.Event.OrderId,
            OrderNumber = intake.Event.OrderNumber!,
            CustomerId = intake.Event.CustomerId,
            SubjectId = intake.SubjectId,
            OccurredAtUtc = intake.Event.OccurredAtUtc,
            TotalQuantity = intake.TotalQuantity,
            DistinctProductCount = intake.Event.Items!.Count,
            CorrelationId = intake.Event.CorrelationId,
            ReceivedAtUtc = intake.ReceivedAtUtc
        };

        foreach (var item in intake.Event.Items)
        {
            order.Items.Add(new OrderSnapshotItem
            {
                OrderId = order.OrderId,
                ProductId = item!.ProductId,
                Quantity = item.Quantity,
                Order = order
            });
        }

        return order;
    }

    private static ProcessedEvent CreateProcessedEvent(
        Guid eventId,
        string eventType,
        string payloadHash,
        Guid correlationId,
        DateTime receivedAtUtc)
    {
        return new ProcessedEvent
        {
            EventId = eventId,
            EventType = eventType,
            PayloadHash = payloadHash,
            CorrelationId = correlationId,
            ReceivedAtUtc = receivedAtUtc,
            ProcessedAtUtc = receivedAtUtc
        };
    }

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    private static RecommendationEventStoreResult ResolveExisting(
        ProcessedEventState existing,
        string incomingPayloadHash)
    {
        return Outcome(existing.PayloadHash.Equals(
            incomingPayloadHash,
            StringComparison.Ordinal)
                ? RecommendationEventStoreOutcome.Replay
                : RecommendationEventStoreOutcome.PayloadConflict);
    }

    private static RecommendationEventStoreResult Outcome(
        RecommendationEventStoreOutcome outcome)
    {
        return new RecommendationEventStoreResult(outcome);
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException
        {
            Number: 2601 or 2627
        };
    }

    private sealed record ProcessedEventState(string PayloadHash);
}
