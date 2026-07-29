namespace Recommendation.Api.Domain.Entities;

public sealed class OrderSnapshot
{
    public Guid OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public int TotalQuantity { get; set; }

    public int DistinctProductCount { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public ICollection<OrderSnapshotItem> Items { get; } = [];
}
