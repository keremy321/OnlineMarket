using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class OutboxService : IOutboxService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(
        OnlineMarketDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OutboxService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task CreateOutboxMessageAsync<T>(string eventType, string destination, string aggregateType, Guid aggregateId, T eventPayload, Guid correlationId)
    {
        var outboxMessage = new OutboxMessage
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            Destination = destination,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = JsonSerializer.Serialize(eventPayload),
            Status = OutboxStatus.Pending,
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            AttemptCount = 0,
            CorrelationId = correlationId
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();
    }

    public async Task ProcessPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
    {
        var workerId = $"Worker-{Environment.MachineName}-{Environment.ProcessId}";
        var now = DateTime.UtcNow;

        // Step 1: Claim batch in a short transaction
        List<OutboxMessage> claimedMessages;
        using (var claimTx = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            claimedMessages = await _dbContext.OutboxMessages
                .Where(m => (m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Retrying)
                            && m.AttemptCount < 5
                            && m.AvailableAtUtc <= now
                            && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)
                            && (m.LockedAtUtc == null || m.LockedAtUtc < now.AddMinutes(-5)))
                .OrderBy(m => m.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (!claimedMessages.Any())
            {
                await claimTx.CommitAsync(cancellationToken);
                return;
            }

            foreach (var msg in claimedMessages)
            {
                msg.Status = OutboxStatus.Processing;
                msg.LockedAtUtc = now;
                msg.LockedBy = workerId;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await claimTx.CommitAsync(cancellationToken);
        }

        // Step 2: Send HTTP for each claimed message outside transaction
        foreach (var msg in claimedMessages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            bool success = false;
            string? errorMessage = null;

            try
            {
                success = await DispatchMessageHttpAsync(msg, cancellationToken);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogError(ex, "Error dispatching outbox message {MessageId} ({EventType}) to {Destination}", msg.Id, msg.EventType, msg.Destination);
            }

            // Step 3: Update message result in a short transaction
            using var updateTx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var dbMsg = await _dbContext.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
            if (dbMsg != null)
            {
                dbMsg.AttemptCount++;
                dbMsg.LockedAtUtc = null;
                dbMsg.LockedBy = null;

                if (success)
                {
                    dbMsg.Status = OutboxStatus.Processed;
                    dbMsg.ProcessedAtUtc = DateTime.UtcNow;
                    dbMsg.LastError = null;
                    dbMsg.LastErrorCode = null;
                }
                else
                {
                    dbMsg.LastError = errorMessage ?? "HTTP call failed";
                    dbMsg.LastErrorCode = "DeliveryFailed";

                    if (dbMsg.AttemptCount >= 5)
                    {
                        dbMsg.Status = OutboxStatus.FailedPermanent;
                    }
                    else
                    {
                        dbMsg.Status = OutboxStatus.Retrying;
                        dbMsg.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Pow(2, dbMsg.AttemptCount) * 5);
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            await updateTx.CommitAsync(cancellationToken);
        }
    }

    private async Task<bool> DispatchMessageHttpAsync(OutboxMessage msg, CancellationToken cancellationToken)
    {
        HttpClient client;
        string requestUri;

        if (msg.Destination.Contains("Recommendation"))
        {
            client = _httpClientFactory.CreateClient("RecommendationApi");
            requestUri = msg.EventType switch
            {
                "ProductSnapshotChangedV1" => "/api/v1/events/products",
                "OrderConfirmedForRecommendationV1" => "/api/v1/events/orders",
                _ => "/api/v1/events/orders"
            };
        }
        else if (msg.Destination.Contains("ErpIntegration"))
        {
            client = _httpClientFactory.CreateClient("ErpIntegrationApi");
            requestUri = "/api/v1/integration/orders";
        }
        else
        {
            _logger.LogWarning("Unknown outbox destination: {Destination}", msg.Destination);
            return false;
        }

        var content = new StringContent(msg.Payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(requestUri, content, cancellationToken);

        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted;
    }
}
