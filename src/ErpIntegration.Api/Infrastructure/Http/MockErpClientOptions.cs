using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Http;

public sealed record MockErpClientOptions
{
    public const string SectionName = "MockErp";

    public string BaseAddress { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public int FastRetryCount { get; init; } = 1;

    public TimeSpan FastRetryDelay { get; init; } =
        TimeSpan.FromMilliseconds(100);

    public TimeSpan MaxFastRetryDelay { get; init; } =
        TimeSpan.FromSeconds(2);

    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    public TimeSpan CircuitBreakerBreakDuration { get; init; } =
        TimeSpan.FromSeconds(30);
}

public sealed class MockErpClientOptionsValidator
    : IValidateOptions<MockErpClientOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        MockErpClientOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(
                options.BaseAddress,
                UriKind.Absolute,
                out var baseAddress)
            || baseAddress.Scheme is not ("http" or "https"))
        {
            failures.Add(
                "MockErp:BaseAddress must be an absolute HTTP or HTTPS URI.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("MockErp:ApiKey is required.");
        }

        if (options.Timeout < TimeSpan.FromMilliseconds(250)
            || options.Timeout > TimeSpan.FromSeconds(30))
        {
            failures.Add(
                "MockErp:Timeout must be between 250 milliseconds and 30 seconds.");
        }

        if (options.FastRetryCount is < 0 or > 3)
        {
            failures.Add("MockErp:FastRetryCount must be between zero and three.");
        }

        if (options.FastRetryDelay < TimeSpan.Zero
            || options.MaxFastRetryDelay < options.FastRetryDelay
            || options.MaxFastRetryDelay > TimeSpan.FromSeconds(10))
        {
            failures.Add(
                "Mock ERP fast-retry delays are outside the supported range.");
        }

        if (options.CircuitBreakerFailureThreshold is < 2 or > 100)
        {
            failures.Add(
                "Mock ERP circuit-breaker threshold must be between two and 100.");
        }

        if (options.CircuitBreakerBreakDuration
                < TimeSpan.FromSeconds(1)
            || options.CircuitBreakerBreakDuration
                > TimeSpan.FromMinutes(10))
        {
            failures.Add(
                "Mock ERP circuit-breaker duration must be between one second and ten minutes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
