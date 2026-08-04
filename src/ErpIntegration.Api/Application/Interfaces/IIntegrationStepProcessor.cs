namespace ErpIntegration.Api.Application.Interfaces;

public interface IIntegrationStepProcessor
{
    Task<bool> ProcessNextAsync(
        string workerId,
        CancellationToken cancellationToken = default);
}
