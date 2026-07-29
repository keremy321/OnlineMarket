using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Domain.Entities;

public class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid SourceCartId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Confirmed;
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "TRY";
    public Guid CorrelationId { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Customer? Customer { get; set; }
    public Cart? SourceCart { get; set; }
    public OrderAddress? AddressSnapshot { get; set; }
    public Payment? Payment { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
