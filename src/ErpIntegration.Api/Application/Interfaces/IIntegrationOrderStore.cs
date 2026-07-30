using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Entities;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Interfaces;

public interface IIntegrationOrderStore
{
    Task<IntakeStoreResult> AcceptAsync(
        IntegrationBatch batch,
        CancellationToken cancellationToken = default);

    Task<IntegrationOrderReadState?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IntegrationBatch?> GetForRetryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<bool> SaveRetryAsync(
        CancellationToken cancellationToken = default);

    Task<PagedReadResult<IntegrationJobReadState>> GetCustomerOrdersAsync(
        Guid customerId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<PagedReadResult<IntegrationJobReadState>> GetJobsAsync(
        IntegrationBatchStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
