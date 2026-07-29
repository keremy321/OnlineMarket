using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Domain.Entities;

public class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public string? LockedBy { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastError { get; set; }
    public Guid CorrelationId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
