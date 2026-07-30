using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Application.Services;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Workers;

public sealed class IntegrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrationWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<IntegrationWorker> logger)
    : BackgroundService
{
    private readonly IntegrationWorkerOptions options = options.Value;
    private readonly string workerId = CreateWorkerId();

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The ERP integration worker is disabled.");
            return;
        }

        logger.LogInformation(
            "ERP integration worker {WorkerId} started.",
            workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<IntegrationStepProcessor>();
                processed = await processor.ProcessNextAsync(
                    workerId,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The ERP integration worker loop failed.");
            }

            if (!processed)
            {
                await Task.Delay(
                    options.PollInterval,
                    timeProvider,
                    stoppingToken);
            }
        }
    }

    private static string CreateWorkerId()
    {
        var value = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        return value.Length <= 100 ? value : value[..100];
    }
}
