using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/stock-movements")]
public sealed class StockMovementsController(IMockErpService service)
    : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
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
