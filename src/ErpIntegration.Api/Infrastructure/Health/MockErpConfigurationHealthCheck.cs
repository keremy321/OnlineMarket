using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Infrastructure.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Health;

public sealed class MockErpConfigurationHealthCheck(
    IOptions<MockErpClientOptions> clientOptions,
    IOptions<IntegrationWorkerOptions> workerOptions)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = clientOptions.Value;
        _ = workerOptions.Value;
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
