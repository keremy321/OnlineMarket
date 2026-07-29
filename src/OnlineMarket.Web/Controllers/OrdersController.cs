using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;

namespace OnlineMarket.Web.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly IOrderService _orderService;
    private readonly IAuthService _authService;

    public OrdersController(IOrderService orderService, IAuthService authService)
    {
        _orderService = orderService;
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

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        var orders = await _orderService.GetCustomerOrdersAsync(customerId.Value);
        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        var order = await _orderService.GetOrderByIdAsync(id, customerId.Value);
        if (order == null) return NotFound();

        return View(order);
    }
}
