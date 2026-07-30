using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Models;

public sealed record ApplicationError(
    string Code,
    string Message,
    bool Retryable);

public sealed record IntakeOrderResult(
    IntegrationOrderAcceptedResponse? Value,
    ApplicationError? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors)
{
    public bool Succeeded => Value is not null;
}

public sealed record RetryOrderResult(
    IntegrationOrderStatusResponse? Value,
    ApplicationError? Error)
{
    public bool Succeeded => Value is not null;
}

public enum IntakeStoreOutcome
{
    Created,
    Replay,
    PayloadConflict,
    OrderConflict
}

public sealed record IntakeStoreResult(
    IntakeStoreOutcome Outcome,
    IntegrationOrderAcceptedResponse? Accepted);

public sealed record IntegrationOrderReadState(
    Guid BatchId,
    Guid EventId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid CorrelationId,
    IntegrationBatchStatus Status,
    IntegrationStepType? CurrentStepType,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTime? LastAttemptAtUtc,
    IReadOnlyList<IntegrationStepReadState> Steps);

public sealed record IntegrationStepReadState(
    IntegrationStepType StepType,
    byte SequenceNumber,
    IntegrationStepStatus Status,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? CompletedAtUtc,
    string? ExternalReference,
    string? LastErrorCode,
    string? LastErrorMessage);

public sealed record IntegrationJobReadState(
    Guid BatchId,
    Guid EventId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid CorrelationId,
    IntegrationBatchStatus Status,
    IntegrationStepType? CurrentStepType,
    DateTime CreatedAtUtc,
    DateTime? LastAttemptAtUtc,
    string? LastErrorCode,
    string? LastErrorMessage);

public sealed record PagedReadResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount);
