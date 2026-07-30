using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Application.Services;

public sealed class IntegrationRetryPolicy(
    IOptions<IntegrationWorkerOptions> options)
{
    private readonly IntegrationWorkerOptions options = options.Value;

    public StepCompletionPlan Decide(
        ClaimedIntegrationStep claim,
        StepExecutionResult result,
        DateTime completedAtUtc)
    {
        if (result.Succeeded)
        {
            return new StepCompletionPlan(
                IntegrationStepStatus.Succeeded,
                null);
        }

        if (result.ResultType == IntegrationResultType.PermanentFailure)
        {
            return new StepCompletionPlan(
                IntegrationStepStatus.FailedPermanent,
                null);
        }

        if (claim.AttemptNumber >= claim.MaxAttempts)
        {
            return new StepCompletionPlan(
                IntegrationStepStatus.WaitingManualRetry,
                null);
        }

        var exponent = Math.Min(claim.AttemptNumber - 1, 30);
        var multiplier = Math.Pow(2, exponent);
        var delayMilliseconds = Math.Min(
            options.MaxRetryDelay.TotalMilliseconds,
            options.BaseRetryDelay.TotalMilliseconds * multiplier);
        var nextAttemptAtUtc = completedAtUtc.AddMilliseconds(
            delayMilliseconds);
        if (result.RetryAfterUtc > nextAttemptAtUtc)
        {
            nextAttemptAtUtc = result.RetryAfterUtc.Value;
        }

        return new StepCompletionPlan(
            IntegrationStepStatus.Retrying,
            nextAttemptAtUtc);
    }
}
