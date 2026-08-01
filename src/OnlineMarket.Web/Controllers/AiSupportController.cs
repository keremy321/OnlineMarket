using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Controllers;

public class AiSupportController : Controller
{
    private readonly IAiSupportService _aiSupportService;
    private readonly IAuthService _authService;

    public AiSupportController(
        IAiSupportService aiSupportService,
        IAuthService authService)
    {
        _aiSupportService = aiSupportService;
        _authService = authService;
    }

    private async Task<Guid?> GetCurrentCustomerIdAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdStr, out var userId))
        {
            var customer = await _authService.GetCustomerByUserIdAsync(userId);
            return customer?.Id;
        }
        return null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat([FromBody] AiSupportRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new AiSupportResponseDto(
                Reply: "Geçersiz istek.",
                ConversationId: Guid.NewGuid().ToString("N"),
                TimestampUtc: DateTime.UtcNow,
                Success: false,
                ErrorMessage: "Request body cannot be null."));
        }

        var customerId = await GetCurrentCustomerIdAsync();
        var response = await _aiSupportService.ProcessCustomerQueryAsync(request, customerId, cancellationToken);
        return Json(response);
    }
}
