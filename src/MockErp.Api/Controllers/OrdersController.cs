using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

/// <summary>
/// Simulates the ERP system's sales order intake.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/orders")]
public sealed class OrdersController(IMockErpService service)
    : ControllerBase
{
    /// <summary>
    /// Creates an ERP order for a previously ensured customer.
    /// </summary>
    /// <remarks>
    /// Requires a unique <c>Idempotency-Key</c> header. Replaying the same key
    /// with an identical request body returns the original 201 response
    /// again. Replaying the same key with a different body, or an order
    /// whose external/market order identity already exists, returns 409
    /// Conflict.
    /// </remarks>
    /// <param name="request">The order to create.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Orders")]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<CreateOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiValidationErrorResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        return MockErpControllerResults.FromOperation(
            await service.CreateOrderAsync(
                request,
                IdempotencyKeyFilter.GetRequiredKey(HttpContext),
                cancellationToken));
    }
}
