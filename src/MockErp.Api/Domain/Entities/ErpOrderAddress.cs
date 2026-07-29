namespace MockErp.Api.Domain.Entities;

public sealed class ErpOrderAddress
{
    public Guid ErpOrderId { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string District { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string? PostalCode { get; set; }

    public string CountryCode { get; set; } = "TR";

    public ErpOrder ErpOrder { get; set; } = null!;
}
