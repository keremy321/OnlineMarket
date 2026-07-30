using ErpIntegration.Api.Application.Models;

namespace ErpIntegration.Api.Application.Interfaces;

public interface IIntegrationStepStore
{
    Task<ClaimedIntegrationStep?> ClaimNextAsync(
        string workerId,
        DateTime nowUtc,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        ClaimedIntegrationStep claim,
        StepExecutionResult result,
        StepCompletionPlan plan,
        DateTime completedAtUtc,
        int durationMs,
        string workerId,
        CancellationToken cancellationToken = default);
}
