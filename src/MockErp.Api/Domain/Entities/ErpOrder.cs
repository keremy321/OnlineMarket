using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Domain.Entities;

public sealed class ErpOrder
{
    public Guid Id { get; set; }

    public string ErpOrderNumber { get; set; } = string.Empty;

    public Guid ExternalOrderId { get; set; }

    public string MarketOrderNumber { get; set; } = string.Empty;

    public Guid ErpCustomerId { get; set; }

    public DateTime OrderPlacedAtUtc { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public decimal Subtotal { get; set; }

    public decimal VatTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ErpCustomer ErpCustomer { get; set; } = null!;

    public ErpOrderAddress? Address { get; set; }

    public ICollection<ErpOrderLine> Lines { get; } = [];

    public ICollection<ErpStockMovement> StockMovements { get; } = [];

    public ErpAccountingEntry? AccountingEntry { get; set; }
}
