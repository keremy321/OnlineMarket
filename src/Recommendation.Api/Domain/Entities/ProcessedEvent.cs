namespace Recommendation.Api.Domain.Entities;

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public DateTime ProcessedAtUtc { get; set; }
}
