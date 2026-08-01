using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Application.Models;

public record AuthResultDto(bool Succeeded, string? ErrorMessage = null, Guid? UserId = null, Guid? CustomerId = null);

public record CustomerAddressDto(
    Guid Id,
    Guid CustomerId,
    string Title,
    string ContactName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode,
    bool IsDefault,
    bool IsActive
);

public record CreateAddressDto(
    string Title,
    string ContactName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    bool IsDefault
);

public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    int DisplayOrder,
    Guid? ParentCategoryId,
    List<CategoryDto>? SubCategories = null
);

public record BrandDto(
    Guid Id,
    string Name,
    string Slug
);

public record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    Guid BrandId,
    string BrandName,
    decimal Price,
    decimal VatRate,
    decimal NetContent,
    UnitType UnitType,
    string? ImageUrl,
    bool IsActive,
    int StockQuantity,
    bool IsInStock
);

public record ProductFilterDto(
    Guid? CategoryId,
    Guid? BrandId,
    string? SearchQuery,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStockOnly,
    string? SortBy
);

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    string? ImageUrl,
    decimal UnitPrice,
    decimal VatRate,
    int Quantity,
    int AvailableStock,
    decimal LineSubtotal,
    decimal LineVat,
    decimal LineTotal
);

public record CartDto(
    Guid Id,
    Guid CustomerId,
    CartStatus Status,
    List<CartItemDto> Items,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal
);

public record CheckoutRequestDto(
    Guid AddressId,
    string CardHolderName,
    string CardNumberMasked,
    bool SimulateSuccess = true
);

public record CheckoutResultDto(
    bool Success,
    Guid? OrderId,
    string? OrderNumber,
    string? ErrorMessage
);

public record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductSkuSnapshot,
    string ProductNameSnapshot,
    decimal UnitPriceSnapshot,
    decimal VatRateSnapshot,
    int Quantity,
    decimal LineSubtotal,
    decimal LineVat,
    decimal LineTotal
);

public record OrderAddressDto(
    string Title,
    string ContactName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string CountryCode
);

public record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    Guid SourceCartId,
    OrderStatus Status,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency,
    Guid CorrelationId,
    DateTime PlacedAtUtc,
    OrderAddressDto? Address,
    List<OrderItemDto> Items,
    ErpOrderTransferStatusDto? ErpTransferStatus
);

public record ErpOrderTransferStatusDto(
    Guid OrderId,
    string Status, // e.g. "Completed", "Processing", "Failed", "NotStarted"
    string? CurrentStep,
    DateTime? LastAttemptAtUtc,
    string? ErrorMessage
);

public record RecommendationItemDto(
    Guid ProductId,
    string Reason,
    decimal Score,
    ProductDto? ProductDetails
);

public sealed record AdminDashboardDto(
    int TotalProducts,
    int OutOfStockProducts,
    int TotalOrders,
    int PendingOutboxMessages,
    List<OrderDto> RecentOrders);

public sealed record AdminOutboxMessageDto(
    long Id,
    string EventType,
    string Destination,
    Guid AggregateId,
    OutboxStatus Status,
    int AttemptCount,
    DateTime OccurredAtUtc,
    DateTime? ProcessedAtUtc,
    string? LastErrorCode,
    string? MaskedLastError);

public readonly record struct StockMutationResultDto(
    int PreviousQuantity,
    int NewQuantity);

public sealed record ClaimedOutboxMessageDto(
    long Id,
    string EventType,
    string Destination,
    string Payload);

public sealed record OutboxDeliveryResultDto(
    long MessageId,
    string WorkerId,
    bool Succeeded,
    bool Retryable,
    string? ErrorCode,
    string? MaskedError,
    DateTime CompletedAtUtc);

public sealed record OutboxDispatchResultDto(
    bool Succeeded,
    bool Retryable,
    string? ErrorCode,
    string? MaskedError);
