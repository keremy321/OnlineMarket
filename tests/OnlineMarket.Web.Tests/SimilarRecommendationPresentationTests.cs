using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Controllers;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Tests;

public sealed class SimilarRecommendationPresentationTests
{
    [Fact]
    public async Task Details_batch_resolves_filters_and_preserves_similar_order()
    {
        var sourceProductId = Guid.NewGuid();
        var firstRecommendationId = Guid.NewGuid();
        var secondRecommendationId = Guid.NewGuid();
        var thirdRecommendationId = Guid.NewGuid();
        var fourthRecommendationId = Guid.NewGuid();
        var fifthRecommendationId = Guid.NewGuid();
        var missingProductId = Guid.NewGuid();
        var inactiveProductId = Guid.NewGuid();
        var outOfStockProductId = Guid.NewGuid();
        var zeroQuantityProductId = Guid.NewGuid();
        var catalog = new RecordingCatalogService(
            [
                Product(sourceProductId),
                Product(firstRecommendationId),
                Product(secondRecommendationId),
                Product(thirdRecommendationId),
                Product(fourthRecommendationId),
                Product(fifthRecommendationId),
                Product(inactiveProductId, isActive: false),
                Product(outOfStockProductId, isInStock: false),
                Product(zeroQuantityProductId, stockQuantity: 0)
            ]);
        var recommendationClient = new StubRecommendationClient(
            [
                Recommendation(sourceProductId),
                Recommendation(secondRecommendationId),
                Recommendation(secondRecommendationId),
                Recommendation(missingProductId),
                Recommendation(inactiveProductId),
                Recommendation(outOfStockProductId),
                Recommendation(zeroQuantityProductId),
                Recommendation(firstRecommendationId),
                Recommendation(thirdRecommendationId),
                Recommendation(fourthRecommendationId),
                Recommendation(fifthRecommendationId)
            ]);
        var controller = CreateController(catalog, recommendationClient);

        var result = await controller.Details(sourceProductId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductDetailViewModel>(view.Model);
        Assert.Equal(
            [
                secondRecommendationId,
                firstRecommendationId,
                thirdRecommendationId,
                fourthRecommendationId
            ],
            model.SimilarProducts.Select(item => item.ProductId));
        Assert.All(
            model.SimilarProducts,
            item => Assert.NotNull(item.ProductDetails));
        Assert.Equal(1, catalog.BatchLookupCount);
        Assert.Equal(1, catalog.SingleLookupCount);
        Assert.DoesNotContain(sourceProductId, catalog.LastBatchProductIds);
        Assert.Equal(
            catalog.LastBatchProductIds.Distinct().Count(),
            catalog.LastBatchProductIds.Count);
        Assert.Equal(1, recommendationClient.SimilarCallCount);
        Assert.Equal(4, recommendationClient.RequestedLimit);
    }

    [Fact]
    public async Task Empty_similar_response_keeps_similar_section_model_empty()
    {
        var sourceProductId = Guid.NewGuid();
        var catalog = new RecordingCatalogService([Product(sourceProductId)]);
        var controller = CreateController(
            catalog,
            new StubRecommendationClient([]));

        var result = await controller.Details(sourceProductId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductDetailViewModel>(view.Model);
        Assert.Empty(model.SimilarProducts);
        Assert.Equal(0, catalog.BatchLookupCount);
    }

    [Fact]
    public async Task Details_still_returns_view_when_similar_client_throws()
    {
        var sourceProductId = Guid.NewGuid();
        var catalog = new RecordingCatalogService([Product(sourceProductId)]);
        var controller = CreateController(
            catalog,
            new StubRecommendationClient([], throwOnSimilar: true));

        var result = await controller.Details(sourceProductId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductDetailViewModel>(view.Model);
        Assert.Equal(sourceProductId, model.Product.Id);
        Assert.Empty(model.SimilarProducts);
        Assert.Equal(0, catalog.BatchLookupCount);
    }

    private static CatalogController CreateController(
        ICatalogService catalog,
        IRecommendationClient recommendationClient)
    {
        return new CatalogController(
            catalog,
            recommendationClient,
            new StubAuthService(),
            NullLogger<CatalogController>.Instance);
    }

    private static RecommendationItemDto Recommendation(Guid productId)
    {
        return new RecommendationItemDto(productId, "Similar product", 0.8m);
    }

    private static ProductDto Product(
        Guid productId,
        bool isActive = true,
        bool isInStock = true,
        int stockQuantity = 1)
    {
        return new ProductDto(
            productId,
            $"SKU-{productId:N}"[..16],
            $"Product {productId:N}",
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
            isInStock ? stockQuantity : 0,
            isInStock);
    }

    private sealed class StubRecommendationClient(
        List<RecommendationItemDto> recommendations,
        bool throwOnSimilar = false)
        : IRecommendationClient
    {
        public int SimilarCallCount { get; private set; }

        public int RequestedLimit { get; private set; }

        public Task<List<RecommendationItemDto>> GetSimilarProductsAsync(
            Guid productId,
            int count = 5)
        {
            SimilarCallCount++;
            RequestedLimit = count;
            if (throwOnSimilar)
            {
                throw new HttpRequestException("Injected Recommendation.Api failure.");
            }

            return Task.FromResult(recommendations);
        }

        public Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(
            Guid productId,
            int count = 5) => Task.FromResult(new List<RecommendationItemDto>());

        public Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(
            int count = 5) => throw new NotSupportedException();

        public Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(
            Guid customerId,
            int count = 5) => throw new NotSupportedException();

        public Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(
            List<Guid> productIds,
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

        public List<Guid> LastBatchProductIds { get; private set; } = [];

        public Task<ProductDto?> GetProductByIdAsync(Guid id)
        {
            SingleLookupCount++;
            return Task.FromResult(productsById.GetValueOrDefault(id));
        }

        public Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(
            IEnumerable<Guid> ids)
        {
            BatchLookupCount++;
            LastBatchProductIds = ids.ToList();
            return Task.FromResult(LastBatchProductIds
                .Where(productsById.ContainsKey)
                .ToDictionary(id => id, id => productsById[id]));
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

    private sealed class StubAuthService : IAuthService
    {
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

        public Task<Customer?> GetCustomerByUserIdAsync(Guid userId) =>
            throw new NotSupportedException();

        public Task<Customer?> GetCustomerByEmailAsync(string email) =>
            throw new NotSupportedException();
    }
}
