using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Models;

public sealed record IntegrationWorkerOptions
{
    public const string SectionName = "IntegrationWorker";

    public bool Enabled { get; init; } = true;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public int ClaimBatchSize { get; init; } = 2;

    public int MaximumParallelism { get; init; } = 2;
}

public sealed record ClaimedIntegrationStep(
    Guid StepId,
    Guid BatchId,
    IntegrationStepType StepType,
    byte SequenceNumber,
    int AttemptNumber,
    int MaxAttempts,
    string IdempotencyKey,
    Guid MarketOrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid CorrelationId,
    DateTime ClaimedAtUtc,
    IntegrationWorkerOrderSnapshot Snapshot,
    IReadOnlyList<IntegrationWorkerOrderLine> Lines,
    string? ErpCustomerCode);

public sealed record IntegrationWorkerOrderSnapshot(
    Guid CustomerId,
    string FirstName,
    string LastName,
    string RecipientName,
    string Email,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode,
    DateTime OrderPlacedAtUtc,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency);

public sealed record IntegrationWorkerOrderLine(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetLineAmount,
    decimal VatAmount,
    decimal LineTotal);

public sealed record StepExecutionResult(
    IntegrationResultType ResultType,
    short? HttpStatusCode,
    string RequestHash,
    string RequestPayloadMasked,
    string? ResponsePayloadMasked,
    string? ErrorCode,
    string? ErrorMessage,
    string? ExternalReference,
    DateTime? RetryAfterUtc)
{
    public bool OutboundCallMade { get; init; }

    public bool Succeeded =>
        ResultType is IntegrationResultType.Succeeded
            or IntegrationResultType.IdempotentReplay;
}

public sealed record StepCompletionPlan(
    IntegrationStepStatus StepStatus,
    DateTime? NextAttemptAtUtc);
