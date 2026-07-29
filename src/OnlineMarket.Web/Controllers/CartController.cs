using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly ICartService _cartService;
    private readonly ICatalogService _catalogService;
    private readonly IRecommendationClient _recommendationClient;
    private readonly IAuthService _authService;

    public CartController(
        ICartService cartService,
        ICatalogService catalogService,
        IRecommendationClient recommendationClient,
        IAuthService authService)
    {
        _cartService = cartService;
        _catalogService = catalogService;
        _recommendationClient = recommendationClient;
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

        var cart = await _cartService.GetOrCreateActiveCartAsync(customerId.Value);

        var productIdsInCart = cart.Items.Select(i => i.ProductId).ToList();
        var rawCartCompletion = new List<RecommendationItemDto>();
        if (productIdsInCart.Any())
        {
            rawCartCompletion = await _recommendationClient.GetCartCompletionRecommendationsAsync(productIdsInCart, 4);
        }

        var cartCompletionRecs = new List<RecommendationItemDto>();
        foreach (var item in rawCartCompletion)
        {
            if (!productIdsInCart.Contains(item.ProductId))
            {
                var product = await _catalogService.GetProductByIdAsync(item.ProductId);
                if (product != null && product.IsActive && product.IsInStock)
                {
                    cartCompletionRecs.Add(item with { ProductDetails = product });
                }
            }
        }

        var viewModel = new CartIndexViewModel
        {
            Cart = cart,
            CartCompletionRecommendations = cartCompletionRecs
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(Guid productId, int quantity = 1)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            await _cartService.AddItemToCartAsync(customerId.Value, productId, quantity);
            TempData["SuccessMessage"] = "Ürün sepete eklendi.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(Guid cartItemId, int quantity)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        await _cartService.UpdateItemQuantityAsync(customerId.Value, cartItemId, quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid cartItemId)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        await _cartService.RemoveItemFromCartAsync(customerId.Value, cartItemId);
        TempData["SuccessMessage"] = "Ürün sepetten çıkarıldı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCart()
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        await _cartService.ClearCartAsync(customerId.Value);
        TempData["SuccessMessage"] = "Sepetiniz temizlendi.";
        return RedirectToAction(nameof(Index));
    }
}
