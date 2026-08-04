using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

public class HomeController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly IRecommendationClient _recommendationClient;
    private readonly IAuthService _authService;
    private readonly ICartService _cartService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ICatalogService catalogService,
        IRecommendationClient recommendationClient,
        IAuthService authService,
        ICartService cartService,
        ILogger<HomeController> logger)
    {
        _catalogService = catalogService;
        _recommendationClient = recommendationClient;
        _authService = authService;
        _cartService = cartService;
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
            // 1. HTTP calls to recommendation client
            var popularRecsTask = _recommendationClient.GetPopularRecommendationsAsync(4);
            
            var customerId = await GetCurrentCustomerIdAsync();
            var personalizedRecsTask = customerId.HasValue
                ? _recommendationClient.GetPersonalizedRecommendationsAsync(customerId.Value, 4)
                : Task.FromResult(new List<RecommendationItemDto>());

            // Fetch cart completion if customer has active cart
            Task<List<RecommendationItemDto>> cartCompletionRecsTask = Task.FromResult(new List<RecommendationItemDto>());
            if (customerId.HasValue)
            {
                try
                {
                    var cart = await _cartService.GetOrCreateActiveCartAsync(customerId.Value);
                    var cartProductIds = cart.Items.Select(i => i.ProductId).ToList();
                    if (cartProductIds.Any())
                    {
                        cartCompletionRecsTask = _recommendationClient.GetCartCompletionRecommendationsAsync(cartProductIds, 4);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch cart for customer {CustomerId} on Home page.", customerId);
                }
            }

            // 2. Sequential DB calls
            var filter = new ProductFilterDto(
                CategoryId: null,
                BrandId: null,
                SearchQuery: null,
                MinPrice: null,
                MaxPrice: null,
                InStockOnly: true,
                SortBy: "newest");

            var allProducts = await _catalogService.GetProductsAsync(filter);
            var categories = await _catalogService.GetCategoriesAsync();

            // 3. Await HTTP recommendation tasks
            var rawPopular = await popularRecsTask;
            var rawPersonalized = await personalizedRecsTask;
            var rawCartCompletion = await cartCompletionRecsTask;

            // 4. Enrich recommendations
            var popularRecs = await EnrichAndValidateRecommendationsAsync(rawPopular);
            var personalizedRecs = await EnrichAndValidateRecommendationsAsync(rawPersonalized);
            var cartCompletionRecs = await EnrichAndValidateRecommendationsAsync(rawCartCompletion);

            // Also fetch FBT & Similar based on first featured product if available
            var fbtRecs = new List<RecommendationItemDto>();
            var similarRecs = new List<RecommendationItemDto>();

            var firstProduct = allProducts.FirstOrDefault();
            if (firstProduct != null)
            {
                var rawFbt = await _recommendationClient.GetFrequentlyBoughtTogetherAsync(firstProduct.Id, 4);
                var rawSimilar = await _recommendationClient.GetSimilarProductsAsync(firstProduct.Id, 4);
                fbtRecs = await EnrichAndValidateRecommendationsAsync(rawFbt);
                similarRecs = await EnrichAndValidateRecommendationsAsync(rawSimilar);
            }

            var viewModel = new HomeViewModel
            {
                Categories = categories,
                FeaturedProducts = allProducts.Take(8).ToList(),
                PopularRecommendations = popularRecs,
                PersonalizedRecommendations = personalizedRecs,
                FrequentlyBoughtTogether = fbtRecs,
                SimilarProducts = similarRecs,
                CartCompletionRecommendations = cartCompletionRecs
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while rendering Home page.");
            return View(new HomeViewModel());
        }
    }

    private async Task<List<RecommendationItemDto>> EnrichAndValidateRecommendationsAsync(List<RecommendationItemDto> recs)
    {
        if (!recs.Any()) return new List<RecommendationItemDto>();

        var productIds = recs.Select(r => r.ProductId).Distinct();
        var productsDict = await _catalogService.GetProductsByIdsAsync(productIds);

        var enriched = new List<RecommendationItemDto>();
        foreach (var item in recs)
        {
            if (productsDict.TryGetValue(item.ProductId, out var product) && product.IsActive && product.IsInStock)
            {
                enriched.Add(item with { ProductDetails = product });
            }
        }
        return enriched;
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
