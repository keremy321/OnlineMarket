using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Services;

public sealed class OutboxService : IOutboxService
{
    private readonly IOutboxStore _outboxStore;
    private readonly IOutboxDispatcher _outboxDispatcher;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(
        IOutboxStore outboxStore,
        IOutboxDispatcher outboxDispatcher,
        ILogger<OutboxService> logger)
    {
        _outboxStore = outboxStore;
        _outboxDispatcher = outboxDispatcher;
        _logger = logger;
    }

    public async Task ProcessPendingMessagesAsync(
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        var workerId = CreateWorkerId();
        var claimedMessages = await _outboxStore.ClaimAsync(
            batchSize,
            workerId,
            DateTime.UtcNow,
            cancellationToken);

        foreach (var message in claimedMessages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            OutboxDispatchResultDto dispatchResult;
            try
            {
                dispatchResult = await _outboxDispatcher.DispatchAsync(
                    message,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Transport failure dispatching outbox message {MessageId} ({EventType}) to {Destination}.",
                    message.Id,
                    message.EventType,
                    message.Destination);

                dispatchResult = new OutboxDispatchResultDto(
                    false,
                    true,
                    "Delivery.TransportFailure",
                    "Downstream delivery failed.");
            }

            await _outboxStore.RecordDeliveryResultAsync(
                new OutboxDeliveryResultDto(
                    message.Id,
                    workerId,
                    dispatchResult.Succeeded,
                    dispatchResult.Retryable,
                    dispatchResult.ErrorCode,
                    dispatchResult.MaskedError,
                    DateTime.UtcNow),
                cancellationToken);
        }
    }

    private static string CreateWorkerId()
    {
        var workerId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return workerId.Length <= 100 ? workerId : workerId[..100];
    }
}
