using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Controllers;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Tests;

public sealed class CartRecommendationPresentationTests
{
    [Fact]
    public async Task Cart_recommendations_are_batch_resolved_and_keep_service_order()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cartProductId = Guid.NewGuid();
        var firstRecommendationId = Guid.NewGuid();
        var secondRecommendationId = Guid.NewGuid();
        var inactiveProductId = Guid.NewGuid();
        var outOfStockProductId = Guid.NewGuid();
        var cart = CreateCart(customerId, cartProductId);
        var recommendationClient = new StubRecommendationClient(
            [
                Recommendation(secondRecommendationId, 0.9m),
                Recommendation(firstRecommendationId, 0.8m),
                Recommendation(secondRecommendationId, 0.7m),
                Recommendation(cartProductId, 0.6m),
                Recommendation(inactiveProductId, 0.5m),
                Recommendation(outOfStockProductId, 0.4m)
            ]);
        var catalog = new RecordingCatalogService(
            [
                Product(firstRecommendationId),
                Product(secondRecommendationId),
                Product(cartProductId),
                Product(inactiveProductId, isActive: false),
                Product(outOfStockProductId, isInStock: false)
            ]);
        var controller = new CartController(
            new StubCartService(cart),
            catalog,
            recommendationClient,
            new StubAuthService(userId, customerId),
            NullLogger<CartController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        "Test"))
                }
            }
        };

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CartIndexViewModel>(view.Model);
        Assert.Equal(
            [secondRecommendationId, firstRecommendationId],
            model.CartCompletionRecommendations.Select(item => item.ProductId));
        Assert.All(
            model.CartCompletionRecommendations,
            item => Assert.NotNull(item.ProductDetails));
        Assert.Equal(1, catalog.BatchLookupCount);
        Assert.Equal(0, catalog.SingleLookupCount);
        Assert.Equal(1, recommendationClient.CartCallCount);
    }

    private static CartDto CreateCart(Guid customerId, Guid productId)
    {
        return new CartDto(
            Guid.NewGuid(),
            customerId,
            CartStatus.Active,
            [
                new CartItemDto(
                    Guid.NewGuid(),
                    productId,
                    "Cart product",
                    "CART-1",
                    null,
                    10m,
                    20m,
                    1,
                    5,
                    10m,
                    2m,
                    12m)
            ],
            10m,
            2m,
            12m);
    }

    private static RecommendationItemDto Recommendation(
        Guid productId,
        decimal score)
    {
        return new RecommendationItemDto(
            productId,
            "Cart completion",
            score);
    }

    private static ProductDto Product(
        Guid productId,
        bool isActive = true,
        bool isInStock = true)
    {
        return new ProductDto(
            productId,
            $"SKU-{productId:N}"[..16],
            "Product",
            $"product-{productId:N}",
            null,
            Guid.NewGuid(),
            "Category",
            Guid.NewGuid(),
            "Brand",
            10m,
            20m,
            1m,
            UnitType.Piece,
            null,
            isActive,
            isInStock ? 1 : 0,
            isInStock);
    }

    private sealed class StubAuthService(Guid userId, Guid customerId)
        : IAuthService
    {
        public Task<Customer?> GetCustomerByUserIdAsync(Guid requestedUserId)
        {
            return Task.FromResult<Customer?>(requestedUserId == userId
                ? new Customer { Id = customerId, UserId = userId }
                : null);
        }

        public Task<AuthResultDto> RegisterAsync(
            string email,
            string password,
            string firstName,
            string lastName) => throw new NotSupportedException();

        public Task<AuthResultDto> LoginAsync(
            string email,
            string password,
            bool rememberMe) => throw new NotSupportedException();

        public Task LogoutAsync() => throw new NotSupportedException();

        public Task<Customer?> GetCustomerByEmailAsync(string email) =>
            throw new NotSupportedException();
    }

    private sealed class StubCartService(CartDto cart) : ICartService
    {
        public Task<CartDto> GetOrCreateActiveCartAsync(Guid customerId) =>
            Task.FromResult(cart);

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

    private sealed class StubRecommendationClient(
        List<RecommendationItemDto> recommendations)
        : IRecommendationClient
    {
        public int CartCallCount { get; private set; }

        public Task<List<RecommendationItemDto>>
            GetCartCompletionRecommendationsAsync(
                List<Guid> productIds,
                int count = 5)
        {
            CartCallCount++;
            return Task.FromResult(recommendations);
        }

        public Task<List<RecommendationItemDto>>
            GetPopularRecommendationsAsync(int count = 5) =>
            throw new NotSupportedException();

        public Task<List<RecommendationItemDto>>
            GetFrequentlyBoughtTogetherAsync(
                Guid productId,
                int count = 5,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<RecommendationItemDto>> GetSimilarProductsAsync(
            Guid productId,
            int count = 5) => throw new NotSupportedException();

        public Task<List<RecommendationItemDto>>
            GetPersonalizedRecommendationsAsync(
                Guid customerId,
                int count = 5) => throw new NotSupportedException();
    }

    private sealed class RecordingCatalogService(
        IReadOnlyList<ProductDto> products)
        : ICatalogService
    {
        private readonly Dictionary<Guid, ProductDto> productsById =
            products.ToDictionary(product => product.Id);

        public int BatchLookupCount { get; private set; }

        public int SingleLookupCount { get; private set; }

        public Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(
            IEnumerable<Guid> ids)
        {
            BatchLookupCount++;
            return Task.FromResult(ids
                .Distinct()
                .Where(productsById.ContainsKey)
                .ToDictionary(id => id, id => productsById[id]));
        }

        public Task<ProductDto?> GetProductByIdAsync(Guid id)
        {
            SingleLookupCount++;
            return Task.FromResult(productsById.GetValueOrDefault(id));
        }

        public Task<List<CategoryDto>> GetCategoriesAsync() =>
            throw new NotSupportedException();

        public Task<List<BrandDto>> GetBrandsAsync() =>
            throw new NotSupportedException();

        public Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter) =>
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
}
