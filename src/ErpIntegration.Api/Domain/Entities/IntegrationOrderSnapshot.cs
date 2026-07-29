using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Domain.Entities;

public sealed class IntegrationOrderSnapshot
{
    public Guid BatchId { get; set; }

    public Guid CustomerId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string District { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string? PostalCode { get; set; }

    public string CountryCode { get; set; } = string.Empty;

    public DateTime OrderPlacedAtUtc { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public decimal Subtotal { get; set; }

    public decimal VatTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public string Currency { get; set; } = string.Empty;

    public IntegrationBatch Batch { get; set; } = null!;
}
