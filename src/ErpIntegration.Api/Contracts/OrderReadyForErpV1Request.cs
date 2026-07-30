using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Contracts;

public sealed record OrderReadyForErpV1Request
{
    public Guid EventId { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public DateTime OrderPlacedAtUtc { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public ErpOrderCustomerV1Request? Customer { get; init; }

    public ErpOrderAddressV1Request? Address { get; init; }

    public ErpOrderTotalsV1Request? Totals { get; init; }

    public IReadOnlyList<ErpOrderItemV1Request?>? Items { get; init; }
}

public sealed record ErpOrderCustomerV1Request
{
    public Guid CustomerId { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Email { get; init; }
}

public sealed record ErpOrderAddressV1Request
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

public sealed record ErpOrderTotalsV1Request
{
    public decimal Subtotal { get; init; }

    public decimal VatTotal { get; init; }

    public decimal GrandTotal { get; init; }

    public string? Currency { get; init; }
}

public sealed record ErpOrderItemV1Request
{
    public Guid ProductId { get; init; }

    public string? Sku { get; init; }

    public string? ProductName { get; init; }

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal VatRate { get; init; }

    public decimal NetLineAmount { get; init; }

    public decimal VatAmount { get; init; }

    public decimal LineTotal { get; init; }
}
