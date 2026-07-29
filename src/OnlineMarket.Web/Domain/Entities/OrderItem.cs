namespace OnlineMarket.Web.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetLineAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
