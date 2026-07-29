using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Domain.Entities;

public class Cart
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public CartStatus Status { get; set; } = CartStatus.Active;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Customer? Customer { get; set; }
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
    public Order? ConvertedOrder { get; set; }
}
