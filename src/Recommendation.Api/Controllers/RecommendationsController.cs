using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/recommendations")]
public sealed class RecommendationsController(
    IPopularityRecommendationService popularityService,
    IFrequentlyBoughtTogetherRecommendationService fbtService,
    ICartCompletionRecommendationService cartCompletionService,
    ISimilarRecommendationService similarService,
    IPersonalizedRecommendationService personalizedService,
    IRecommendationModelOrchestrationService modelOrchestrationService)
    : ControllerBase
{
    [HttpGet("popular")]
    [ProducesResponseType<IReadOnlyList<RecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecommendationResponse>>>
        GetPopular(
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await popularityService.GetPopularAsync(
            limit,
            cancellationToken);
        return Ok(items.Select(MapRecommendation).ToArray());
    }

    [HttpGet("customers/{customerId:guid}")]
    [ProducesResponseType<IReadOnlyList<PersonalizedRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<PersonalizedRecommendationResponse>>> GetPersonalized(
            Guid customerId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            [FromQuery(Name = "excludePreviouslyPurchased")]
            bool excludePreviouslyPurchased = true,
            CancellationToken cancellationToken = default)
    {
        var items = await personalizedService.GetAsync(
            customerId,
            limit,
            excludePreviouslyPurchased,
            cancellationToken);
        return Ok(items.Select(MapPersonalizedRecommendation).ToArray());
    }

    [HttpGet("fbt/{productId:guid}")]
    [ProducesResponseType<
        IReadOnlyList<FrequentlyBoughtTogetherRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<FrequentlyBoughtTogetherRecommendationResponse>>> GetFbt(
            Guid productId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await fbtService.GetAsync(
            productId,
            limit,
            cancellationToken);
        return Ok(items.Select(MapFbtRecommendation).ToArray());
    }

    [HttpPost("cart")]
    [ProducesResponseType<
        IReadOnlyList<CartCompletionRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<CartCompletionRecommendationResponse>>> GetCartCompletion(
            [FromBody] CartCompletionRecommendationRequest request,
            CancellationToken cancellationToken)
    {
        var items = await cartCompletionService.GetAsync(
            request.ProductIds!,
            request.Limit,
            cancellationToken);
        return Ok(items.Select(MapCartCompletionRecommendation).ToArray());
    }

    [HttpGet("similar/{productId:guid}")]
    [ProducesResponseType<IReadOnlyList<SimilarRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimilarRecommendationResponse>>>
        GetSimilar(
            Guid productId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await similarService.GetAsync(
            productId,
            limit,
            cancellationToken);
        return Ok(items.Select(MapSimilarRecommendation).ToArray());
    }

    [HttpPost("recalculate-models")]
    [ProducesResponseType<RecommendationModelRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RecalculateModels(
        CancellationToken cancellationToken)
    {
        var result = await modelOrchestrationService.RecalculateAsync(
            cancellationToken);
        if (result.Outcome != RecommendationModelClientOutcome.Succeeded
            || result.Metadata is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    "RecommendationModel.Unavailable",
                    "Recommendation model training is currently unavailable.",
                    true));
        }

        return Ok(new RecommendationModelRecalculationResponse(
            result.Metadata.ModelVersion,
            "Succeeded",
            result.Metadata.TrainedAtUtc,
            result.Metadata.ProductCount,
            result.Metadata.SubjectCount,
            result.Metadata.InteractionCount,
            result.Metadata.InputHash,
            result.Metadata.Algorithm,
            result.Metadata.AlgorithmComponents,
            new RecommendationModelComponentStatusesContract(
                new RecommendationModelComponentStatusContract(
                    result.Metadata.Components.Tfidf.Status,
                    result.Metadata.Components.Tfidf
                        .TrainingDurationMilliseconds),
                new RecommendationModelComponentStatusContract(
                    result.Metadata.Components.Als.Status,
                    result.Metadata.Components.Als
                        .TrainingDurationMilliseconds)),
            new RecommendationModelAlsParametersContract(
                result.Metadata.AlsParameters.Factors,
                result.Metadata.AlsParameters.Regularization,
                result.Metadata.AlsParameters.Iterations,
                result.Metadata.AlsParameters.Alpha,
                result.Metadata.AlsParameters.RandomSeed)));
    }

    [HttpPost("recalculate")]
    [ProducesResponseType<RecommendationRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recalculate(
        CancellationToken cancellationToken)
    {
        var result = await popularityService.RecalculateAsync(
            cancellationToken);
        if (result.Outcome
            == PopularityRecalculationOutcome.AlreadyInProgress)
        {
            return Conflict(new ApiErrorResponse(
                "Recommendation.RunAlreadyInProgress",
                "A recommendation recalculation is already in progress.",
                false));
        }

        return Ok(new RecommendationRecalculationResponse(
            result.RunId
                ?? throw new InvalidOperationException(
                    "A successful recalculation did not return a run ID."),
            "Succeeded",
            result.OutputRecordCount));
    }

    [HttpPost("recalculate-fbt")]
    [ProducesResponseType<RecommendationRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecalculateFbt(
        CancellationToken cancellationToken)
    {
        var result = await fbtService.RecalculateAsync(cancellationToken);
        if (result.Outcome == FbtRecalculationOutcome.AlreadyInProgress)
        {
            return Conflict(new ApiErrorResponse(
                "Recommendation.RunAlreadyInProgress",
                "A recommendation recalculation is already in progress.",
                false));
        }

        return Ok(new RecommendationRecalculationResponse(
            result.RunId
                ?? throw new InvalidOperationException(
                    "A successful recalculation did not return a run ID."),
            "Succeeded",
            result.OutputRecordCount));
    }

    private static RecommendationResponse MapRecommendation(
        PopularityRecommendationItem item)
    {
        return new RecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Popular),
            "Popularity.RecentConfirmedOrders",
            "Popular based on recent confirmed orders.",
            new PopularityMetricsResponse(
                item.SoldQuantity,
                item.DistinctOrderCount,
                item.WindowStartUtc,
                item.WindowEndUtc));
    }

    private static FrequentlyBoughtTogetherRecommendationResponse
        MapFbtRecommendation(FbtRecommendationItem item)
    {
        return new FrequentlyBoughtTogetherRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.FrequentlyBoughtTogether),
            "Association.FrequentlyBoughtTogether",
            "Frequently purchased with this product.",
            new AssociationMetricsResponse(
                item.PairOrderCount,
                item.Support,
                item.Confidence,
                item.Lift));
    }

    private static CartCompletionRecommendationResponse
        MapCartCompletionRecommendation(CartCompletionRecommendationItem item)
    {
        return new CartCompletionRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.CartCompletion),
            "Association.CartCompletion",
            "Frequently purchased with products in the cart.",
            new CartCompletionMetricsResponse(
                item.SupportingCartProductCount,
                item.Confidence,
                item.Lift));
    }

    private static SimilarRecommendationResponse MapSimilarRecommendation(
        SimilarRecommendationItem item)
    {
        return new SimilarRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Similar),
            item.RankingSource == SimilarRankingSource.PythonTfidf
                ? "Similarity.TfidfContent"
                : "Similarity.DeterministicFallback",
            item.RankingSource == SimilarRankingSource.PythonTfidf
                ? "Similar product based on content features."
                : "Similar product based on local content attributes.",
            new SimilarityMetricsResponse(
                item.TfidfScore,
                item.PopularityScore,
                item.FrequentlyBoughtTogetherScore,
                item.FallbackScore,
                item.RankingSource.ToString(),
                item.ModelVersion));
    }

    private static PersonalizedRecommendationResponse
        MapPersonalizedRecommendation(PersonalizedRecommendationItem item)
    {
        var python = item.RankingSource
            == PersonalizedRankingSource.PythonImplicitAls;
        return new PersonalizedRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Personalized),
            python
                ? "Personalized.ImplicitAls"
                : "Personalized.PreferenceFallback",
            python
                ? "Recommended from pseudonymous purchase interactions."
                : "Recommended from purchase preferences and popularity.",
            new PersonalizedMetricsResponse(
                item.Confidence,
                item.RankingSource.ToString(),
                item.ModelVersion));
    }
}
