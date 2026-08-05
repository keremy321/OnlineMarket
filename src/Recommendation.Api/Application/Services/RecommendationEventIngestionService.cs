using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;

namespace Recommendation.Api.Application.Services;

public sealed class RecommendationEventIngestionService(
    IRecommendationEventStore store,
    RecommendationEventValidator validator,
    IRecommendationSubjectIdDeriver subjectIdDeriver,
    TimeProvider timeProvider,
    ILogger<RecommendationEventIngestionService> logger)
    : IRecommendationEventIngestionService
{
    public async Task<RecommendationEventIngestionResult> IngestProductAsync(
        ProductSnapshotChangedV1Request request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = validator.ValidateProduct(request);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation.Errors);
        }

        var normalized = validation.NormalizedRequest;
        var result = await store.AcceptProductAsync(
            new ProductEventIntake(
                normalized,
                RecommendationEventPayloadHasher.Compute(normalized),
                GetUtcNow()),
            cancellationToken);

        return MapResult(
            result,
            normalized.EventId,
            "product",
            normalized.ProductId);
    }

    public async Task<RecommendationEventIngestionResult> IngestOrderAsync(
        OrderConfirmedForRecommendationV1Request request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = validator.ValidateOrder(request);
        if (!validation.IsValid || !validation.TotalQuantity.HasValue)
        {
            return ValidationFailure(validation.Errors);
        }

        var normalized = validation.NormalizedRequest;
        var subjectId = subjectIdDeriver.Derive(normalized.CustomerId);
        if (!RecommendationSubjectIdContract.IsValid(subjectId))
        {
            throw new InvalidOperationException(
                "Recommendation subject derivation returned an invalid identifier.");
        }

        var result = await store.AcceptOrderAsync(
            new OrderEventIntake(
                normalized,
                RecommendationEventPayloadHasher.Compute(normalized),
                GetUtcNow(),
                validation.TotalQuantity.Value,
                subjectId),
            cancellationToken);

        return MapResult(
            result,
            normalized.EventId,
            "order",
            normalized.OrderId);
    }

    private RecommendationEventIngestionResult MapResult(
        RecommendationEventStoreResult result,
        Guid eventId,
        string eventKind,
        Guid resourceId)
    {
        switch (result.Outcome)
        {
            case RecommendationEventStoreOutcome.Created:
                logger.LogInformation(
                    "Processed Recommendation {EventKind} event {EventId} for resource {ResourceId}.",
                    eventKind,
                    eventId,
                    resourceId);
                return Success(eventId, "Processed");

            case RecommendationEventStoreOutcome.Replay:
                logger.LogInformation(
                    "Accepted idempotent replay of Recommendation {EventKind} event {EventId}.",
                    eventKind,
                    eventId);
                return Success(eventId, "Replay");

            case RecommendationEventStoreOutcome.PayloadConflict:
                logger.LogWarning(
                    "Rejected Recommendation event {EventId} because its canonical payload differs from the processed payload.",
                    eventId);
                return Failure(
                    "Idempotency.PayloadConflict",
                    "The EventId has already been processed with a different payload.",
                    false);

            case RecommendationEventStoreOutcome.ProductSnapshotMissing:
                logger.LogWarning(
                    "Rejected Recommendation order event {EventId} because at least one product snapshot is missing.",
                    eventId);
                return Failure(
                    "Recommendation.ProductSnapshotMissing",
                    "At least one referenced product snapshot is missing.",
                    true);

            case RecommendationEventStoreOutcome.ResourceConflict:
                logger.LogWarning(
                    "Rejected Recommendation {EventKind} event {EventId} because a unique resource identity conflicts.",
                    eventKind,
                    eventId);
                return Failure(
                    "Recommendation.ResourceConflict",
                    "The event conflicts with an existing Recommendation resource.",
                    false);

            default:
                throw new InvalidOperationException(
                    $"Unknown Recommendation event store outcome '{result.Outcome}'.");
        }
    }

    private static RecommendationEventIngestionResult ValidationFailure(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new RecommendationEventIngestionResult(null, null, errors);
    }

    private static RecommendationEventIngestionResult Success(
        Guid eventId,
        string status)
    {
        return new RecommendationEventIngestionResult(
            new RecommendationEventAcceptedResponse(eventId, status),
            null,
            null);
    }

    private static RecommendationEventIngestionResult Failure(
        string code,
        string message,
        bool retryable)
    {
        return new RecommendationEventIngestionResult(
            null,
            new ApplicationError(code, message, retryable),
            null);
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }
}
