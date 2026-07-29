namespace Recommendation.Api.Domain.Entities;

public sealed class OrderSnapshotItem
{
    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public OrderSnapshot Order { get; set; } = null!;

    public ProductSnapshot Product { get; set; } = null!;
}
