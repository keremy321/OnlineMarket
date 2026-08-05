using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

/// <summary>
/// Simulates the ERP system's accounting ledger: recording the sales-invoice
/// voucher for an existing order.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/accounting-entries")]
public sealed class AccountingEntriesController(IMockErpService service)
    : ControllerBase
{
    /// <summary>
    /// Records the accounting voucher (debit/credit lines) for an existing
    /// ERP order.
    /// </summary>
    /// <remarks>
    /// Requires a unique <c>Idempotency-Key</c> header. Replaying the same key
    /// with an identical request body returns the original 201 response
    /// again. Replaying the same key with a different body, or an order that
    /// already has an accounting entry, returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The order to post an accounting entry for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Accounting Entries")]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<CreateAccountingEntryResponse>(
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
        [FromBody] CreateAccountingEntryRequest request,
        CancellationToken cancellationToken)
    {
        return MockErpControllerResults.FromOperation(
            await service.CreateAccountingEntryAsync(
                request,
                IdempotencyKeyFilter.GetRequiredKey(HttpContext),
                cancellationToken));
    }
}
