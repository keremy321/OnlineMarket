using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

/// <summary>
/// Simulates the ERP system's stock ledger: recording sale-driven stock
/// movements for an existing order.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/stock-movements")]
public sealed class StockMovementsController(IMockErpService service)
    : ControllerBase
{
    /// <summary>
    /// Records stock movements for an existing ERP order's line items.
    /// </summary>
    /// <remarks>
    /// Requires a unique <c>Idempotency-Key</c> header. Replaying the same key
    /// with an identical request body returns the original 201 response
    /// again. Replaying the same key with a different body, movement lines
    /// that already exist for the order/product, or insufficient stock,
    /// returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The order and its stock movement lines.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Stock Movements")]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<CreateStockMovementsResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiValidationErrorResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Create(
        [FromBody] CreateStockMovementsRequest request,
        CancellationToken cancellationToken)
    {
        return MockErpControllerResults.FromOperation(
            await service.CreateStockMovementsAsync(
                request,
                IdempotencyKeyFilter.GetRequiredKey(HttpContext),
                cancellationToken));
    }
}
