using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Application.Services;

public sealed class IntegrationStepProcessor(
    IIntegrationStepStore store,
    IMockErpClient mockErpClient,
    IntegrationRetryPolicy retryPolicy,
    IOptions<IntegrationWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<IntegrationStepProcessor> logger)
{
    private const string UnknownMaskedRequest =
        """{"operation":"unavailable"}""";
    private static readonly string UnknownRequestHash =
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(UnknownMaskedRequest)))
            .ToLowerInvariant();
    private readonly IntegrationWorkerOptions options = options.Value;

    public async Task<bool> ProcessNextAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 100)
        {
            throw new ArgumentException(
                "Worker identity must contain between one and 100 characters.",
                nameof(workerId));
        }

        var claimedAtUtc = GetUtcNow();
        var claim = await store.ClaimNextAsync(
            workerId,
            claimedAtUtc,
            options.LockTimeout,
            cancellationToken);
        if (claim is null)
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        StepExecutionResult result;
        try
        {
            result = await mockErpClient.ExecuteAsync(
                claim,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected ERP step execution failure for step {StepId} and correlation {CorrelationId}.",
                claim.StepId,
                claim.CorrelationId);
            result = new StepExecutionResult(
                IntegrationResultType.TransientFailure,
                null,
                UnknownRequestHash,
                UnknownMaskedRequest,
                null,
                "Integration.ExecutionFailure",
                "The ERP step could not be executed.",
                null,
                null);
        }

        stopwatch.Stop();
        var completedAtUtc = GetUtcNow();
        var plan = retryPolicy.Decide(claim, result, completedAtUtc);
        var durationMs = stopwatch.ElapsedMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
        var saved = await store.CompleteAsync(
            claim,
            result,
            plan,
            completedAtUtc,
            durationMs,
            workerId,
            cancellationToken);
        if (!saved)
        {
            logger.LogWarning(
                "Discarded a stale ERP step result for step {StepId} and correlation {CorrelationId}.",
                claim.StepId,
                claim.CorrelationId);
        }

        return true;
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }
}
