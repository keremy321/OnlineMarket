using OnlineMarket.Web.Application.Interfaces;

namespace OnlineMarket.Web.Infrastructure.Workers;

public class OutboxBackgroundWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundWorker> _logger;
    private readonly TimeSpan _period = TimeSpan.FromSeconds(5);

    public OutboxBackgroundWorker(IServiceProvider serviceProvider, ILogger<OutboxBackgroundWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxBackgroundWorker starting execution with interval {Period}", _period);

        using var timer = new PeriodicTimer(_period);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

                await outboxService.ProcessPendingMessagesAsync(batchSize: 10, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred during OutboxBackgroundWorker execution cycle");
            }
        }
    }
}
