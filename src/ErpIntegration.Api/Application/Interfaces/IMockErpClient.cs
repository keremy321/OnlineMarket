using ErpIntegration.Api.Application.Models;

namespace ErpIntegration.Api.Application.Interfaces;

public interface IMockErpClient
{
    Task<StepExecutionResult> ExecuteAsync(
        ClaimedIntegrationStep step,
        CancellationToken cancellationToken = default);
}
