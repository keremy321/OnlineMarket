using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Domain.Entities;

public sealed class IntegrationStep
{
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }

    public IntegrationStepType StepType { get; set; }

    public byte SequenceNumber { get; set; }

    public IntegrationStepStatus Status { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTime? NextAttemptAtUtc { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public string? LockedBy { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ExternalReference { get; set; }

    public short? LastHttpStatusCode { get; set; }

    public IntegrationResultType? LastErrorType { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public IntegrationBatch Batch { get; set; } = null!;

    public ICollection<IntegrationAttempt> Attempts { get; } = [];
}
