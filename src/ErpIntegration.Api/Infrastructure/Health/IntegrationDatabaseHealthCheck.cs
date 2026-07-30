using ErpIntegration.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ErpIntegration.Api.Infrastructure.Health;

public sealed class IntegrationDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<IntegrationDbContext>();
            var connected = await dbContext.Database.CanConnectAsync(
                cancellationToken);
            return connected
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(
                    "IntegrationDb is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "IntegrationDb is unavailable.",
                exception);
        }
    }
}
