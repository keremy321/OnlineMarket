namespace MockErp.Api.Domain.Entities;

public sealed class ErpCustomer
{
    public Guid Id { get; set; }

    public string ErpCustomerCode { get; set; } = string.Empty;

    public Guid ExternalCustomerId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string District { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string? PostalCode { get; set; }

    public string CountryCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<ErpOrder> Orders { get; } = [];

    public ICollection<ErpAccountingEntry> AccountingEntries { get; } = [];

    public ICollection<ErpAccountingEntryLine> AccountingEntryLines { get; } =
        [];
}
