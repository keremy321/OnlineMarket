namespace ErpIntegration.Api.Contracts;

public sealed record IntegrationOrderAcceptedResponse(
    Guid EventId,
    Guid BatchId,
    Guid OrderId,
    string OrderNumber,
    string Status);

public sealed record IntegrationOrderStatusResponse(
    Guid OrderId,
    string Status,
    string? CurrentStep,
    DateTime? LastAttemptAtUtc,
    string? ErrorMessage,
    Guid BatchId,
    Guid EventId,
    string OrderNumber,
    Guid CustomerId,
    Guid CorrelationId,
    string BatchStatus,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorCode,
    IReadOnlyList<IntegrationStepStatusResponse> Steps);

public sealed record IntegrationStepStatusResponse(
    string StepType,
    int SequenceNumber,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? CompletedAtUtc,
    string? ExternalReference,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record IntegrationJobSummaryResponse(
    Guid BatchId,
    Guid EventId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid CorrelationId,
    string Status,
    string BatchStatus,
    string? CurrentStep,
    DateTime CreatedAtUtc,
    DateTime? LastAttemptAtUtc,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    bool Retryable);
