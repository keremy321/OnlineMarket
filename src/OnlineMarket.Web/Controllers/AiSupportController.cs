using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Controllers;

/// <summary>
/// The only endpoint the assistant widget talks to. The browser never calls
/// Recommendation.Api or the AI provider directly.
/// </summary>
[AllowAnonymous]
public class AiSupportController : Controller
{
    /// <summary>Hard cap on the request body; the widget sends a short JSON message.</summary>
    private const int MaximumRequestBodyBytes = 8 * 1024;

    private readonly IAiSupportService _aiSupportService;
    private readonly ICustomerIdentityResolver _customerIdentityResolver;
    private readonly IOptions<AiAssistantOptions> _options;
    private readonly ILogger<AiSupportController> _logger;

    public AiSupportController(
        IAiSupportService aiSupportService,
        ICustomerIdentityResolver customerIdentityResolver,
        IOptions<AiAssistantOptions> options,
        ILogger<AiSupportController> logger)
    {
        _aiSupportService = aiSupportService;
        _customerIdentityResolver = customerIdentityResolver;
        _options = options;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [Consumes("application/json")]
    public async Task<IActionResult> Chat(
        [FromBody] AiSupportRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(CreateRejection("Geçersiz istek."));
        }

        var options = _options.Value;
        if (!options.Enabled)
        {
            return BadRequest(CreateRejection(
                "Asistan şu anda kullanılamıyor. Lütfen daha sonra tekrar deneyin."));
        }

        if (request.Message is { Length: > 0 }
            && request.Message.Length > options.EffectiveMaxMessageLength)
        {
            return BadRequest(CreateRejection(
                $"Mesajınız çok uzun. Lütfen en fazla {options.EffectiveMaxMessageLength} karakter kullanın.",
                request.ConversationId));
        }

        try
        {
            // Identity is resolved server-side; the browser never supplies a customer id.
            var customerId = await GetCurrentCustomerIdAsync();
            var response = await _aiSupportService.ProcessCustomerQueryAsync(
                request,
                customerId,
                cancellationToken);

            return Json(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The shopper navigated away or closed the panel; not an application error.
            _logger.LogDebug("An assistant chat request was cancelled by the client.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An assistant chat request could not be completed.");

            return Json(CreateRejection(
                "Şu anda isteğinizi işlerken geçici bir sorun oluştu. Lütfen tekrar deneyin.",
                request.ConversationId));
        }
    }

    private async Task<Guid?> GetCurrentCustomerIdAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        try
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdValue, out var userId))
            {
                return await _customerIdentityResolver
                    .GetActiveCustomerIdByUserIdAsync(userId);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The authenticated customer could not be resolved; the assistant will answer as a guest.");
        }

        return null;
    }

    /// <summary>
    /// Builds a safe rejection payload. No provider, infrastructure or exception detail
    /// is ever placed in <see cref="AiSupportResponseDto.ErrorMessage"/>.
    /// </summary>
    private static AiSupportResponseDto CreateRejection(string reply, string? conversationId = null) =>
        new(
            Reply: reply,
            ConversationId: string.IsNullOrWhiteSpace(conversationId)
                ? Guid.NewGuid().ToString("N")
                : conversationId,
            TimestampUtc: DateTime.UtcNow,
            SuggestedActions: null,
            Success: false,
            ErrorMessage: null,
            Intent: nameof(AiAssistantIntent.Unknown),
            UsedFallback: true,
            Products: []);
}
