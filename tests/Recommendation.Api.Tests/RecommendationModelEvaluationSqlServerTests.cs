using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationModelEvaluationSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    [Fact]
    public async Task Store_exports_timestamped_subject_snapshot_without_transaction()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var first = Product(1, isActive: true, isInStock: true);
        var second = Product(2, isActive: false, isInStock: true);
        var subjectId = RecommendationModelEvaluationTestData.SubjectId;
        var customerId = Guid.NewGuid();
        var earlier = Order(
            1,
            customerId,
            subjectId,
            RecommendationEventTestData.BaseUtc);
        var later = Order(
            2,
            customerId,
            subjectId,
            RecommendationEventTestData.BaseUtc.AddDays(1));
        await using (var setup = database.CreateContext())
        {
            setup.ProductSnapshots.AddRange(first, second);
            setup.OrderSnapshots.AddRange(earlier, later);
            setup.OrderSnapshotItems.AddRange(
                new OrderSnapshotItem
                {
                    OrderId = earlier.OrderId,
                    ProductId = first.ProductId,
                    Quantity = 2
                },
                new OrderSnapshotItem
                {
                    OrderId = later.OrderId,
                    ProductId = second.ProductId,
                    Quantity = 1
                });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var store = new SqlServerModelEvaluationSnapshotStore(context);
        var client = new TransactionCheckingClient(context);
        var service = new RecommendationModelEvaluationService(
            store,
            client,
            TimeProvider.System);

        await service.EvaluateAsync();

        var request = Assert.IsType<RecommendationModelEvaluationRequest>(
            client.Request);
        Assert.Equal([first.ProductId, second.ProductId], request.CatalogueProductIds);
        Assert.Equal([first.ProductId], request.CandidateProductIds);
        Assert.Equal(2, request.Interactions.Count);
        Assert.All(
            request.Interactions,
            interaction =>
            {
                Assert.Equal(subjectId, interaction.SubjectId);
                Assert.Equal(DateTimeKind.Utc, interaction.OccurredAtUtc.Kind);
            });
        Assert.True(client.NoTransactionWasHeld);
        Assert.Null(context.Database.CurrentTransaction);
    }

    private static ProductSnapshot Product(
        int key,
        bool isActive,
        bool isInStock)
    {
        return new ProductSnapshot
        {
            ProductId = Guid.Parse($"00000000-0000-0000-0000-{key:D12}"),
            Sku = $"EVALUATION-{key:D4}",
            Name = $"Evaluation product {key}",
            CategoryId = Guid.Parse($"10000000-0000-0000-0000-{key:D12}"),
            BrandId = Guid.Parse($"20000000-0000-0000-0000-{key:D12}"),
            Price = 10m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = isActive,
            IsInStock = isInStock,
            SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc,
            ReceivedAtUtc = RecommendationEventTestData.BaseUtc
        };
    }

    private static OrderSnapshot Order(
        int key,
        Guid customerId,
        string subjectId,
        DateTime occurredAtUtc)
    {
        return new OrderSnapshot
        {
            OrderId = Guid.Parse($"40000000-0000-0000-0000-{key:D12}"),
            OrderNumber = $"EVALUATION-{key:D4}",
            CustomerId = customerId,
            SubjectId = subjectId,
            OccurredAtUtc = occurredAtUtc,
            TotalQuantity = 1,
            DistinctProductCount = 1,
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = occurredAtUtc
        };
    }

    private sealed class TransactionCheckingClient(
        RecommendationDbContext context)
        : IRecommendationModelClient
    {
        public RecommendationModelEvaluationRequest? Request { get; private set; }

        public bool NoTransactionWasHeld { get; private set; }

        public Task<RecommendationModelClientResult<
            RecommendationModelEvaluationClientResponse>> EvaluateAsync(
                RecommendationModelEvaluationRequest request,
                CancellationToken cancellationToken = default)
        {
            Request = request;
            NoTransactionWasHeld = context.Database.CurrentTransaction is null;
            return Task.FromResult(new RecommendationModelClientResult<
                RecommendationModelEvaluationClientResponse>(
                    RecommendationModelClientOutcome.Succeeded,
                    RecommendationModelEvaluationTestData.Response(
                        request.EvaluationVersion)));
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelTrainingClientResponse>> TrainAsync(
                RecommendationModelTrainingRequest request,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RecommendationModelClientResult<
            RecommendationModelSimilarClientResponse>> GetSimilarAsync(
                RecommendationModelSimilarRequest request,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RecommendationModelClientResult<
            RecommendationModelPersonalizedClientResponse>> GetPersonalizedAsync(
                RecommendationModelPersonalizedRequest request,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
