using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Controllers;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Tests;

public class AiSupportServiceTests
{
    [Fact]
    public async Task ProcessCustomerQueryAsync_EmptyMessage_ReturnsDefaultWelcome()
    {
        var service = CreateService();
        var request = new AiSupportRequestDto(Message: "");

        var result = await service.ProcessCustomerQueryAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Reply);
        Assert.NotEmpty(result.ConversationId);
        Assert.NotEmpty(result.SuggestedActions!);
    }

    [Fact]
    public async Task ProcessCustomerQueryAsync_CargoQuery_ReturnsShippingPolicy()
    {
        var service = CreateService();
        var request = new AiSupportRequestDto(Message: "Kargo ücretleri nedir?");

        var result = await service.ProcessCustomerQueryAsync(request);

        Assert.True(result.Success);
        Assert.Contains("500 ₺", result.Reply);
        Assert.Contains("Ekspres Kargo", result.Reply);
    }

    [Fact]
    public async Task ProcessCustomerQueryAsync_OrderQueryNotLoggedIn_PromptsLogin()
    {
        var service = CreateService();
        var request = new AiSupportRequestDto(Message: "Sipariş durumum nerede?");

        var result = await service.ProcessCustomerQueryAsync(request, customerId: null);

        Assert.True(result.Success);
        Assert.Contains("Giriş Yapmış", result.Reply);
    }

    [Fact]
    public async Task ProcessCustomerQueryAsync_ProductSearchQuery_CallsCatalogService()
    {
        var catalogStub = new StubCatalogService
        {
            ProductsToReturn = new List<ProductDto>
            {
                new(
                    Guid.NewGuid(),
                    "SKU-001",
                    "Organik Elma",
                    "organik-elma",
                    "Taze elma",
                    Guid.NewGuid(),
                    "Meyve",
                    Guid.NewGuid(),
                    "Doğal Tarım",
                    25.50m,
                    10.0m,
                    1.0m,
                    UnitType.Piece,
                    null,
                    true,
                    100,
                    true)
            }
        };

        var service = CreateService(catalogService: catalogStub);
        var request = new AiSupportRequestDto(Message: "Elma var mı?");

        var result = await service.ProcessCustomerQueryAsync(request);

        Assert.True(result.Success);
        Assert.Contains("Organik Elma", result.Reply);
        Assert.True(catalogStub.GetProductsCalled);
    }

    [Fact]
    public async Task ProcessCustomerQueryAsync_ExternalApiEnabled_ReturnsApiResponse()
    {
        var stubApiClient = new StubAiApiClient
        {
            ResponseToReturn = new AiApiCompletionResponse(
                Id: "chatcmpl-123",
                Model: "gpt-4o-mini",
                Choices: new List<AiApiCompletionChoice>
                {
                    new(0, new AiChatMessage("assistant", "API tarafından üretilmiş özel yanıt."), "stop")
                })
        };

        var options = Options.Create(new AiAssistantOptions
        {
            Enabled = true,
            Provider = "OpenAI",
            EndpointUrl = "https://api.openai.com/v1/chat/completions",
            ApiKey = "test-key"
        });

        var service = CreateService(aiApiClient: stubApiClient, options: options);
        var request = new AiSupportRequestDto(Message: "Özel soru");

        var result = await service.ProcessCustomerQueryAsync(request);

        Assert.True(result.Success);
        Assert.Equal("API tarafından üretilmiş özel yanıt.", result.Reply);
        Assert.True(stubApiClient.GenerateCompletionCalled);
    }

    [Fact]
    public async Task ProcessCustomerQueryAsync_ExternalApiFails_FallsBackToRuleEngine()
    {
        var stubApiClient = new StubAiApiClient
        {
            ResponseToReturn = null // API failure simulated
        };

        var options = Options.Create(new AiAssistantOptions
        {
            Enabled = true,
            Provider = "OpenAI",
            EndpointUrl = "https://api.openai.com/v1/chat/completions",
            ApiKey = "test-key"
        });

        var service = CreateService(aiApiClient: stubApiClient, options: options);
        var request = new AiSupportRequestDto(Message: "Kargo ücretleri nedir?");

        var result = await service.ProcessCustomerQueryAsync(request);

        Assert.True(result.Success);
        Assert.Contains("500 ₺", result.Reply); // Fallback response from rule engine
        Assert.True(stubApiClient.GenerateCompletionCalled);
    }

    [Fact]
    public void AiSupportController_ChatAction_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(AiSupportController).GetMethod(nameof(AiSupportController.Chat));

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();
        Assert.NotNull(attribute);
    }

    [Fact]
    public async Task AiSupportController_ChatAction_NullRequest_ReturnsBadRequest()
    {
        var controller = new AiSupportController(CreateService(), new StubAuthService());

        var response = await controller.Chat(null!, CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(response);
        var dto = Assert.IsType<AiSupportResponseDto>(response!.Value);
        Assert.False(dto.Success);
    }

    private static AiSupportService CreateService(
        ICatalogService? catalogService = null,
        IOrderService? orderService = null,
        IRecommendationClient? recommendationClient = null,
        IAiApiClient? aiApiClient = null,
        IOptions<AiAssistantOptions>? options = null)
    {
        return new AiSupportService(
            catalogService ?? new StubCatalogService(),
            orderService ?? new StubOrderService(),
            recommendationClient ?? new StubRecommendationClient(),
            aiApiClient,
            options,
            NullLogger<AiSupportService>.Instance);
    }

    private sealed class StubAiApiClient : IAiApiClient
    {
        public bool GenerateCompletionCalled { get; private set; }
        public AiApiCompletionResponse? ResponseToReturn { get; set; }

        public Task<AiApiCompletionResponse?> GenerateCompletionAsync(
            AiApiCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateCompletionCalled = true;
            return Task.FromResult(ResponseToReturn);
        }
    }

    private sealed class StubCatalogService : ICatalogService
    {
        public bool GetProductsCalled { get; private set; }
        public List<ProductDto> ProductsToReturn { get; set; } = new();

        public Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter)
        {
            GetProductsCalled = true;
            return Task.FromResult(ProductsToReturn);
        }

        public Task<List<CategoryDto>> GetCategoriesAsync() => Task.FromResult(new List<CategoryDto>());
        public Task<List<BrandDto>> GetBrandsAsync() => Task.FromResult(new List<BrandDto>());
        public Task<ProductDto?> GetProductByIdAsync(Guid id) => Task.FromResult<ProductDto?>(null);
        public Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids) => Task.FromResult(new Dictionary<Guid, ProductDto>());
        public Task<ProductDto?> GetProductBySlugAsync(string slug) => Task.FromResult<ProductDto?>(null);
        public Task<ProductDto> CreateProductAsync(ProductDto dto, int initialStock) => throw new NotImplementedException();
        public Task<ProductDto?> UpdateProductAsync(Guid id, ProductDto dto) => throw new NotImplementedException();
        public Task<bool> AdjustStockAsync(Guid productId, int quantityChange, string reason, Guid userId) => Task.FromResult(true);
    }

    private sealed class StubOrderService : IOrderService
    {
        public Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId) => Task.FromResult(new List<OrderDto>());
        public Task<OrderDto?> GetOrderByIdAsync(Guid orderId, Guid customerId) => Task.FromResult<OrderDto?>(null);
        public Task<List<OrderDto>> GetAllOrdersForAdminAsync() => Task.FromResult(new List<OrderDto>());
    }

    private sealed class StubRecommendationClient : IRecommendationClient
    {
        public Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
        public Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(Guid productId, int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
        public Task<List<RecommendationItemDto>> GetSimilarProductsAsync(Guid productId, int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
        public Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(Guid customerId, int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
        public Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(List<Guid> productIds, int count = 5) => Task.FromResult(new List<RecommendationItemDto>());
    }

    private sealed class StubAuthService : IAuthService
    {
        public Task<AuthResultDto> RegisterAsync(string email, string password, string firstName, string lastName) => throw new NotImplementedException();
        public Task<AuthResultDto> LoginAsync(string email, string password, bool rememberMe) => throw new NotImplementedException();
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<Customer?> GetCustomerByUserIdAsync(Guid userId) => Task.FromResult<Customer?>(null);
        public Task<Customer?> GetCustomerByEmailAsync(string email) => Task.FromResult<Customer?>(null);
    }
}
