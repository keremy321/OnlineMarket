using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Tests;

/// <summary>
/// Shared fakes for the assistant tests. Provider calls are always faked; no test
/// performs a real OpenAI or Recommendation.Api request.
/// </summary>
internal static class AiAssistantTestData
{
    public static ProductDto Product(
        Guid? id = null,
        string name = "Organik Elma",
        decimal price = 25.50m,
        bool isActive = true,
        bool isInStock = true,
        int stockQuantity = 100,
        string? imageUrl = "/uploads/products/organik-elma.webp",
        string categoryName = "Meyve",
        string brandName = "Doğal Tarım") =>
        new(
            Id: id ?? Guid.NewGuid(),
            Sku: "SKU-001",
            Name: name,
            Slug: "organik-elma",
            Description: "Taze elma",
            CategoryId: Guid.NewGuid(),
            CategoryName: categoryName,
            BrandId: Guid.NewGuid(),
            BrandName: brandName,
            Price: price,
            VatRate: 10.0m,
            NetContent: 1.0m,
            UnitType: UnitType.Piece,
            ImageUrl: imageUrl,
            IsActive: isActive,
            StockQuantity: stockQuantity,
            IsInStock: isInStock);

    public static RecommendationItemDto Recommendation(Guid productId, string reason = "Sık tercih ediliyor") =>
        new(productId, reason, 0.9m);
}

/// <summary>
/// Records which Recommendation.Api operation the orchestrator used, so tests can assert
/// intent-to-endpoint mapping and prove no operational route is reachable from chat.
/// </summary>
internal sealed class RecordingRecommendationClient : IRecommendationClient
{
    public List<string> Calls { get; } = new();

    public List<RecommendationItemDto> PopularResult { get; set; } = new();
    public List<RecommendationItemDto> PersonalizedResult { get; set; } = new();
    public List<RecommendationItemDto> SimilarResult { get; set; } = new();
    public List<RecommendationItemDto> FbtResult { get; set; } = new();
    public List<RecommendationItemDto> CartCompletionResult { get; set; } = new();

    public Guid? LastSimilarProductId { get; private set; }
    public Guid? LastFbtProductId { get; private set; }
    public CancellationToken LastFbtCancellationToken { get; private set; }
    public Guid? LastPersonalizedCustomerId { get; private set; }
    public List<Guid>? LastCartProductIds { get; private set; }

    public Exception? ThrowOnAnyCall { get; set; }

    public Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(int count = 5)
    {
        Calls.Add("Popular");
        Throw();
        return Task.FromResult(PopularResult);
    }

    public Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(
        Guid productId,
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("Fbt");
        LastFbtProductId = productId;
        LastFbtCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        Throw();
        return Task.FromResult(FbtResult);
    }

    public Task<List<RecommendationItemDto>> GetSimilarProductsAsync(Guid productId, int count = 5)
    {
        Calls.Add("Similar");
        LastSimilarProductId = productId;
        Throw();
        return Task.FromResult(SimilarResult);
    }

    public Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(Guid customerId, int count = 5)
    {
        Calls.Add("Personalized");
        LastPersonalizedCustomerId = customerId;
        Throw();
        return Task.FromResult(PersonalizedResult);
    }

    public Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(List<Guid> productIds, int count = 5)
    {
        Calls.Add("CartCompletion");
        LastCartProductIds = productIds;
        Throw();
        return Task.FromResult(CartCompletionResult);
    }

    private void Throw()
    {
        if (ThrowOnAnyCall is not null)
        {
            throw ThrowOnAnyCall;
        }
    }
}

internal sealed class StubCatalogService : ICatalogService
{
    public bool GetProductsCalled { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public List<ProductDto> ProductsToReturn { get; set; } = new();
    public Dictionary<Guid, ProductDto> ProductsById { get; } = new();

    public void Register(params ProductDto[] products)
    {
        foreach (var product in products)
        {
            ProductsById[product.Id] = product;
        }
    }

    public Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter)
    {
        GetProductsCalled = true;
        LastSearchQuery = filter.SearchQuery;
        return Task.FromResult(ProductsToReturn);
    }

    public Task<List<CategoryDto>> GetCategoriesAsync() => Task.FromResult(new List<CategoryDto>());

    public Task<List<BrandDto>> GetBrandsAsync() => Task.FromResult(new List<BrandDto>());

    public Task<ProductDto?> GetProductByIdAsync(Guid id) =>
        Task.FromResult(ProductsById.TryGetValue(id, out var product) ? product : null);

    public Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids)
    {
        var result = new Dictionary<Guid, ProductDto>();
        foreach (var id in ids)
        {
            if (ProductsById.TryGetValue(id, out var product))
            {
                result[id] = product;
            }
        }

        return Task.FromResult(result);
    }

    public Task<ProductDto?> GetProductBySlugAsync(string slug) => Task.FromResult<ProductDto?>(null);

    public Task<ProductDto> CreateProductAsync(ProductDto dto, int initialStock) => throw new NotImplementedException();

    public Task<ProductDto?> UpdateProductAsync(Guid id, ProductDto dto) => throw new NotImplementedException();

    public Task<bool> AdjustStockAsync(Guid productId, int quantityChange, string reason, Guid userId) =>
        Task.FromResult(true);
}

internal sealed class StubCartService : ICartService
{
    public List<CartItemDto> Items { get; set; } = new();
    public Guid? LastRequestedCustomerId { get; private set; }

    public Task<CartDto> GetOrCreateActiveCartAsync(Guid customerId)
    {
        LastRequestedCustomerId = customerId;
        return Task.FromResult(new CartDto(
            Guid.NewGuid(),
            customerId,
            CartStatus.Active,
            Items,
            0m,
            0m,
            0m));
    }

    public static CartItemDto CartItem(Guid productId, int quantity = 1) =>
        new(
            Guid.NewGuid(),
            productId,
            "Sepetteki Ürün",
            "SKU-CART",
            null,
            10m,
            10m,
            quantity,
            50,
            10m,
            1m,
            11m);

    public Task<CartDto> AddItemToCartAsync(Guid customerId, Guid productId, int quantity) => throw new NotImplementedException();

    public Task<CartDto> UpdateItemQuantityAsync(Guid customerId, Guid cartItemId, int quantity) => throw new NotImplementedException();

    public Task<CartDto> RemoveItemFromCartAsync(Guid customerId, Guid cartItemId) => throw new NotImplementedException();

    public Task<CartDto> ClearCartAsync(Guid customerId) => throw new NotImplementedException();
}

internal sealed class StubOrderService : IOrderService
{
    public List<OrderDto> Orders { get; set; } = new();

    public Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId) => Task.FromResult(Orders);

    public Task<OrderDto?> GetOrderByIdAsync(Guid orderId, Guid customerId) => Task.FromResult<OrderDto?>(null);

    public Task<List<OrderDto>> GetAllOrdersForAdminAsync() => Task.FromResult(new List<OrderDto>());
}

/// <summary>
/// Captures every prompt sent to the provider so tests can assert that no personal
/// identifier ever leaves the application.
/// </summary>
internal sealed class StubAiApiClient : IAiApiClient
{
    public bool GenerateCompletionCalled { get; private set; }
    public int CallCount { get; private set; }
    public List<AiApiCompletionRequest> Requests { get; } = new();
    public AiApiCompletionResponse? ResponseToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<AiApiCompletionResponse?> GenerateCompletionAsync(
        AiApiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        GenerateCompletionCalled = true;
        CallCount++;
        Requests.Add(request);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(ResponseToReturn);
    }

    public string AllPromptText =>
        string.Join(
            "\n",
            Requests.SelectMany(request => request.Messages).Select(message => message.Content));

    public static AiApiCompletionResponse Reply(string content) =>
        new(
            Id: "chatcmpl-test",
            Model: "gpt-5-nano",
            Choices: [new AiApiCompletionChoice(0, new AiChatMessage("assistant", content), "stop")]);
}

internal sealed class StubCustomerIdentityResolver : ICustomerIdentityResolver
{
    public Guid? CustomerIdToReturn { get; set; }

    public Task<Guid?> GetActiveCustomerIdByUserIdAsync(Guid userId) =>
        Task.FromResult(CustomerIdToReturn);
}
