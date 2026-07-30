using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ErpIntegration.Api.Controllers;

[ApiController]
[Route("api/v1/integration/orders")]
public sealed class IntegrationOrdersController(
    IIntegrationOrderService orderService)
    : ControllerBase
{
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
