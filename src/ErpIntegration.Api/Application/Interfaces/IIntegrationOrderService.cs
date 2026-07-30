using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Interfaces;

public interface IIntegrationOrderService
{
    Task<IntakeOrderResult> AcceptAsync(
        OrderReadyForErpV1Request request,
        CancellationToken cancellationToken = default);

    Task<IntegrationOrderStatusResponse?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<RetryOrderResult> RetryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<IntegrationJobSummaryResponse>> GetCustomerOrdersAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<IntegrationJobSummaryResponse>> GetJobsAsync(
        IntegrationBatchStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
