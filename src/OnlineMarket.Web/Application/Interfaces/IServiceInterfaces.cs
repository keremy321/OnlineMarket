using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(string email, string password, string firstName, string lastName);
    Task<AuthResultDto> LoginAsync(string email, string password, bool rememberMe);
    Task LogoutAsync();
    Task<Customer?> GetCustomerByUserIdAsync(Guid userId);
    Task<Customer?> GetCustomerByEmailAsync(string email);
}

public interface ICustomerAddressService
{
    Task<List<CustomerAddressDto>> GetCustomerAddressesAsync(Guid customerId);
    Task<CustomerAddressDto?> GetAddressByIdAsync(Guid addressId, Guid customerId);
    Task<CustomerAddressDto> AddAddressAsync(Guid customerId, CreateAddressDto dto);
    Task<bool> UpdateAddressAsync(Guid addressId, Guid customerId, CreateAddressDto dto);
    Task<bool> DeleteAddressAsync(Guid addressId, Guid customerId);
    Task<bool> SetDefaultAddressAsync(Guid addressId, Guid customerId);
}

public interface ICatalogService
{
    Task<List<CategoryDto>> GetCategoriesAsync();
    Task<List<BrandDto>> GetBrandsAsync();
    Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter);
    Task<ProductDto?> GetProductByIdAsync(Guid id);
    Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids);
    Task<ProductDto?> GetProductBySlugAsync(string slug);
    Task<ProductDto> CreateProductAsync(ProductDto dto, int initialStock);
    Task<ProductDto?> UpdateProductAsync(Guid id, ProductDto dto);
    Task<bool> AdjustStockAsync(Guid productId, int quantityChange, string reason, Guid userId);
}

public interface ICartService
{
    Task<CartDto> GetOrCreateActiveCartAsync(Guid customerId);
    Task<CartDto> AddItemToCartAsync(Guid customerId, Guid productId, int quantity);
    Task<CartDto> UpdateItemQuantityAsync(Guid customerId, Guid cartItemId, int quantity);
    Task<CartDto> RemoveItemFromCartAsync(Guid customerId, Guid cartItemId);
    Task<CartDto> ClearCartAsync(Guid customerId);
}

public interface ICheckoutService
{
    Task<CheckoutResultDto> ExecuteCheckoutAsync(Guid customerId, CheckoutRequestDto request);
}

public interface IOrderService
{
    Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId);
    Task<OrderDto?> GetOrderByIdAsync(Guid orderId, Guid customerId);
    Task<List<OrderDto>> GetAllOrdersForAdminAsync();
}

public interface IAdminQueryService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<List<AdminOutboxMessageDto>> GetRecentOutboxMessagesAsync(
        int count,
        CancellationToken cancellationToken = default);
}

public interface IRecommendationClient
{
    Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(int count = 5);
    Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(Guid productId, int count = 5);
    Task<List<RecommendationItemDto>> GetSimilarProductsAsync(Guid productId, int count = 5);
    Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(Guid customerId, int count = 5);
    Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(List<Guid> productIds, int count = 5);
}

public interface IErpIntegrationClient
{
    Task<ErpOrderTransferStatusDto?> GetErpOrderTransferStatusAsync(Guid orderId);
}

public interface IOutboxService
{
    Task ProcessPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default);
}

public interface IStockMutationService
{
    Task<StockMutationResultDto?> TryDecreaseAsync(
        Guid productId,
        int requestedQuantity,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<StockMutationResultDto?> TryAdjustAsync(
        Guid productId,
        int quantityChange,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IOrderNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}

public interface IOutboxStore
{
    Task<IReadOnlyList<ClaimedOutboxMessageDto>> ClaimAsync(
        int batchSize,
        string workerId,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken = default);

    Task RecordDeliveryResultAsync(
        OutboxDeliveryResultDto result,
        CancellationToken cancellationToken = default);
}

public interface IOutboxDispatcher
{
    Task<OutboxDispatchResultDto> DispatchAsync(
        ClaimedOutboxMessageDto message,
        CancellationToken cancellationToken = default);
}
