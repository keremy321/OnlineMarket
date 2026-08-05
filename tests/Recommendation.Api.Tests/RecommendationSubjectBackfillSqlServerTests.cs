using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationSubjectBackfillSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    private const string SubjectKey =
        "test-only-backfill-subject-key-with-at-least-32-bytes";

    [Fact]
    public async Task Backfill_updates_only_missing_subjects_and_is_idempotent()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var product = CreateProduct();
        var firstCustomerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var secondCustomerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");
        var firstOrder = CreateOrder(firstCustomerId, null, 1);
        var secondOrder = CreateOrder(firstCustomerId, null, 2);
        var existingSubjectId = $"v1.{new string('Z', 43)}";
        var thirdOrder = CreateOrder(secondCustomerId, existingSubjectId, 3);
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.Add(product);
            setup.OrderSnapshots.AddRange(
                firstOrder,
                secondOrder,
                thirdOrder);
            setup.OrderSnapshotItems.AddRange(
                CreateItem(firstOrder.OrderId, product.ProductId, 1),
                CreateItem(secondOrder.OrderId, product.ProductId, 2),
                CreateItem(thirdOrder.OrderId, product.ProductId, 3));
            setup.ProcessedEvents.AddRange(
                CreateProcessedEvent(firstOrder),
                CreateProcessedEvent(secondOrder),
                CreateProcessedEvent(thirdOrder));
            await setup.SaveChangesAsync();
        }

        ItemState[] itemState;
        EventState[] eventState;
        await using (var before = database.CreateContext())
        {
            itemState = await before.OrderSnapshotItems
                .AsNoTracking()
                .OrderBy(item => item.OrderId)
                .Select(item => new ItemState(
                    item.OrderId,
                    item.ProductId,
                    item.Quantity))
                .ToArrayAsync();
            eventState = await before.ProcessedEvents
                .AsNoTracking()
                .OrderBy(item => item.EventId)
                .Select(item => new EventState(
                    item.EventId,
                    item.PayloadHash,
                    item.ProcessedAtUtc))
                .ToArrayAsync();
        }

        var firstResult = await RunBackfillAsync(database);

        Assert.Equal(3, firstResult.ScannedCount);
        Assert.Equal(2, firstResult.UpdatedCount);
        Assert.Equal(1, firstResult.SkippedCount);
        Assert.Equal(0, firstResult.FailureCount);

        var deriver = CreateDeriver();
        await using (var verification = database.CreateContext())
        {
            var orders = await verification.OrderSnapshots
                .AsNoTracking()
                .OrderBy(order => order.OrderId)
                .ToDictionaryAsync(order => order.OrderId);
            Assert.Equal(
                deriver.Derive(firstCustomerId),
                orders[firstOrder.OrderId].SubjectId);
            Assert.Equal(
                deriver.Derive(firstCustomerId),
                orders[secondOrder.OrderId].SubjectId);
            Assert.Equal(
                existingSubjectId,
                orders[thirdOrder.OrderId].SubjectId);
            Assert.Equal(
                itemState,
                await verification.OrderSnapshotItems
                    .AsNoTracking()
                    .OrderBy(item => item.OrderId)
                    .Select(item => new ItemState(
                        item.OrderId,
                        item.ProductId,
                        item.Quantity))
                    .ToArrayAsync());
            Assert.Equal(
                eventState,
                await verification.ProcessedEvents
                    .AsNoTracking()
                    .OrderBy(item => item.EventId)
                    .Select(item => new EventState(
                        item.EventId,
                        item.PayloadHash,
                        item.ProcessedAtUtc))
                    .ToArrayAsync());
        }

        var repeatedResult = await RunBackfillAsync(database);

        Assert.Equal(3, repeatedResult.ScannedCount);
        Assert.Equal(0, repeatedResult.UpdatedCount);
        Assert.Equal(3, repeatedResult.SkippedCount);
        Assert.Equal(0, repeatedResult.FailureCount);
    }

    private static async Task<RecommendationSubjectBackfillResult>
        RunBackfillAsync(RecommendationTestDatabase database)
    {
        await using var context = database.CreateContext();
        var service = new RecommendationSubjectBackfillService(
            new SqlServerRecommendationSubjectBackfillStore(context),
            CreateDeriver(),
            NullLogger<RecommendationSubjectBackfillService>.Instance);
        return await service.BackfillAsync();
    }

    private static HmacRecommendationSubjectIdDeriver CreateDeriver()
    {
        return new HmacRecommendationSubjectIdDeriver(
            Options.Create(new RecommendationSubjectOptions
            {
                Key = SubjectKey,
                Version = "v1"
            }));
    }

    private static ProductSnapshot CreateProduct()
    {
        return new ProductSnapshot
        {
            ProductId = Guid.NewGuid(),
            Sku = $"BACKFILL-{Guid.NewGuid():N}",
            Name = "Backfill product",
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Price = 1m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = true,
            IsInStock = true,
            SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc,
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc
        };
    }

    private static OrderSnapshot CreateOrder(
        Guid customerId,
        string? subjectId,
        int key)
    {
        return new OrderSnapshot
        {
            OrderId = Guid.Parse($"20000000-0000-0000-0000-{key:D12}"),
            OrderNumber = $"BACKFILL-{key:D4}",
            CustomerId = customerId,
            SubjectId = subjectId,
            OccurredAtUtc = RecommendationEventTestData.BaseUtc.AddMinutes(key),
            TotalQuantity = key,
            DistinctProductCount = 1,
            CorrelationId = Guid.Parse(
                $"30000000-0000-0000-0000-{key:D12}"),
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc
        };
    }

    private static OrderSnapshotItem CreateItem(
        Guid orderId,
        Guid productId,
        int quantity)
    {
        return new OrderSnapshotItem
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity
        };
    }

    private static ProcessedEvent CreateProcessedEvent(OrderSnapshot order)
    {
        return new ProcessedEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "OrderConfirmedForRecommendationV1",
            PayloadHash = new string('a', 64),
            CorrelationId = order.CorrelationId,
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc,
            ProcessedAtUtc = RecommendationEventTestData.BaseUtc
        };
    }

    private sealed record ItemState(
        Guid OrderId,
        Guid ProductId,
        int Quantity);

    private sealed record EventState(
        Guid EventId,
        string PayloadHash,
        DateTime ProcessedAtUtc);
}
