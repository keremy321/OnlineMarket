using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/events")]
public sealed class RecommendationEventsController(
    IRecommendationEventIngestionService ingestionService)
    : ControllerBase
{
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
