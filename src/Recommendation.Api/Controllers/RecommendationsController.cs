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
    IFrequentlyBoughtTogetherRecommendationService fbtService)
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
}
