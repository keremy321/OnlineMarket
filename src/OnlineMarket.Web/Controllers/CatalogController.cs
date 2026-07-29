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

    public CatalogController(
        ICatalogService catalogService,
        IRecommendationClient recommendationClient,
        IAuthService authService)
    {
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
    public async Task<IActionResult> Index(
        Guid? categoryId,
        Guid? brandId,
        string? searchQuery,
        decimal? minPrice,
        decimal? maxPrice,
        bool inStockOnly = false,
        string sortBy = "newest")
    {
        // 1. Trigger HTTP recommendation tasks (HttpClient is safe for async parallelism)
        var popularRecsTask = _recommendationClient.GetPopularRecommendationsAsync(4);

        var customerId = await GetCurrentCustomerIdAsync();
        var personalizedRecsTask = customerId.HasValue
            ? _recommendationClient.GetPersonalizedRecommendationsAsync(customerId.Value, 4)
            : Task.FromResult(new List<RecommendationItemDto>());

        // 2. Execute DB queries sequentially (EF Core DbContext is single-threaded per request)
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

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var product = await _catalogService.GetProductByIdAsync(id);
        if (product == null) return NotFound();

        var rawFbtTask = _recommendationClient.GetFrequentlyBoughtTogetherAsync(id, 4);
        var rawSimilarTask = _recommendationClient.GetSimilarProductsAsync(id, 4);

        var rawFbt = await rawFbtTask;
        var rawSimilar = await rawSimilarTask;

        var fbtRecs = await EnrichAndValidateRecommendationsAsync(rawFbt);
        var similarRecs = await EnrichAndValidateRecommendationsAsync(rawSimilar);

        var viewModel = new ProductDetailViewModel
        {
            Product = product,
            FrequentlyBoughtTogether = fbtRecs,
            SimilarProducts = similarRecs
        };

        return View(viewModel);
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
