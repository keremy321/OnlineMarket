using MockErp.Api.Application.Interfaces;
using MockErp.Api.Application.Models;
using MockErp.Api.Contracts;

namespace MockErp.Api.Application.Services;

public sealed class MockErpService(
    IMockErpStore store,
    MockErpRequestValidator validator,
    TimeProvider timeProvider,
    ILogger<MockErpService> logger)
    : IMockErpService
{
    public Task<OperationResult> EnsureCustomerAsync(
        EnsureCustomerRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        return ExecuteAsync(
            validation,
            idempotencyKey,
            store.EnsureCustomerAsync,
            cancellationToken);
    }

    public Task<OperationResult> CreateOrderAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        return ExecuteAsync(
            validation,
            idempotencyKey,
            store.CreateOrderAsync,
            cancellationToken);
    }

    public Task<OperationResult> CreateStockMovementsAsync(
        CreateStockMovementsRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        return ExecuteAsync(
            validation,
            idempotencyKey,
            store.CreateStockMovementsAsync,
            cancellationToken);
    }

    public Task<OperationResult> CreateAccountingEntryAsync(
        CreateAccountingEntryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        return ExecuteAsync(
            validation,
            idempotencyKey,
            store.CreateAccountingEntryAsync,
            cancellationToken);
    }

    public async Task<HistoryResult> GetCustomerOrdersAsync(
        string erpCustomerCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = erpCustomerCode.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalizedCode) || normalizedCode.Length > 50)
        {
            return new HistoryResult(
                null,
                new ApplicationError(
                    StatusCodes.Status400BadRequest,
                    "Validation.Failed",
                    "ErpCustomerCode is invalid.",
                    false));
        }

        var response = await store.GetCustomerOrdersAsync(
            normalizedCode,
            cancellationToken);
        return response is null
            ? new HistoryResult(
                null,
                new ApplicationError(
                    StatusCodes.Status404NotFound,
                    "MockErp.CustomerNotFound",
                    "The ERP customer was not found.",
                    false))
            : new HistoryResult(response, null);
    }

    private async Task<OperationResult> ExecuteAsync<T>(
        RequestValidationResult<T> validation,
        string idempotencyKey,
        Func<T, string, string, DateTime, CancellationToken, Task<StoreOperationResult>>
            operation,
        CancellationToken cancellationToken)
    {
        if (!validation.IsValid)
        {
            return new OperationResult(null, null, validation.Errors);
        }

        var hash = CanonicalRequestHasher.Compute(validation.NormalizedRequest);
        var storeResult = await operation(
            validation.NormalizedRequest,
            idempotencyKey,
            hash,
            GetUtcNow(),
            cancellationToken);
        if (storeResult.Error is not null)
        {
            logger.LogWarning(
                "Mock ERP operation failed with code {ErrorCode}; retryable: {Retryable}.",
                storeResult.Error.Code,
                storeResult.Error.Retryable);
        }

        return new OperationResult(
            storeResult.Response,
            storeResult.Error,
            null);
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }
}
