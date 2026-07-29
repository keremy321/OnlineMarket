using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Common.Messaging;

public static class OutboxMessageFactory
{
    public const string RecommendationDestination = "Recommendation.Api";
    public const string ErpIntegrationDestination = "ErpIntegration.Api";

    public static OutboxMessage Create<T>(
        T integrationEvent,
        string destination,
        string aggregateType,
        Guid aggregateId)
        where T : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);

        return new OutboxMessage
        {
            EventId = integrationEvent.EventId,
            EventType = typeof(T).Name,
            Destination = destination,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = EventJsonSerializer.Serialize(integrationEvent),
            Status = OutboxStatus.Pending,
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            AvailableAtUtc = integrationEvent.OccurredAtUtc,
            AttemptCount = 0,
            CorrelationId = integrationEvent.CorrelationId
        };
    }
}
