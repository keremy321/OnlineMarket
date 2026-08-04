using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IAuthService authService,
        IChatHistoryStore chatHistoryStore,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _chatHistoryStore = chatHistoryStore;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Catalog");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result = await _authService.LoginAsync(model.Email, model.Password, model.RememberMe);
            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }
                return RedirectToAction("Index", "Catalog");
            }

            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Giriş başarısız.");
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during login for user {Email}.", model.Email);
            ModelState.AddModelError(string.Empty, "Giriş sırasında beklenmeyen bir hata oluştu.");
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Catalog");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result = await _authService.RegisterAsync(model.Email, model.Password, model.FirstName, model.LastName);
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Catalog");
            }

            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Kayıt sırasında bir hata oluştu.");
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during registration for user {Email}.", model.Email);
            ModelState.AddModelError(string.Empty, "Kayıt işlemi sırasında beklenmeyen bir hata oluştu.");
            return View(model);
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        Guid? customerId = null;

        try
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdValue, out var userId))
            {
                customerId = (await _authService.GetCustomerByUserIdAsync(userId))?.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve the customer while clearing chat history during logout.");
        }

        try
        {
            await _authService.LogoutAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during logout.");
        }

        if (customerId.HasValue)
        {
            _chatHistoryStore.ClearAccountHistory(customerId.Value);
        }

        return RedirectToAction("Index", "Catalog");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
