using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/accounting-entries")]
public sealed class AccountingEntriesController(IMockErpService service)
    : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
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
