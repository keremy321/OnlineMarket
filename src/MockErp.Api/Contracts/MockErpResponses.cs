using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Contracts;

public sealed record EnsureCustomerResponse(
    Guid ErpCustomerId,
    string ErpCustomerCode,
    Guid ExternalCustomerId,
    bool Created);

public sealed record CreateOrderResponse(
    Guid ErpOrderId,
    string ErpOrderNumber,
    Guid ExternalOrderId);

public sealed record CreateStockMovementsResponse(
    Guid ErpOrderId,
    Guid ExternalOrderId,
    IReadOnlyList<StockMovementResponse> Movements);

public sealed record StockMovementResponse(
    Guid MovementId,
    Guid ExternalProductId,
    string Sku,
    int QuantityChange,
    int PreviousQuantity,
    int NewQuantity);

public sealed record CreateAccountingEntryResponse(
    Guid AccountingEntryId,
    string ErpVoucherNumber,
    Guid ExternalOrderId);

public sealed record CustomerOrderHistoryResponse(
    Guid ErpCustomerId,
    string ErpCustomerCode,
    Guid ExternalCustomerId,
    IReadOnlyList<CustomerOrderResponse> Orders);

public sealed record CustomerOrderResponse(
    Guid ErpOrderId,
    string ErpOrderNumber,
    Guid ExternalOrderId,
    string MarketOrderNumber,
    DateTime OrderPlacedAtUtc,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency,
    CustomerOrderAddressResponse Address,
    IReadOnlyList<CustomerOrderLineResponse> Lines,
    CustomerOrderAccountingResponse? Accounting);

public sealed record CustomerOrderAddressResponse(
    string RecipientName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode);

public sealed record CustomerOrderLineResponse(
    Guid ExternalProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetLineAmount,
    decimal VatAmount,
    decimal LineTotal);

public sealed record CustomerOrderAccountingResponse(
    Guid AccountingEntryId,
    string ErpVoucherNumber,
    AccountingVoucherType VoucherType,
    DateTime EntryDateUtc);

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    bool Retryable);

public sealed record ApiValidationErrorResponse(
    string Code,
    string Message,
    bool Retryable,
    IReadOnlyDictionary<string, string[]> Errors);
