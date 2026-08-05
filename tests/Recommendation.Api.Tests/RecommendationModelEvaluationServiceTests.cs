using System.Text.Json;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelEvaluationServiceTests
{
    [Fact]
    public async Task Snapshot_is_sorted_and_contains_no_direct_customer_id()
    {
        var laterOrder = new ModelEvaluationOrder(
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            RecommendationModelEvaluationTestData.SubjectId,
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            [
                new ModelOrderInteractionItem(
                    RecommendationModelEvaluationTestData.SecondProductId,
                    1)
            ]);
        var earlierOrder = new ModelEvaluationOrder(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            RecommendationModelEvaluationTestData.SubjectId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            [
                new ModelOrderInteractionItem(
                    RecommendationModelEvaluationTestData.FirstProductId,
                    2)
            ]);
        var snapshot = new ModelEvaluationSnapshot(
            [
                Product(RecommendationModelEvaluationTestData.SecondProductId),
                Product(RecommendationModelEvaluationTestData.FirstProductId)
            ],
            [
                RecommendationModelEvaluationTestData.SecondProductId,
                RecommendationModelEvaluationTestData.FirstProductId
            ],
            [RecommendationModelEvaluationTestData.FirstProductId],
            [laterOrder, earlierOrder]);
        var client = new CapturingClient();
        var service = new RecommendationModelEvaluationService(
            new StubStore(snapshot),
            client,
            new FixedTimeProvider());

        var result = await service.EvaluateAsync();

        Assert.Equal(RecommendationModelClientOutcome.Succeeded, result.Outcome);
        var request = Assert.IsType<RecommendationModelEvaluationRequest>(
            client.Request);
        Assert.Equal(
            [
                RecommendationModelEvaluationTestData.FirstProductId,
                RecommendationModelEvaluationTestData.SecondProductId
            ],
            request.CatalogueProductIds);
        Assert.Equal(
            request.CatalogueProductIds,
            request.Products.Select(product => product.ProductId));
        Assert.Equal(
            [earlierOrder.OrderId, laterOrder.OrderId],
            request.Interactions.Select(interaction => interaction.OrderId));
        Assert.All(
            request.Interactions,
            interaction =>
            {
                Assert.Equal(
                    RecommendationModelEvaluationTestData.SubjectId,
                    interaction.SubjectId);
                Assert.Equal(DateTimeKind.Utc, interaction.OccurredAtUtc.Kind);
            });
        var serialized = JsonSerializer.Serialize(request);
        Assert.DoesNotContain(
            "customerId",
            serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ModelProductTrainingSnapshot Product(Guid productId)
    {
        return new ModelProductTrainingSnapshot(
            productId,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "Product",
            "Evaluation product",
            UnitType.Piece,
            1m,
            10m,
            true,
            true);
    }

    private sealed class StubStore(ModelEvaluationSnapshot snapshot)
        : IModelEvaluationSnapshotStore
    {
        public Task<ModelEvaluationSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private sealed class CapturingClient : IRecommendationModelClient
    {
        public RecommendationModelEvaluationRequest? Request { get; private set; }

        public Task<RecommendationModelClientResult<
            RecommendationModelEvaluationClientResponse>> EvaluateAsync(
                RecommendationModelEvaluationRequest request,
                CancellationToken cancellationToken = default)
        {
            Request = request;
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
    }
}
