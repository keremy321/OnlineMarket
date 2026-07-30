using ErpIntegration.Api.Application.Models;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Configuration;

public sealed class IntegrationWorkerOptionsValidator
    : IValidateOptions<IntegrationWorkerOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IntegrationWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.PollInterval < TimeSpan.FromMilliseconds(50)
            || options.PollInterval > TimeSpan.FromMinutes(1))
        {
            failures.Add(
                "IntegrationWorker:PollInterval must be between 50 milliseconds and one minute.");
        }

        if (options.LockTimeout < TimeSpan.FromSeconds(5)
            || options.LockTimeout > TimeSpan.FromMinutes(30))
        {
            failures.Add(
                "IntegrationWorker:LockTimeout must be between five seconds and 30 minutes.");
        }

        if (options.BaseRetryDelay < TimeSpan.FromMilliseconds(100)
            || options.BaseRetryDelay > TimeSpan.FromMinutes(10))
        {
            failures.Add(
                "IntegrationWorker:BaseRetryDelay must be between 100 milliseconds and ten minutes.");
        }

        if (options.MaxRetryDelay < options.BaseRetryDelay
            || options.MaxRetryDelay > TimeSpan.FromHours(1))
        {
            failures.Add(
                "IntegrationWorker:MaxRetryDelay must be at least the base delay and no more than one hour.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
