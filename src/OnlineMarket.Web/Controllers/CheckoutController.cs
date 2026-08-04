using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private readonly ICartService _cartService;
    private readonly ICustomerAddressService _addressService;
    private readonly ICheckoutService _checkoutService;
    private readonly IOrderService _orderService;
    private readonly IAuthService _authService;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ICartService cartService,
        ICustomerAddressService addressService,
        ICheckoutService checkoutService,
        IOrderService orderService,
        IAuthService authService,
        ILogger<CheckoutController> logger)
    {
        _cartService = cartService;
        _addressService = addressService;
        _checkoutService = checkoutService;
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

            var cart = await _cartService.GetOrCreateActiveCartAsync(customerId.Value);
            if (!cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Ödeme yapmak için sepetinizde en az bir ürün bulunmalıdır.";
                return RedirectToAction("Index", "Cart");
            }

            var addresses = await _addressService.GetCustomerAddressesAsync(customerId.Value);
            if (!addresses.Any())
            {
                TempData["ErrorMessage"] = "Sipariş verebilmek için lütfen önce bir teslimat adresi ekleyiniz.";
                return RedirectToAction("Create", "Address");
            }

            var defaultAddress = addresses.FirstOrDefault(a => a.IsDefault) ?? addresses.First();

            var viewModel = new CheckoutIndexViewModel
            {
                Cart = cart,
                Addresses = addresses,
                SelectedAddressId = defaultAddress.Id
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in CheckoutController.Index.");
            TempData["ErrorMessage"] = "Ödeme sayfası yüklenirken bir hata oluştu.";
            return RedirectToAction("Index", "Cart");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessCheckout(CheckoutIndexViewModel model)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            var request = new CheckoutRequestDto(
                model.SelectedAddressId,
                model.CardHolderName,
                model.CardNumberMasked,
                model.SimulateSuccess
            );

            var result = await _checkoutService.ExecuteCheckoutAsync(customerId.Value, request);
            if (result.Success && result.OrderId.HasValue)
            {
                return RedirectToAction(nameof(Success), new { id = result.OrderId.Value });
            }

            var cart = await _cartService.GetOrCreateActiveCartAsync(customerId.Value);
            var addresses = await _addressService.GetCustomerAddressesAsync(customerId.Value);

            model.Cart = cart;
            model.Addresses = addresses;
            model.ErrorMessage = result.ErrorMessage ?? "Sipariş tamamlanamadı.";

            return View("Index", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing checkout for customer {CustomerId}.", customerId);
            model.ErrorMessage = "Sipariş işlenirken beklenmeyen bir hata oluştu: " + ex.Message;
            return View("Index", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Success(Guid id)
    {
        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue) return RedirectToAction("Login", "Account");

            var order = await _orderService.GetOrderByIdAsync(id, customerId.Value);
            if (order == null) return NotFound();

            return View(new OrderSuccessViewModel
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error viewing order success page for OrderId {OrderId}.", id);
            return RedirectToAction("Index", "Catalog");
        }
    }
}
