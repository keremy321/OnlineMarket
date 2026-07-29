namespace ErpIntegration.Api.Domain.Entities;

public sealed class IntegrationOrderLine
{
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }

    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }

    public decimal NetLineAmount { get; set; }

    public decimal VatAmount { get; set; }

    public decimal LineTotal { get; set; }

    public IntegrationBatch Batch { get; set; } = null!;
}
