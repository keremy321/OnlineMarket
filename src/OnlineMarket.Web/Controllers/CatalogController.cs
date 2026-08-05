using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

public class CatalogController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly IRecommendationClient _recommendationClient;
    private readonly IAuthService _authService;
    private readonly ILogger<CatalogController> _logger;

    public CatalogController(
        ICatalogService catalogService,
        IRecommendationClient recommendationClient,
        IAuthService authService,
        ILogger<CatalogController> logger)
    {
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
    public async Task<IActionResult> Index(
        Guid? categoryId,
        Guid? brandId,
        string? searchQuery,
        decimal? minPrice,
        decimal? maxPrice,
        bool inStockOnly = false,
        string sortBy = "newest")
    {
        try
        {
            // 1. Trigger HTTP recommendation tasks
            var popularRecsTask = _recommendationClient.GetPopularRecommendationsAsync(4);

            var customerId = await GetCurrentCustomerIdAsync();
            var personalizedRecsTask = customerId.HasValue
                ? _recommendationClient.GetPersonalizedRecommendationsAsync(customerId.Value, 4)
                : Task.FromResult(new List<RecommendationItemDto>());

            // 2. Execute DB queries sequentially
            var filter = new ProductFilterDto(categoryId, brandId, searchQuery, minPrice, maxPrice, inStockOnly, sortBy);
            var products = await _catalogService.GetProductsAsync(filter);
            var categories = await _catalogService.GetCategoriesAsync();
            var brands = await _catalogService.GetBrandsAsync();

            // 3. Await HTTP tasks
            var rawPopular = await popularRecsTask;
            var rawPersonalized = await personalizedRecsTask;

            // 4. Enrich recommendations sequentially against DB
            var popularRecs = await EnrichAndValidateRecommendationsAsync(rawPopular);
            var personalizedRecs = await EnrichAndValidateRecommendationsAsync(rawPersonalized);

            var viewModel = new CatalogIndexViewModel
            {
                Products = products,
                Categories = categories,
                Brands = brands,
                PopularRecommendations = popularRecs,
                PersonalizedRecommendations = personalizedRecs,
                SelectedCategoryId = categoryId,
                SelectedBrandId = brandId,
                SearchQuery = searchQuery,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                InStockOnly = inStockOnly,
                SortBy = sortBy
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in CatalogController.Index.");
            return View(new CatalogIndexViewModel());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        try
        {
            var product = await _catalogService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            var rawFbtTask = _recommendationClient.GetFrequentlyBoughtTogetherAsync(id, 4);
            var rawSimilarTask = GetSimilarRecommendationsAsync(id);

            var rawFbt = await rawFbtTask;
            var rawSimilar = await rawSimilarTask;

            var fbtRecs = await EnrichAndValidateRecommendationsAsync(rawFbt);
            var similarRecs = await ResolveSimilarRecommendationsAsync(
                id,
                rawSimilar);

            var viewModel = new ProductDetailViewModel
            {
                Product = product,
                FrequentlyBoughtTogether = fbtRecs,
                SimilarProducts = similarRecs
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in CatalogController.Details for ProductId {Id}.", id);
            return NotFound();
        }
    }

    private async Task<List<RecommendationItemDto>> GetSimilarRecommendationsAsync(
        Guid productId)
    {
        try
        {
            return await _recommendationClient.GetSimilarProductsAsync(
                productId,
                4);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Similar recommendations were unavailable for ProductId {ProductId}; the section will be hidden.",
                productId);
            return [];
        }
    }

    private async Task<List<RecommendationItemDto>> ResolveSimilarRecommendationsAsync(
        Guid sourceProductId,
        List<RecommendationItemDto> recommendations)
    {
        if (recommendations.Count == 0)
        {
            return [];
        }

        try
        {
            var seenProductIds = new HashSet<Guid> { sourceProductId };
            var orderedRecommendations = recommendations
                .Where(item => item.ProductId != Guid.Empty)
                .Where(item => seenProductIds.Add(item.ProductId))
                .ToList();
            if (orderedRecommendations.Count == 0)
            {
                return [];
            }

            var products = await _catalogService.GetProductsByIdsAsync(
                orderedRecommendations.Select(item => item.ProductId));

            return orderedRecommendations
                .Where(item => products.TryGetValue(item.ProductId, out var product)
                    && product.IsActive
                    && product.IsInStock
                    && product.StockQuantity > 0)
                .Select(item => item with
                {
                    ProductDetails = products[item.ProductId]
                })
                .Take(4)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Similar recommendation products could not be resolved for ProductId {ProductId}; the section will be hidden.",
                sourceProductId);
            return [];
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
}
