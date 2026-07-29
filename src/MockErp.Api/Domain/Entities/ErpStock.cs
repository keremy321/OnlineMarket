using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Domain.Entities;

public sealed class ErpStock
{
    public Guid Id { get; set; }

    public Guid ExternalProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public UnitType UnitType { get; set; }

    public decimal NetContent { get; set; }

    public int Quantity { get; set; }

    public int ReorderLevel { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
