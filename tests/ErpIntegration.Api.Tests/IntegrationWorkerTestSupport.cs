using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Domain.Enums;
using ErpIntegration.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Tests;

internal static class IntegrationWorkerTestSupport
{
    public static readonly IntegrationWorkerOptions ImmediateRetryOptions =
        new()
        {
            PollInterval = TimeSpan.FromMilliseconds(50),
            LockTimeout = TimeSpan.FromSeconds(5),
            BaseRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

    public static async Task AcceptAsync(
        IntegrationTestDatabase database,
        Contracts.OrderReadyForErpV1Request request)
    {
        await using var context = database.CreateContext();
        var service = CreateOrderService(context);
        Assert.True((await service.AcceptAsync(request)).Succeeded);
    }

    public static IntegrationStepProcessor CreateProcessor(
        IntegrationDbContext context,
        IMockErpClient client,
        IntegrationWorkerOptions? workerOptions = null)
    {
        var options = Options.Create(
            workerOptions ?? ImmediateRetryOptions);
        return new IntegrationStepProcessor(
            new SqlServerIntegrationStepStore(
                context,
                NullLogger<SqlServerIntegrationStepStore>.Instance),
            client,
            new IntegrationRetryPolicy(options),
            options,
            TimeProvider.System,
            NullLogger<IntegrationStepProcessor>.Instance);
    }

    public static IntegrationOrderService CreateOrderService(
        IntegrationDbContext context)
    {
        return new IntegrationOrderService(
            new SqlServerIntegrationOrderStore(
                context,
                NullLogger<SqlServerIntegrationOrderStore>.Instance),
            new OrderReadyForErpV1Validator(),
            TimeProvider.System,
            NullLogger<IntegrationOrderService>.Instance);
    }

    public static StepExecutionResult Success(
        ClaimedIntegrationStep step)
    {
        var externalReference = step.StepType switch
        {
            IntegrationStepType.EnsureCustomer => "CARI-WORKER-TEST",
            IntegrationStepType.CreateOrder => "SIP-WORKER-TEST",
            IntegrationStepType.CreateStockMovement =>
                step.MarketOrderId.ToString("D"),
            IntegrationStepType.CreateAccountingEntry => "FIS-WORKER-TEST",
            _ => throw new ArgumentOutOfRangeException()
        };
        return new StepExecutionResult(
            IntegrationResultType.Succeeded,
            201,
            new string('a', 64),
            MaskedRequest(step),
            """{"succeeded":true}""",
            null,
            null,
            externalReference,
            null)
        {
            OutboundCallMade = true
        };
    }

    public static StepExecutionResult Transient(
        ClaimedIntegrationStep step)
    {
        return new StepExecutionResult(
            IntegrationResultType.TransientFailure,
            503,
            new string('b', 64),
            MaskedRequest(step),
            """{"code":"MockErp.Unavailable","retryable":true}""",
            "MockErp.Unavailable",
            "Mock ERP is temporarily unavailable.",
            null,
            null)
        {
            OutboundCallMade = true
        };
    }

    public static StepExecutionResult Permanent(
        ClaimedIntegrationStep step)
    {
        return new StepExecutionResult(
            IntegrationResultType.PermanentFailure,
            400,
            new string('c', 64),
            MaskedRequest(step),
            """{"code":"Validation.Failed","retryable":false}""",
            "Validation.Failed",
            "Mock ERP rejected the request.",
            null,
            null)
        {
            OutboundCallMade = true
        };
    }

    public static StepExecutionResult CircuitOpen(
        ClaimedIntegrationStep step)
    {
        return new StepExecutionResult(
            IntegrationResultType.TransientFailure,
            null,
            new string('d', 64),
            MaskedRequest(step),
            null,
            "MockErp.CircuitOpen",
            "The Mock ERP circuit is temporarily open.",
            null,
            null)
        {
            OutboundCallMade = false
        };
    }

    public static StepExecutionResult RateLimited(
        ClaimedIntegrationStep step,
        DateTime? retryAfterUtc = null)
    {
        return new StepExecutionResult(
            IntegrationResultType.TransientFailure,
            429,
            new string('e', 64),
            MaskedRequest(step),
            """{"code":"RateLimit.Exceeded","retryable":true}""",
            "RateLimit.Exceeded",
            "Mock ERP rate limited the request.",
            null,
            retryAfterUtc)
        {
            OutboundCallMade = true
        };
    }

    private static string MaskedRequest(ClaimedIntegrationStep step)
    {
        return $$"""{"operation":"{{step.StepType}}","orderId":"{{step.MarketOrderId:D}}"}""";
    }
}

internal sealed class RecordingMockErpClient(
    Func<ClaimedIntegrationStep, int, StepExecutionResult> resultFactory)
    : IMockErpClient
{
    private readonly object gate = new();
    private readonly List<ClaimedIntegrationStep> calls = [];

    public IReadOnlyList<ClaimedIntegrationStep> Calls
    {
        get
        {
            lock (gate)
            {
                return calls.ToArray();
            }
        }
    }

    public Task<StepExecutionResult> ExecuteAsync(
        ClaimedIntegrationStep step,
        CancellationToken cancellationToken = default)
    {
        int callNumber;
        lock (gate)
        {
            calls.Add(step);
            callNumber = calls.Count;
        }

        return Task.FromResult(resultFactory(step, callNumber));
    }
}
