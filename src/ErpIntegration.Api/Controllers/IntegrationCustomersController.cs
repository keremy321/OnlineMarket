using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ErpIntegration.Api.Controllers;

[ApiController]
[Route("api/v1/integration/customers")]
public sealed class IntegrationCustomersController(
    IIntegrationOrderService orderService)
    : ControllerBase
{
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
