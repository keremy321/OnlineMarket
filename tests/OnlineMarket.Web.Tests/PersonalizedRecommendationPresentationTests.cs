using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;
using OnlineMarket.Web.Controllers;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Tests;

public sealed class PersonalizedRecommendationPresentationTests
{
    [Fact]
    public async Task Anonymous_home_does_not_resolve_or_request_personalized_recommendations()
    {
        var customerResolver = new StubCustomerIdentityResolver();
        var recommendations = new StubRecommendationClient([]);
        var controller = CreateController(
            new RecordingCatalogService([]),
            recommendations,
            customerResolver,
            authenticatedUserId: null);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeViewModel>(view.Model);
        Assert.Empty(model.PersonalizedRecommendations);
        Assert.Equal(0, customerResolver.CustomerLookupCount);
        Assert.Equal(0, recommendations.PersonalizedCallCount);
    }

    [Fact]
    public async Task Authenticated_home_batches_filters_preserves_order_and_caps_results()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var validProductIds = Enumerable.Range(0, 10)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var missingProductId = Guid.NewGuid();
        var inactiveProductId = Guid.NewGuid();
        var outOfStockProductId = Guid.NewGuid();
        var zeroQuantityProductId = Guid.NewGuid();
        var catalog = new RecordingCatalogService(
            validProductIds
                .Select((id, index) => Product(id, price: 100m + index))
                .Append(Product(inactiveProductId, isActive: false))
                .Append(Product(outOfStockProductId, isInStock: false))
                .Append(Product(zeroQuantityProductId, stockQuantity: 0))
                .ToArray());
        var recommendations = new StubRecommendationClient(
            [
                Recommendation(validProductIds[1]),
                Recommendation(validProductIds[1]),
                Recommendation(missingProductId),
                Recommendation(inactiveProductId),
                Recommendation(outOfStockProductId),
                Recommendation(zeroQuantityProductId),
                Recommendation(validProductIds[0]),
                .. validProductIds.Skip(2).Select(Recommendation)
            ]);
        var customerResolver = new StubCustomerIdentityResolver(userId, customerId);
        var controller = CreateController(
            catalog,
            recommendations,
            customerResolver,
            userId);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeViewModel>(view.Model);
        Assert.Equal(
            new[] { validProductIds[1], validProductIds[0] }
                .Concat(validProductIds.Skip(2).Take(6)),
            model.PersonalizedRecommendations.Select(item => item.ProductId));
        Assert.All(
            model.PersonalizedRecommendations,
            item =>
            {
                Assert.NotNull(item.ProductDetails);
                Assert.True(item.ProductDetails.IsActive);
                Assert.True(item.ProductDetails.IsInStock);
                Assert.True(item.ProductDetails.StockQuantity > 0);
            });
        Assert.Equal(101m, model.PersonalizedRecommendations[0].ProductDetails?.Price);
        Assert.Equal(1, catalog.BatchLookupCount);
        Assert.Equal(
            catalog.LastBatchProductIds.Distinct().Count(),
            catalog.LastBatchProductIds.Count);
        Assert.Equal(1, customerResolver.CustomerLookupCount);
        Assert.Equal(userId, customerResolver.RequestedUserId);
        Assert.Equal(1, recommendations.PersonalizedCallCount);
        Assert.Equal(customerId, recommendations.RequestedCustomerId);
        Assert.Equal(RecommendationUiOptions.MaximumPersonalizedDisplayLimit, recommendations.RequestedLimit);
    }

    [Fact]
    public async Task Unknown_authenticated_customer_does_not_request_personalized_recommendations()
    {
        var userId = Guid.NewGuid();
        var recommendations = new StubRecommendationClient([]);
        var controller = CreateController(
            new RecordingCatalogService([]),
            recommendations,
            new StubCustomerIdentityResolver(),
            userId);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeViewModel>(view.Model);
        Assert.Empty(model.PersonalizedRecommendations);
        Assert.Equal(0, recommendations.PersonalizedCallCount);
    }

    [Fact]
    public async Task Personalized_failure_keeps_other_home_content_available()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var featuredProduct = Product(Guid.NewGuid());
        var catalog = new RecordingCatalogService(
            [featuredProduct],
            featuredProducts: [featuredProduct],
            categories: [new CategoryDto(Guid.NewGuid(), "Gıda", "gida", 1, null)]);
        var controller = CreateController(
            catalog,
            new StubRecommendationClient([], throwOnPersonalized: true),
            new StubCustomerIdentityResolver(userId, customerId),
            userId);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeViewModel>(view.Model);
        Assert.Empty(model.PersonalizedRecommendations);
        Assert.Equal(featuredProduct.Id, Assert.Single(model.FeaturedProducts).Id);
        Assert.Equal("Gıda", Assert.Single(model.Categories).Name);
        Assert.Equal(0, catalog.BatchLookupCount);
    }

    [Fact]
    public void Home_personalized_section_uses_product_partial_without_technical_metadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "OnlineMarket.Web",
            "Views",
            "Home",
            "Index.cshtml"));

        Assert.Contains(
            "<h2 class=\"fw-extrabold mb-1 text-dark\">Sana Özel Öneriler</h2>",
            view,
            StringComparison.Ordinal);
        Assert.True(
            view.Split("<partial name=\"_ProductCard\"", StringSplitOptions.None).Length >= 3);
        Assert.DoesNotContain("SubjectId", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerId", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalizedMetrics", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ModelVersion", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RankingSource", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HybridPersonalized", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_route_has_no_customer_selector_input()
    {
        var action = typeof(HomeController).GetMethod(nameof(HomeController.Index));

        Assert.NotNull(action);
        Assert.Empty(action.GetParameters());
    }

    private static HomeController CreateController(
        ICatalogService catalog,
        StubRecommendationClient recommendations,
        StubCustomerIdentityResolver customerResolver,
        Guid? authenticatedUserId)
    {
        var identity = authenticatedUserId.HasValue
            ? new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, authenticatedUserId.Value.ToString())],
                "Test")
            : new ClaimsIdentity();
        return new HomeController(
            catalog,
            recommendations,
            customerResolver,
            new EmptyCartService(),
            Options.Create(new RecommendationUiOptions()),
            NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static RecommendationItemDto Recommendation(Guid productId) =>
        new(
            productId,
            "Selected for you",
            0.8m,
            RecommendationType: "Personalized",
            ReasonCode: "Personalized.PreferenceFallback",
            PersonalizedMetrics: new PersonalizedRecommendationMetricsDto(
                0.8m,
                "PreferenceFallback",
                null,
                null,
                null,
                null,
                0.8m,
                0.8m));

    private static ProductDto Product(
        Guid productId,
        decimal price = 10m,
        bool isActive = true,
        bool isInStock = true,
        int stockQuantity = 1) =>
        new(
            productId,
            $"SKU-{productId:N}"[..16],
            $"Product {productId:N}",
            $"product-{productId:N}",
            null,
            Guid.NewGuid(),
            "Category",
            Guid.NewGuid(),
            "Brand",
            price,
            20m,
            1m,
            UnitType.Piece,
            null,
            isActive,
            isInStock ? stockQuantity : 0,
            isInStock);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OnlineMarket.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class StubCustomerIdentityResolver(
        Guid? expectedUserId = null,
        Guid? customerId = null) : ICustomerIdentityResolver
    {
        public int CustomerLookupCount { get; private set; }

        public Guid? RequestedUserId { get; private set; }

        public Task<Guid?> GetActiveCustomerIdByUserIdAsync(Guid userId)
        {
            CustomerLookupCount++;
            RequestedUserId = userId;
            return Task.FromResult(
                expectedUserId == userId && customerId.HasValue
                    ? customerId
                    : null);
        }
    }

    private sealed class StubRecommendationClient(
        List<RecommendationItemDto> personalized,
        bool throwOnPersonalized = false) : IRecommendationClient
    {
        public int PersonalizedCallCount { get; private set; }

        public Guid? RequestedCustomerId { get; private set; }

        public int? RequestedLimit { get; private set; }

        public Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(
            Guid customerId,
            int count = 5)
        {
            PersonalizedCallCount++;
            RequestedCustomerId = customerId;
            RequestedLimit = count;
            if (throwOnPersonalized)
            {
                throw new HttpRequestException("Injected Recommendation.Api failure.");
            }

            return Task.FromResult(personalized);
        }

        public Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(
            int count = 5) => Task.FromResult(new List<RecommendationItemDto>());

        public Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(
            Guid productId,
            int count = 5) => Task.FromResult(new List<RecommendationItemDto>());

        public Task<List<RecommendationItemDto>> GetSimilarProductsAsync(
            Guid productId,
            int count = 5) => Task.FromResult(new List<RecommendationItemDto>());

        public Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(
            List<Guid> productIds,
            int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
    }

    private sealed class RecordingCatalogService(
        IReadOnlyList<ProductDto> products,
        IReadOnlyList<ProductDto>? featuredProducts = null,
        IReadOnlyList<CategoryDto>? categories = null) : ICatalogService
    {
        private readonly Dictionary<Guid, ProductDto> productsById =
            products.ToDictionary(product => product.Id);

        public int BatchLookupCount { get; private set; }

        public List<Guid> LastBatchProductIds { get; private set; } = [];

        public Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(
            IEnumerable<Guid> ids)
        {
            BatchLookupCount++;
            LastBatchProductIds = ids.ToList();
            return Task.FromResult(LastBatchProductIds
                .Where(productsById.ContainsKey)
                .ToDictionary(id => id, id => productsById[id]));
        }

        public Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter) =>
            Task.FromResult((featuredProducts ?? []).ToList());

        public Task<List<CategoryDto>> GetCategoriesAsync() =>
            Task.FromResult((categories ?? []).ToList());

        public Task<List<BrandDto>> GetBrandsAsync() =>
            throw new NotSupportedException();

        public Task<ProductDto?> GetProductByIdAsync(Guid id) =>
            throw new NotSupportedException();

        public Task<ProductDto?> GetProductBySlugAsync(string slug) =>
            throw new NotSupportedException();

        public Task<ProductDto> CreateProductAsync(
            ProductDto dto,
            int initialStock) => throw new NotSupportedException();

        public Task<ProductDto?> UpdateProductAsync(
            Guid id,
            ProductDto dto) => throw new NotSupportedException();

        public Task<bool> AdjustStockAsync(
            Guid productId,
            int quantityChange,
            string reason,
            Guid userId) => throw new NotSupportedException();
    }

    private sealed class EmptyCartService : ICartService
    {
        public Task<CartDto> GetOrCreateActiveCartAsync(Guid customerId) =>
            Task.FromResult(new CartDto(
                Guid.NewGuid(),
                customerId,
                CartStatus.Active,
                [],
                0m,
                0m,
                0m));

        public Task<CartDto> AddItemToCartAsync(
            Guid customerId,
            Guid productId,
            int quantity) => throw new NotSupportedException();

        public Task<CartDto> UpdateItemQuantityAsync(
            Guid customerId,
            Guid cartItemId,
            int quantity) => throw new NotSupportedException();

        public Task<CartDto> RemoveItemFromCartAsync(
            Guid customerId,
            Guid cartItemId) => throw new NotSupportedException();

        public Task<CartDto> ClearCartAsync(Guid customerId) =>
            throw new NotSupportedException();
    }
}
