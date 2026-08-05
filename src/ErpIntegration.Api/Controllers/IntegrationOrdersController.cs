using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ErpIntegration.Api.Controllers;

/// <summary>
/// Accepts <c>OrderReadyForErpV1</c> events from the OnlineMarket.Web outbox
/// and exposes the resulting integration batch's status and retry.
/// </summary>
[ApiController]
[Route("api/v1/integration/orders")]
public sealed class IntegrationOrdersController(
    IIntegrationOrderService orderService)
    : ControllerBase
{
    /// <summary>
    /// Accepts an <c>OrderReadyForErpV1</c> event and queues its integration
    /// batch (customer upsert, order, stock movements and accounting entry
    /// steps toward the Mock ERP system).
    /// </summary>
    /// <remarks>
    /// Idempotent on <c>EventId</c>: replaying the same event with an
    /// identical canonical payload returns 202 Accepted again for the same
    /// batch rather than creating a duplicate. Replaying the same
    /// <c>EventId</c> with a different payload returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The canonical order-ready-for-ERP event.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Integration Events")]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<IntegrationOrderAcceptedResponse>(
        StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Accept(
        [FromBody] OrderReadyForErpV1Request request,
        CancellationToken cancellationToken)
    {
        var result = await orderService.AcceptAsync(
            request,
            cancellationToken);
        if (result.ValidationErrors is not null)
        {
            return BadRequest(new ValidationProblemDetails(
                result.ValidationErrors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)));
        }

        if (result.Error is not null)
        {
            return Conflict(new ApiErrorResponse(
                result.Error.Code,
                result.Error.Message,
                result.Error.Retryable));
        }

        var accepted = result.Value
            ?? throw new InvalidOperationException(
                "A successful intake result did not contain a response.");
        return AcceptedAtAction(
            nameof(Get),
            new { orderId = accepted.OrderId },
            accepted);
    }

    /// <summary>
    /// Returns the current status and per-step outcomes of an integration
    /// batch.
    /// </summary>
    /// <param name="orderId">The order ID from the accepted event.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Integration Events")]
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<IntegrationOrderStatusResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [nameof(orderId)] = ["OrderId must not be empty."]
                }));
        }

        var response = await orderService.GetOrderAsync(
            orderId,
            cancellationToken);
        return response is null
            ? NotFound(new ApiErrorResponse(
                "Integration.OrderNotFound",
                "The integration order was not found.",
                false))
            : Ok(response);
    }

    /// <summary>
    /// Retries the integration batch's first eligible failed step, reusing
    /// its original idempotency key.
    /// </summary>
    /// <remarks>
    /// Returns 404 if the batch does not exist, or 409 if no step is
    /// currently eligible for a manual retry.
    /// </remarks>
    /// <param name="orderId">The order ID from the accepted event.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Retry Operations")]
    [HttpPost("{orderId:guid}/retry")]
    [ProducesResponseType<IntegrationOrderStatusResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [nameof(orderId)] = ["OrderId must not be empty."]
                }));
        }

        var result = await orderService.RetryAsync(
            orderId,
            cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var error = result.Error
            ?? throw new InvalidOperationException(
                "A failed retry result did not contain an error.");
        var response = new ApiErrorResponse(
            error.Code,
            error.Message,
            error.Retryable);
        return error.Code.Equals(
            "Integration.OrderNotFound",
            StringComparison.Ordinal)
                ? NotFound(response)
                : Conflict(response);
    }
}
