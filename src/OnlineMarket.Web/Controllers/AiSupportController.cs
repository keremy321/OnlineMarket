using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize]
public class AiSupportController : Controller
{
    private readonly IAiSupportService _aiSupportService;
    private readonly IAuthService _authService;
    private readonly ILogger<AiSupportController> _logger;

    public AiSupportController(
        IAiSupportService aiSupportService,
        IAuthService authService,
        ILogger<AiSupportController> logger)
    {
        _aiSupportService = aiSupportService;
        _authService = authService;
        _logger = logger;
    }

    private async Task<Guid?> GetCurrentCustomerIdAsync()
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdStr, out var userId))
            {
                var customer = await _authService.GetCustomerByUserIdAsync(userId);
                return customer?.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve current customer ID.");
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

        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue)
            {
                return Unauthorized();
            }

            var response = await _aiSupportService.ProcessCustomerQueryAsync(request, customerId, cancellationToken);
            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during AI Chat execution.");
            return Json(new AiSupportResponseDto(
                Reply: "Şu anda isteğinizi işlerken geçici bir sorun oluştu. Lütfen tekrar deneyin.",
                ConversationId: request.ConversationId ?? Guid.NewGuid().ToString("N"),
                TimestampUtc: DateTime.UtcNow,
                Success: false,
                ErrorMessage: ex.Message));
        }
    }
}
