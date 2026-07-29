namespace MockErp.Api.Domain.Entities;

public sealed class ErpStockMovement
{
    public Guid Id { get; set; }

    public Guid ErpOrderId { get; set; }

    public Guid ExternalOrderId { get; set; }

    public Guid ExternalProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int QuantityChange { get; set; }

    public int PreviousQuantity { get; set; }

    public int NewQuantity { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ErpOrder ErpOrder { get; set; } = null!;
}
