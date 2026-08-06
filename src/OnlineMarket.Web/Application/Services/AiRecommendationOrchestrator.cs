using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Application.Services;

/// <summary>
/// Turns an approved intent into exactly one read-oriented Recommendation.Api call and
/// rebuilds the resulting cards from current OnlineMarketDb catalog data.
/// Recommendation.Api owns selection and ranking; this class never reorders or invents
/// products, and it never reaches Recommendation.ModelService or any operational route.
/// </summary>
public sealed class AiRecommendationOrchestrator : IAiRecommendationOrchestrator
{
    /// <summary>Same placeholder the storefront product cards use.</summary>
    public const string PlaceholderImageUrl = "https://placehold.co/200x200?text=Urun";

    private const string DetailsUrlFormat = "/Catalog/Details/{0:D}";
    private const int MinimumProductQueryLength = 3;

    /// <summary>
    /// Noise words removed before a named product lookup. They carry intent, not identity.
    /// </summary>
    private static readonly string[] ProductQueryNoiseWords =
    [
        "buna benzer", "bunu alanlar", "bu urunle", "bu urunun", "bu urune",
        "benzer urunler", "benzer urun", "alternatifi", "alternatif", "muadil",
        "urunler", "urunu", "urune", "urun", "goster", "gosterir", "misin",
        "onerir", "oneri", "oner", "tavsiye", "alanlar", "baska", "beraber",
        "birlikte", "alinir", "aliyor", "yaninda", "yanina", "var mi", "var",
        "mi", "ne", "bu", "bunu", "buna", "icin", "ile", "lutfen", "bana"
    ];

    private readonly IRecommendationClient _recommendationClient;
    private readonly ICatalogService _catalogService;
    private readonly ICartService _cartService;
    private readonly IOptions<AiAssistantOptions> _options;
    private readonly ILogger<AiRecommendationOrchestrator> _logger;

    public AiRecommendationOrchestrator(
        IRecommendationClient recommendationClient,
        ICatalogService catalogService,
        ICartService cartService,
        IOptions<AiAssistantOptions> options,
        ILogger<AiRecommendationOrchestrator> logger)
    {
        _recommendationClient = recommendationClient;
        _catalogService = catalogService;
        _cartService = cartService;
        _options = options;
        _logger = logger;
    }

    public async Task<AiRecommendationOutcome> ResolveAsync(
        AiAssistantIntent intent,
        AiAssistantContext context,
        CancellationToken cancellationToken = default)
    {
        if (!intent.IsRecommendationIntent())
        {
            return Empty(intent, AiRecommendationStatus.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var limit = _options.Value.EffectiveRecommendationCount;

        try
        {
            return intent switch
            {
                AiAssistantIntent.GeneralRecommendation =>
                    context.CustomerId.HasValue
                        ? await ResolvePersonalizedAsync(context.CustomerId.Value, limit)
                        : await ResolvePopularAsync(limit),
                AiAssistantIntent.Personalized =>
                    context.CustomerId.HasValue
                        ? await ResolvePersonalizedAsync(context.CustomerId.Value, limit)
                        : await ResolvePopularAsync(limit),
                AiAssistantIntent.Popular =>
                    await ResolvePopularAsync(limit),
                AiAssistantIntent.Similar =>
                    await ResolveProductScopedAsync(intent, context, limit, cancellationToken),
                AiAssistantIntent.FrequentlyBoughtTogether =>
                    await ResolveProductScopedAsync(intent, context, limit, cancellationToken),
                AiAssistantIntent.CartCompletion =>
                    await ResolveCartCompletionAsync(context, limit),
                _ => Empty(intent, AiRecommendationStatus.Empty)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never substitute locally chosen products for an unavailable engine.
            _logger.LogWarning(
                exception,
                "Recommendation.Api was unavailable for assistant intent {Intent}; returning an unavailable outcome.",
                intent);
            return Empty(intent, AiRecommendationStatus.Unavailable);
        }
    }

    private async Task<AiRecommendationOutcome> ResolvePersonalizedAsync(Guid customerId, int limit)
    {
        var recommendations = await _recommendationClient
            .GetPersonalizedRecommendationsAsync(customerId, limit);
        if (recommendations.Count == 0)
        {
            // The engine has no personal history yet; popular is its approved fallback.
            return await ResolvePopularAsync(limit);
        }

        return await BuildAsync(AiAssistantIntent.Personalized, recommendations, limit);
    }

    private async Task<AiRecommendationOutcome> ResolvePopularAsync(int limit)
    {
        var recommendations = await _recommendationClient
            .GetPopularRecommendationsAsync(limit);
        return await BuildAsync(AiAssistantIntent.Popular, recommendations, limit);
    }

    private async Task<AiRecommendationOutcome> ResolveProductScopedAsync(
        AiAssistantIntent intent,
        AiAssistantContext context,
        int limit,
        CancellationToken cancellationToken)
    {
        var sourceProduct = await ResolveSourceProductAsync(context);
        if (sourceProduct is null)
        {
            return Empty(intent, AiRecommendationStatus.NeedsProductClarification);
        }

        var recommendations = intent == AiAssistantIntent.Similar
            ? await _recommendationClient.GetSimilarProductsAsync(sourceProduct.Id, limit)
            : await _recommendationClient.GetFrequentlyBoughtTogetherAsync(
                sourceProduct.Id,
                limit,
                cancellationToken);

        return await BuildAsync(
            intent,
            recommendations,
            limit,
            excludedProductIds: [sourceProduct.Id],
            resolvedProductName: sourceProduct.Name);
    }

    private async Task<AiRecommendationOutcome> ResolveCartCompletionAsync(
        AiAssistantContext context,
        int limit)
    {
        if (!context.CustomerId.HasValue)
        {
            return Empty(AiAssistantIntent.CartCompletion, AiRecommendationStatus.NeedsAuthentication);
        }

        // The cart is always read server-side; browser-supplied cart contents are ignored.
        var cart = await _cartService.GetOrCreateActiveCartAsync(context.CustomerId.Value);
        var cartProductIds = cart.Items
            .Where(item => item.ProductId != Guid.Empty && item.Quantity > 0)
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();
        if (cartProductIds.Count == 0)
        {
            return Empty(AiAssistantIntent.CartCompletion, AiRecommendationStatus.EmptyCart);
        }

        var recommendations = await _recommendationClient
            .GetCartCompletionRecommendationsAsync(cartProductIds, limit);

        return await BuildAsync(
            AiAssistantIntent.CartCompletion,
            recommendations,
            limit,
            excludedProductIds: cartProductIds);
    }

    /// <summary>
    /// Resolves the product a Similar/FBT request is about: the validated current-page
    /// product when present, otherwise an unambiguously named catalog product.
    /// Returns null when the request is ambiguous so the caller can ask for clarification.
    /// </summary>
    private async Task<ProductDto?> ResolveSourceProductAsync(AiAssistantContext context)
    {
        if (context.CurrentProductId is { } productId && productId != Guid.Empty)
        {
            // Browser-supplied identifiers are only ever used to re-read the catalog.
            var currentProduct = await _catalogService.GetProductByIdAsync(productId);
            if (currentProduct is { IsActive: true })
            {
                return currentProduct;
            }
        }

        var query = ExtractProductQuery(context.Message);
        if (query.Length < MinimumProductQueryLength)
        {
            return null;
        }

        var matches = await _catalogService.GetProductsAsync(new ProductFilterDto(
            CategoryId: null,
            BrandId: null,
            SearchQuery: query,
            MinPrice: null,
            MaxPrice: null,
            InStockOnly: true,
            SortBy: "newest"));

        var activeMatches = matches.Where(product => product.IsActive).ToList();
        if (activeMatches.Count == 1)
        {
            return activeMatches[0];
        }

        var normalizedQuery = AiIntentRouter.Normalize(query);
        var exactMatches = activeMatches
            .Where(product => string.Equals(
                AiIntentRouter.Normalize(product.Name),
                normalizedQuery,
                StringComparison.Ordinal))
            .ToList();

        return exactMatches.Count == 1 ? exactMatches[0] : null;
    }

    internal static string ExtractProductQuery(string message)
    {
        var normalized = AiIntentRouter.Normalize(message ?? string.Empty);
        foreach (var noiseWord in ProductQueryNoiseWords)
        {
            normalized = normalized.Replace(noiseWord, " ", StringComparison.Ordinal);
        }

        var words = normalized.Split(
            [' ', '?', '!', '.', ',', ';', ':', '\n', '\r', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', words).Trim();
    }

    /// <summary>
    /// Rebuilds every recommendation from the current catalog snapshot and applies the
    /// storefront active/stock policy. Order is preserved exactly as Recommendation.Api
    /// returned it.
    /// </summary>
    private async Task<AiRecommendationOutcome> BuildAsync(
        AiAssistantIntent intent,
        IReadOnlyList<RecommendationItemDto> recommendations,
        int limit,
        IReadOnlyCollection<Guid>? excludedProductIds = null,
        string? resolvedProductName = null)
    {
        var excluded = excludedProductIds is null
            ? new HashSet<Guid>()
            : new HashSet<Guid>(excludedProductIds);

        var ordered = recommendations
            .Where(item => item.ProductId != Guid.Empty && !excluded.Contains(item.ProductId))
            .DistinctBy(item => item.ProductId)
            .ToList();
        if (ordered.Count == 0)
        {
            return Empty(intent, AiRecommendationStatus.Empty, resolvedProductName);
        }

        var productsById = await _catalogService.GetProductsByIdsAsync(
            ordered.Select(item => item.ProductId));

        var products = new List<AiRecommendedProductDto>(ordered.Count);
        foreach (var item in ordered)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            if (!product.IsActive
                || !product.IsInStock
                || product.StockQuantity <= 0
                || product.Price <= 0m)
            {
                continue;
            }

            products.Add(new AiRecommendedProductDto(
                Id: product.Id,
                Name: product.Name,
                Price: product.Price,
                ImageUrl: string.IsNullOrWhiteSpace(product.ImageUrl)
                    ? PlaceholderImageUrl
                    : product.ImageUrl,
                DetailsUrl: string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    DetailsUrlFormat,
                    product.Id),
                Reason: string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim(),
                CategoryName: product.CategoryName,
                BrandName: product.BrandName));

            if (products.Count >= limit)
            {
                break;
            }
        }

        return products.Count == 0
            ? Empty(intent, AiRecommendationStatus.Empty, resolvedProductName)
            : new AiRecommendationOutcome(
                intent,
                AiRecommendationStatus.Success,
                products,
                resolvedProductName);
    }

    private static AiRecommendationOutcome Empty(
        AiAssistantIntent intent,
        AiRecommendationStatus status,
        string? resolvedProductName = null) =>
        new(intent, status, [], resolvedProductName);
}
