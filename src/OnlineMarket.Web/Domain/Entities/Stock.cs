namespace OnlineMarket.Web.Domain.Entities;

public class Stock
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Product? Product { get; set; }
}
