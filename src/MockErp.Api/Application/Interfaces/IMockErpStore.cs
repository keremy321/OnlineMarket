using MockErp.Api.Application.Models;
using MockErp.Api.Contracts;

namespace MockErp.Api.Application.Interfaces;

public interface IMockErpStore
{
    Task<StoreOperationResult> EnsureCustomerAsync(
        EnsureCustomerRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoreOperationResult> CreateOrderAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoreOperationResult> CreateStockMovementsAsync(
        CreateStockMovementsRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoreOperationResult> CreateAccountingEntryAsync(
        CreateAccountingEntryRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<CustomerOrderHistoryResponse?> GetCustomerOrdersAsync(
        string erpCustomerCode,
        CancellationToken cancellationToken = default);
}
