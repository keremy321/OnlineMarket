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

        var completedAttemptCount = result.OutboundCallMade
            ? claim.AttemptNumber
            : claim.AttemptNumber - 1;
        if (completedAttemptCount >= claim.MaxAttempts)
        {
            return new StepCompletionPlan(
                IntegrationStepStatus.WaitingManualRetry,
                null);
        }

        var exponent = Math.Min(
            Math.Max(completedAttemptCount - 1, 0),
            30);
        var multiplier = Math.Pow(2, exponent);
        var baseDelayMilliseconds = Math.Min(
            options.MaxRetryDelay.TotalMilliseconds,
            options.BaseRetryDelay.TotalMilliseconds * multiplier);
        var jitterMultiplier = 1d + GetJitterFraction(
            claim.StepId,
            completedAttemptCount);
        var delayMilliseconds = Math.Min(
            options.MaxRetryDelay.TotalMilliseconds,
            baseDelayMilliseconds * jitterMultiplier);
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

    private static double GetJitterFraction(
        Guid stepId,
        int attemptCount)
    {
        var bytes = stepId.ToByteArray();
        var seed = BitConverter.ToUInt32(bytes, 0)
            ^ unchecked((uint)attemptCount * 2654435761u);
        return seed % 1001 / 1000d * 0.20d;
    }
}
