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
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService orderService,
        IAuthService authService,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
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

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue) return RedirectToAction("Login", "Account");

            var orders = await _orderService.GetCustomerOrdersAsync(customerId.Value);
            return View(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders in OrdersController.Index.");
            return View(new List<OnlineMarket.Web.Application.Models.OrderDto>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue) return RedirectToAction("Login", "Account");

            var order = await _orderService.GetOrderByIdAsync(id, customerId.Value);
            if (order == null) return NotFound();

            return View(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order {OrderId} details.", id);
            return NotFound();
        }
    }
}
