using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

/// <summary>
/// Accepts product and order events published to the Recommendation Outbox
/// destination by OnlineMarket.Web, keeping this service's read models current.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/events")]
public sealed class RecommendationEventsController(
    IRecommendationEventIngestionService ingestionService)
    : ControllerBase
{
    /// <summary>
    /// Ingests a <c>ProductSnapshotChangedV1</c> event, creating or refreshing
    /// the product snapshot used by recommendation ranking.
    /// </summary>
    /// <remarks>
    /// Idempotent on <c>EventId</c>: replaying the same <c>EventId</c> with an
    /// identical canonical payload returns 200 OK again without reprocessing.
    /// Replaying the same <c>EventId</c> with a different payload, or a payload
    /// whose SKU conflicts with another product, returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The canonical product snapshot event.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Product Events")]
    [HttpPost("products")]
    [Consumes("application/json")]
    [ProducesResponseType<RecommendationEventAcceptedResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiValidationErrorResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> IngestProduct(
        [FromBody] ProductSnapshotChangedV1Request request,
        CancellationToken cancellationToken)
    {
        var result = await ingestionService.IngestProductAsync(
            request,
            cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Ingests an <c>OrderConfirmedForRecommendationV1</c> event, recording the
    /// confirmed order's line items as purchase interactions.
    /// </summary>
    /// <remarks>
    /// Idempotent on <c>EventId</c>: replaying the same <c>EventId</c> with an
    /// identical canonical payload returns 200 OK again without reprocessing.
    /// Replaying the same <c>EventId</c> with a different payload, or an order
    /// referencing a product with no known snapshot, returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The canonical confirmed-order event.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Order Events")]
    [HttpPost("orders")]
    [Consumes("application/json")]
    [ProducesResponseType<RecommendationEventAcceptedResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiValidationErrorResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> IngestOrder(
        [FromBody] OrderConfirmedForRecommendationV1Request request,
        CancellationToken cancellationToken)
    {
        var result = await ingestionService.IngestOrderAsync(
            request,
            cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(
        RecommendationEventIngestionResult result)
    {
        if (result.ValidationErrors is not null)
        {
            return BadRequest(new ApiValidationErrorResponse(
                "Validation.Failed",
                "Request validation failed.",
                false,
                result.ValidationErrors));
        }

        if (result.Error is not null)
        {
            return Conflict(new ApiErrorResponse(
                result.Error.Code,
                result.Error.Message,
                result.Error.Retryable));
        }

        return Ok(result.Value
            ?? throw new InvalidOperationException(
                "A successful ingestion result did not contain a response."));
    }
}
