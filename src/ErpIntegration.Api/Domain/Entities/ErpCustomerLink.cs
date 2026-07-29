namespace ErpIntegration.Api.Domain.Entities;

public sealed class ErpCustomerLink
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string ErpCustomerCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastVerifiedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
