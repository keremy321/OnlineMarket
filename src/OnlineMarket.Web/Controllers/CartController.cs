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
    private readonly ILogger<CartController> _logger;

    public CartController(
        ICartService cartService,
        ICatalogService catalogService,
        IRecommendationClient recommendationClient,
        IAuthService authService,
        ILogger<CartController> logger)
    {
        _cartService = cartService;
        _catalogService = catalogService;
        _recommendationClient = recommendationClient;
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

            var productIdsInCart = cart.Items.Select(i => i.ProductId).ToList();
            var rawCartCompletion = new List<RecommendationItemDto>();
            if (productIdsInCart.Any())
            {
                try
                {
                    rawCartCompletion = await _recommendationClient.GetCartCompletionRecommendationsAsync(productIdsInCart, 4);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch cart completion recommendations.");
                }
            }

            var cartProductIdSet = productIdsInCart.ToHashSet();
            var products = await _catalogService.GetProductsByIdsAsync(
                rawCartCompletion.Select(item => item.ProductId));
            var recommendedProductIds = new HashSet<Guid>();
            var cartCompletionRecs = new List<RecommendationItemDto>();
            foreach (var item in rawCartCompletion)
            {
                if (!cartProductIdSet.Contains(item.ProductId)
                    && recommendedProductIds.Add(item.ProductId)
                    && products.TryGetValue(item.ProductId, out var product)
                    && product.IsActive
                    && product.IsInStock)
                {
                    cartCompletionRecs.Add(item with
                    {
                        ProductDetails = product
                    });
                }
            }

            var viewModel = new CartIndexViewModel
            {
                Cart = cart,
                CartCompletionRecommendations = cartCompletionRecs
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in CartController.Index.");
            TempData["ErrorMessage"] = "Sepet bilgileri yüklenirken bir sorun oluştu.";
            return View(new CartIndexViewModel());
        }
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
            _logger.LogError(ex, "Error adding product {ProductId} to cart for customer {CustomerId}.", productId, customerId);
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

        try
        {
            await _cartService.UpdateItemQuantityAsync(customerId.Value, cartItemId, quantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cart item {CartItemId} quantity for customer {CustomerId}.", cartItemId, customerId);
            TempData["ErrorMessage"] = "Ürün miktarı güncellenirken bir hata oluştu.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid cartItemId)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            await _cartService.RemoveItemFromCartAsync(customerId.Value, cartItemId);
            TempData["SuccessMessage"] = "Ürün sepetten çıkarıldı.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cart item {CartItemId} for customer {CustomerId}.", cartItemId, customerId);
            TempData["ErrorMessage"] = "Ürün sepetten çıkarılırken bir hata oluştu.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCart()
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            await _cartService.ClearCartAsync(customerId.Value);
            TempData["SuccessMessage"] = "Sepetiniz temizlendi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart for customer {CustomerId}.", customerId);
            TempData["ErrorMessage"] = "Sepet temizlenirken bir hata oluştu.";
        }
        return RedirectToAction(nameof(Index));
    }
}
