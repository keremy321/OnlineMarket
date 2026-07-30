using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Contracts;

public sealed record EnsureCustomerRequest
{
    public Guid ExternalCustomerId { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Email { get; init; }

    public string? PhoneNumber { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? District { get; init; }

    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string? CountryCode { get; init; }
}

public sealed record CreateOrderRequest
{
    public Guid ExternalOrderId { get; init; }

    public string? MarketOrderNumber { get; init; }

    public string? ErpCustomerCode { get; init; }

    public DateTime OrderPlacedAtUtc { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public decimal Subtotal { get; init; }

    public decimal VatTotal { get; init; }

    public decimal GrandTotal { get; init; }

    public string? Currency { get; init; }

    public CreateOrderAddressRequest? Address { get; init; }

    public IReadOnlyList<CreateOrderLineRequest?>? Lines { get; init; }
}

public sealed record CreateOrderAddressRequest
{
    public string? RecipientName { get; init; }

    public string? PhoneNumber { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? District { get; init; }

    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string? CountryCode { get; init; }
}

public sealed record CreateOrderLineRequest
{
    public Guid ExternalProductId { get; init; }

    public string? Sku { get; init; }

    public string? ProductName { get; init; }

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal VatRate { get; init; }

    public decimal NetLineAmount { get; init; }

    public decimal VatAmount { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed record CreateStockMovementsRequest
{
    public Guid ExternalOrderId { get; init; }

    public IReadOnlyList<CreateStockMovementLineRequest?>? Lines { get; init; }
}

public sealed record CreateStockMovementLineRequest
{
    public Guid ExternalProductId { get; init; }

    public int QuantityChange { get; init; }
}

public sealed record CreateAccountingEntryRequest
{
    public Guid ExternalOrderId { get; init; }

    public DateTime EntryDateUtc { get; init; }

    public string? Description { get; init; }
}
