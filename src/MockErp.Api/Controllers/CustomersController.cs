using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/customers")]
public sealed class CustomersController(IMockErpService service)
    : ControllerBase
{
    [HttpPost("ensure")]
    [Consumes("application/json")]
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

    [HttpGet("{erpCustomerCode}/orders")]
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
