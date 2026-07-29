using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Domain.Entities;

public sealed class IntegrationBatch
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public Guid MarketOrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public IntegrationBatchStatus Status { get; set; }

    public IntegrationStepType? CurrentStepType { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ProcessedEvent ProcessedEvent { get; set; } = null!;

    public IntegrationOrderSnapshot? OrderSnapshot { get; set; }

    public ICollection<IntegrationOrderLine> OrderLines { get; } = [];

    public ICollection<IntegrationStep> Steps { get; } = [];
}
