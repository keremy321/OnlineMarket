using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

/// <summary>
/// Simulates the ERP system's customer master data: upserting customers and
/// reading their order history.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/customers")]
public sealed class CustomersController(IMockErpService service)
    : ControllerBase
{
    /// <summary>
    /// Creates the ERP customer if it does not yet exist (identified by
    /// <c>ExternalCustomerId</c>), or updates it otherwise.
    /// </summary>
    /// <remarks>
    /// Requires a unique <c>Idempotency-Key</c> header. Replaying the same key
    /// with an identical request body returns the original response again.
    /// Replaying the same key with a different body returns 409 Conflict.
    /// </remarks>
    /// <param name="request">The customer data to upsert.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Customers")]
    [HttpPost("ensure")]
    [Consumes("application/json")]
    [ProducesResponseType<EnsureCustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<EnsureCustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiValidationErrorResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Ensure(
        [FromBody] EnsureCustomerRequest request,
        CancellationToken cancellationToken)
    {
        return MockErpControllerResults.FromOperation(
            await service.EnsureCustomerAsync(
                request,
                IdempotencyKeyFilter.GetRequiredKey(HttpContext),
                cancellationToken));
    }

    /// <summary>
    /// Returns the ERP order history for the given customer.
    /// </summary>
    /// <param name="erpCustomerCode">The ERP-assigned customer code.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Order History")]
    [HttpGet("{erpCustomerCode}/orders")]
    [ProducesResponseType<CustomerOrderHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrders(
        string erpCustomerCode,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCustomerOrdersAsync(
            erpCustomerCode,
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Response)
            : new ObjectResult(new ApiErrorResponse(
                result.Error!.Code,
                result.Error.Message,
                result.Error.Retryable))
            {
                StatusCode = result.Error.StatusCode
            };
    }
}
