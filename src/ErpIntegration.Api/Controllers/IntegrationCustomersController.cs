using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ErpIntegration.Api.Controllers;

/// <summary>
/// Read-only lookups of a customer's integration batches (orders relayed
/// toward the ERP system).
/// </summary>
[ApiController]
[Route("api/v1/integration/customers")]
public sealed class IntegrationCustomersController(
    IIntegrationOrderService orderService)
    : ControllerBase
{
    /// <summary>
    /// Returns a page of integration batch summaries for the given customer,
    /// most recent first.
    /// </summary>
    /// <param name="customerId">The OnlineMarket customer ID.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Integration Batches")]
    [HttpGet("{customerId:guid}/orders")]
    [ProducesResponseType<PagedResponse<IntegrationJobSummaryResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOrders(
        Guid customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var errors = PaginationValidation.Validate(page, pageSize);
        if (customerId == Guid.Empty)
        {
            errors[nameof(customerId)] = ["CustomerId must not be empty."];
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(errors));
        }

        return Ok(await orderService.GetCustomerOrdersAsync(
            customerId,
            page,
            pageSize,
            cancellationToken));
    }
}
