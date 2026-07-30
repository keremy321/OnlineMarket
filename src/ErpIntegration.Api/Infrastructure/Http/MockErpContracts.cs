using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Infrastructure.Http;

internal sealed record MockErpEnsureCustomerRequest(
    Guid ExternalCustomerId,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode);

internal sealed record MockErpCreateOrderRequest(
    Guid ExternalOrderId,
    string MarketOrderNumber,
    string ErpCustomerCode,
    DateTime OrderPlacedAtUtc,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency,
    MockErpCreateOrderAddressRequest Address,
    IReadOnlyList<MockErpCreateOrderLineRequest> Lines);

internal sealed record MockErpCreateOrderAddressRequest(
    string RecipientName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode);

internal sealed record MockErpCreateOrderLineRequest(
    Guid ExternalProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetLineAmount,
    decimal VatAmount,
    decimal LineTotal);

internal sealed record MockErpCreateStockMovementsRequest(
    Guid ExternalOrderId,
    IReadOnlyList<MockErpCreateStockMovementLineRequest> Lines);

internal sealed record MockErpCreateStockMovementLineRequest(
    Guid ExternalProductId,
    int QuantityChange);

internal sealed record MockErpCreateAccountingEntryRequest(
    Guid ExternalOrderId,
    DateTime EntryDateUtc,
    string Description);

internal sealed record MockErpEnsureCustomerResponse(
    Guid ErpCustomerId,
    string ErpCustomerCode,
    Guid ExternalCustomerId,
    bool Created);

internal sealed record MockErpCreateOrderResponse(
    Guid ErpOrderId,
    string ErpOrderNumber,
    Guid ExternalOrderId);

internal sealed record MockErpCreateStockMovementsResponse(
    Guid ErpOrderId,
    Guid ExternalOrderId,
    IReadOnlyList<MockErpStockMovementResponse> Movements);

internal sealed record MockErpStockMovementResponse(
    Guid MovementId,
    Guid ExternalProductId,
    string Sku,
    int QuantityChange,
    int PreviousQuantity,
    int NewQuantity);

internal sealed record MockErpCreateAccountingEntryResponse(
    Guid AccountingEntryId,
    string ErpVoucherNumber,
    Guid ExternalOrderId);

internal sealed record MockErpErrorResponse(
    string Code,
    string Message,
    bool Retryable);
