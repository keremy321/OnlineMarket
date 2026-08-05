using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Tests;

public sealed class SimilarRecommendationServiceTests
{
    [Theory]
    [InlineData(RecommendationModelClientOutcome.Unavailable)]
    [InlineData(RecommendationModelClientOutcome.InvalidResponse)]
    public async Task Model_failure_uses_deterministic_fallback(
        RecommendationModelClientOutcome outcome)
    {
        var context = CreateContext();
        var service = CreateService(context, outcome);

        var first = await service.GetAsync(context.Source!.ProductId, 10);
        var second = await service.GetAsync(context.Source.ProductId, 10);

        Assert.Equal(first, second);
        Assert.Collection(
            first,
            item =>
            {
                Assert.Equal(CandidateOneId, item.ProductId);
                Assert.Equal(
                    SimilarRankingSource.CSharpContentFallback,
                    item.RankingSource);
                Assert.Null(item.TfidfScore);
                Assert.Equal(item.Score, item.FallbackScore);
            },
            item => Assert.Equal(CandidateTwoId, item.ProductId));
        Assert.DoesNotContain(
            first,
            item => item.ProductId == InactiveCandidateId);
    }

    [Fact]
    public async Task Valid_model_results_map_hybrid_inputs_and_recheck_availability()
    {
        var context = CreateContext();
        var response = new RecommendationModelSimilarClientResponse(
            "hybrid-test-v1",
            "HybridSimilar",
            context.Source!.ProductId,
            [
                new RecommendationModelSimilarItemResponse(
                    InactiveCandidateId,
                    0.99m,
                    0m,
                    1m,
                    0.99m,
                    "Hybrid.Similar",
                    "Content similarity."),
                new RecommendationModelSimilarItemResponse(
                    CandidateTwoId,
                    0.60m,
                    0.70m,
                    0.20m,
                    0.80m,
                    "Hybrid.Association",
                    "Directional co-purchase affinity."),
                new RecommendationModelSimilarItemResponse(
                    CandidateOneId,
                    0.70m,
                    0.20m,
                    0.40m,
                    0.70m,
                    "Hybrid.Similar",
                    "Content similarity."),
                new RecommendationModelSimilarItemResponse(
                    CandidateTwoId,
                    0.50m,
                    0.10m,
                    0.10m,
                    0.60m,
                    "Hybrid.Similar",
                    "Duplicate lower-ranked result.")
            ]);
        var service = CreateService(
            context,
            RecommendationModelClientOutcome.Succeeded,
            response);

        var result = await service.GetAsync(context.Source.ProductId, 10);

        Assert.Collection(
            result,
            item =>
            {
                Assert.Equal(CandidateTwoId, item.ProductId);
                Assert.Equal(0.80m, item.Score);
                Assert.Equal(0.60m, item.TfidfScore);
                Assert.Equal(0.20m, item.PopularityScore);
                Assert.Equal(0.30m, item.FrequentlyBoughtTogetherScore);
                Assert.Equal(0.70m, item.CoPurchaseScore);
                Assert.Equal(SimilarRankingSource.PythonHybrid, item.RankingSource);
                Assert.Equal("Hybrid.Association", item.ReasonCode);
            },
            item => Assert.Equal(CandidateOneId, item.ProductId));
    }

    [Fact]
    public void Fallback_order_breaks_score_ties_by_product_id()
    {
        var source = Product(SourceId, popularity: 0m, fbt: 0m);
        var highId = Product(CandidateTwoId, popularity: 0m, fbt: 0m);
        var lowId = Product(CandidateOneId, popularity: 0m, fbt: 0m);

        var result = SimilarRecommendationService.CalculateFallback(
            source,
            [highId, lowId],
            10);

        Assert.Equal(
            [CandidateOneId, CandidateTwoId],
            result.Select(item => item.ProductId));
    }

    private static SimilarRecommendationService CreateService(
        SimilarProductContext context,
        RecommendationModelClientOutcome outcome,
        RecommendationModelSimilarClientResponse? response = null)
    {
        return new SimilarRecommendationService(
            new StubStore(context),
            new StubClient(outcome, response),
            Options.Create(new SimilarRecommendationOptions()),
            NullLogger<SimilarRecommendationService>.Instance);
    }

    private static SimilarProductContext CreateContext()
    {
        var source = Product(SourceId, popularity: 0.1m, fbt: 0m);
        return new SimilarProductContext(
            source,
            [
                source,
                Product(CandidateTwoId, popularity: 0.2m, fbt: 0.3m),
                Product(CandidateOneId, popularity: 0.4m, fbt: 0.5m),
                Product(
                    InactiveCandidateId,
                    popularity: 1m,
                    fbt: 1m,
                    isActive: false)
            ]);
    }

    private static SimilarProductSnapshot Product(
        Guid id,
        decimal popularity,
        decimal fbt,
        bool isActive = true)
    {
        return new SimilarProductSnapshot(
            id,
            CategoryId,
            ParentCategoryId,
            BrandId,
            10m,
            1m,
            UnitType.Piece,
            isActive,
            true,
            popularity,
            fbt);
    }

    private static readonly Guid SourceId = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid CandidateOneId = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid CandidateTwoId = Guid.Parse(
        "00000000-0000-0000-0000-000000000003");
    private static readonly Guid InactiveCandidateId = Guid.Parse(
        "00000000-0000-0000-0000-000000000004");
    private static readonly Guid CategoryId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid ParentCategoryId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid BrandId = Guid.Parse(
        "30000000-0000-0000-0000-000000000001");

    private sealed class StubStore(SimilarProductContext context)
        : ISimilarProductStore
    {
        public Task<SimilarProductContext> GetContextAsync(
            Guid sourceProductId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(context);
        }
    }

    private sealed class StubClient(
        RecommendationModelClientOutcome outcome,
        RecommendationModelSimilarClientResponse? response)
        : IRecommendationModelClient
    {
        public Task<RecommendationModelClientResult<
            RecommendationModelSimilarClientResponse>> GetSimilarAsync(
                RecommendationModelSimilarRequest request,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RecommendationModelClientResult<
                RecommendationModelSimilarClientResponse>(outcome, response));
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelTrainingClientResponse>> TrainAsync(
                RecommendationModelTrainingRequest request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelPersonalizedClientResponse>>
            GetPersonalizedAsync(
                RecommendationModelPersonalizedRequest request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelEvaluationClientResponse>> EvaluateAsync(
                RecommendationModelEvaluationRequest request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
