using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Domain.Entities;

public sealed class IntegrationAttempt
{
    public long Id { get; set; }

    public Guid StepId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int? DurationMs { get; set; }

    public IntegrationResultType ResultType { get; set; }

    public short? HttpStatusCode { get; set; }

    public string RequestHash { get; set; } = string.Empty;

    public string? RequestPayloadMasked { get; set; }

    public string? ResponsePayloadMasked { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid CorrelationId { get; set; }

    public IntegrationStep Step { get; set; } = null!;
}
