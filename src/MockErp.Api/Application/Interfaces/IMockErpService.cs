using MockErp.Api.Application.Models;
using MockErp.Api.Contracts;

namespace MockErp.Api.Application.Interfaces;

public interface IMockErpService
{
    Task<OperationResult> EnsureCustomerAsync(
        EnsureCustomerRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateOrderAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateStockMovementsAsync(
        CreateStockMovementsRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateAccountingEntryAsync(
        CreateAccountingEntryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<HistoryResult> GetCustomerOrdersAsync(
        string erpCustomerCode,
        CancellationToken cancellationToken = default);
}
