using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Application.Services;

namespace Recommendation.Api.Tests;

public sealed class PersonalizedRecommendationServiceTests
{
    [Fact]
    public async Task Customer_id_is_derived_and_valid_model_results_are_filtered()
    {
        var customerId = Guid.Parse(
            "50000000-0000-0000-0000-000000000001");
        var context = CreateContext();
        var response = new RecommendationModelPersonalizedClientResponse(
            "model-set-v1",
            "implicit_als",
            [
                ModelItem(InactiveId, 1m),
                ModelItem(PurchasedId, 0.9m),
                ModelItem(CandidateTwoId, 0.8m),
                ModelItem(CandidateOneId, 0.7m),
                ModelItem(CandidateTwoId, 0.6m)
            ]);
        var client = new StubClient(
            RecommendationModelClientOutcome.Succeeded,
            response);
        var deriver = new CapturingDeriver();
        var service = CreateService(context, client, deriver);

        var result = await service.GetAsync(customerId, 10, true);

        Assert.Equal(customerId, deriver.CustomerId);
        Assert.Equal(deriver.SubjectId, client.Request!.SubjectId);
        Assert.True(client.Request.ExcludePreviouslyPurchased);
        Assert.Collection(
            result,
            item =>
            {
                Assert.Equal(CandidateTwoId, item.ProductId);
                Assert.Equal(
                    PersonalizedRankingSource.PythonImplicitAls,
                    item.RankingSource);
                Assert.Equal("model-set-v1", item.ModelVersion);
            },
            item => Assert.Equal(CandidateOneId, item.ProductId));
    }

    [Theory]
    [InlineData(RecommendationModelClientOutcome.Unavailable)]
    [InlineData(RecommendationModelClientOutcome.InvalidResponse)]
    public async Task Model_failure_uses_deterministic_preference_fallback(
        RecommendationModelClientOutcome outcome)
    {
        var service = CreateService(
            CreateContext(),
            new StubClient(outcome, null),
            new CapturingDeriver());

        var first = await service.GetAsync(Guid.NewGuid(), 10, true);
        var second = await service.GetAsync(Guid.NewGuid(), 10, true);

        Assert.Equal(first, second);
        Assert.Equal(
            [CandidateOneId, CandidateTwoId, CandidateThreeId],
            first.Select(item => item.ProductId));
        Assert.All(
            first,
            item => Assert.Equal(
                PersonalizedRankingSource.CSharpPreferenceFallback,
                item.RankingSource));
        Assert.DoesNotContain(first, item => item.ProductId == PurchasedId);
        Assert.DoesNotContain(first, item => item.ProductId == InactiveId);
        Assert.DoesNotContain(first, item => item.ProductId == OutOfStockId);
        Assert.All(first, item => Assert.InRange(item.Score, 0m, 1m));
    }

    [Fact]
    public async Task Cold_start_result_uses_popularity_fallback()
    {
        var context = CreateContext() with
        {
            Purchases = []
        };
        var response = new RecommendationModelPersonalizedClientResponse(
            "model-set-v1",
            "cold_start_unavailable",
            []);
        var service = CreateService(
            context,
            new StubClient(
                RecommendationModelClientOutcome.Succeeded,
                response),
            new CapturingDeriver());

        var result = await service.GetAsync(Guid.NewGuid(), 10, true);

        Assert.Equal(CandidateThreeId, result[0].ProductId);
        Assert.All(
            result,
            item => Assert.Equal(
                PersonalizedRankingSource.CSharpPreferenceFallback,
                item.RankingSource));
    }

    [Fact]
    public void Fallback_breaks_score_ties_by_product_id()
    {
        var options = new PersonalizedRecommendationOptions();
        var context = new PersonalizedRecommendationContext(
            [
                Product(CandidateTwoId, OtherCategoryId, OtherBrandId, 0m),
                Product(CandidateOneId, OtherCategoryId, OtherBrandId, 0m)
            ],
            []);

        var result = PersonalizedRecommendationService.CalculateFallback(
            context,
            options,
            10,
            true);

        Assert.Equal(
            [CandidateOneId, CandidateTwoId],
            result.Select(item => item.ProductId));
    }

    [Fact]
    public async Task Limit_is_validated_and_capped_before_model_call()
    {
        var client = new StubClient(
            RecommendationModelClientOutcome.Unavailable,
            null);
        var options = new PersonalizedRecommendationOptions
        {
            DefaultLimit = 2,
            MaximumLimit = 3,
            CategoryAffinityWeight = 0.5m,
            BrandAffinityWeight = 0.2m,
            PopularityWeight = 0.3m
        };
        var service = CreateService(
            CreateContext(),
            client,
            new CapturingDeriver(),
            options);

        var result = await service.GetAsync(Guid.NewGuid(), 100, true);

        Assert.Equal(3, client.Request!.Limit);
        Assert.Equal(3, result.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetAsync(Guid.NewGuid(), 0, true));
    }

    private static PersonalizedRecommendationService CreateService(
        PersonalizedRecommendationContext context,
        StubClient client,
        CapturingDeriver deriver,
        PersonalizedRecommendationOptions? options = null)
    {
        return new PersonalizedRecommendationService(
            new StubStore(context),
            deriver,
            client,
            Options.Create(options ?? new PersonalizedRecommendationOptions()),
            NullLogger<PersonalizedRecommendationService>.Instance);
    }

    private static PersonalizedRecommendationContext CreateContext()
    {
        return new PersonalizedRecommendationContext(
            [
                Product(PurchasedId, PreferredCategoryId, PreferredBrandId, 0m),
                Product(CandidateOneId, PreferredCategoryId, PreferredBrandId, 0.1m),
                Product(CandidateTwoId, PreferredCategoryId, OtherBrandId, 0.5m),
                Product(CandidateThreeId, OtherCategoryId, OtherBrandId, 1m),
                Product(InactiveId, PreferredCategoryId, PreferredBrandId, 1m, isActive: false),
                Product(OutOfStockId, PreferredCategoryId, PreferredBrandId, 1m, isInStock: false)
            ],
            [
                new PersonalizedPurchaseSnapshot(
                    PurchasedId,
                    PreferredCategoryId,
                    PreferredBrandId,
                    4)
            ]);
    }

    private static PersonalizedProductSnapshot Product(
        Guid productId,
        Guid categoryId,
        Guid brandId,
        decimal popularity,
        bool isActive = true,
        bool isInStock = true)
    {
        return new PersonalizedProductSnapshot(
            productId,
            categoryId,
            brandId,
            isActive,
            isInStock,
            popularity);
    }

    private static RecommendationModelPersonalizedItemResponse ModelItem(
        Guid productId,
        decimal score)
    {
        return new RecommendationModelPersonalizedItemResponse(
            productId,
            score,
            null,
            "Personalized.ImplicitAls",
            "Recommended from purchase interactions.");
    }

    private sealed class StubStore(PersonalizedRecommendationContext context)
        : IPersonalizedRecommendationStore
    {
        public Task<PersonalizedRecommendationContext> GetContextAsync(
            Guid customerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(context);
        }
    }

    private sealed class CapturingDeriver : IRecommendationSubjectIdDeriver
    {
        public string SubjectId { get; } = $"v1.{new string('S', 43)}";

        public Guid? CustomerId { get; private set; }

        public string Derive(Guid customerId)
        {
            CustomerId = customerId;
            return SubjectId;
        }
    }

    private sealed class StubClient(
        RecommendationModelClientOutcome outcome,
        RecommendationModelPersonalizedClientResponse? response)
        : IRecommendationModelClient
    {
        public RecommendationModelPersonalizedRequest? Request { get; private set; }

        public Task<RecommendationModelClientResult<
            RecommendationModelPersonalizedClientResponse>>
            GetPersonalizedAsync(
                RecommendationModelPersonalizedRequest request,
                CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new RecommendationModelClientResult<
                RecommendationModelPersonalizedClientResponse>(
                    outcome,
                    response));
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelSimilarClientResponse>> GetSimilarAsync(
                RecommendationModelSimilarRequest request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<RecommendationModelClientResult<
            RecommendationModelTrainingClientResponse>> TrainAsync(
                RecommendationModelTrainingRequest request,
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

    private static readonly Guid PurchasedId = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid CandidateOneId = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid CandidateTwoId = Guid.Parse(
        "00000000-0000-0000-0000-000000000003");
    private static readonly Guid CandidateThreeId = Guid.Parse(
        "00000000-0000-0000-0000-000000000004");
    private static readonly Guid InactiveId = Guid.Parse(
        "00000000-0000-0000-0000-000000000005");
    private static readonly Guid OutOfStockId = Guid.Parse(
        "00000000-0000-0000-0000-000000000006");
    private static readonly Guid PreferredCategoryId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCategoryId = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");
    private static readonly Guid PreferredBrandId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherBrandId = Guid.Parse(
        "20000000-0000-0000-0000-000000000002");
}
