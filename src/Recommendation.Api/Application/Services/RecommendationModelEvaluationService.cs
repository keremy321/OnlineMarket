using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Services;

public sealed class RecommendationModelEvaluationService(
    IModelEvaluationSnapshotStore snapshotStore,
    IRecommendationModelClient modelClient,
    TimeProvider timeProvider)
    : IRecommendationModelEvaluationService
{
    public async Task<RecommendationModelClientResult<
        RecommendationModelEvaluationClientResponse>> EvaluateAsync(
            CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshotStore.GetSnapshotAsync(cancellationToken);
        if (snapshot.Interactions.Any(interaction =>
                !RecommendationSubjectIdContract.IsValid(
                    interaction.SubjectId)))
        {
            throw new InvalidOperationException(
                "Evaluation interactions require valid SubjectId values.");
        }

        if (snapshot.Interactions.Any(interaction =>
                interaction.OccurredAtUtc.Kind != DateTimeKind.Utc))
        {
            throw new InvalidOperationException(
                "Evaluation interactions require UTC occurrence timestamps.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = new RecommendationModelEvaluationRequest(
            $"temporal-v1-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            snapshot.CatalogueProductIds
                .OrderBy(productId => productId)
                .ToArray(),
            snapshot.CandidateProductIds
                .OrderBy(productId => productId)
                .ToArray(),
            snapshot.Interactions
                .OrderBy(interaction => interaction.SubjectId,
                    StringComparer.Ordinal)
                .ThenBy(interaction => interaction.OccurredAtUtc)
                .ThenBy(interaction => interaction.OrderId)
                .Select(interaction =>
                    new RecommendationModelEvaluationOrderRequest(
                        interaction.OrderId,
                        interaction.SubjectId,
                        interaction.OccurredAtUtc,
                        interaction.Items
                            .OrderBy(item => item.ProductId)
                            .Select(item =>
                                new RecommendationModelOrderInteractionItemRequest(
                                    item.ProductId,
                                    item.Quantity))
                            .ToArray()))
                .ToArray());
        return await modelClient.EvaluateAsync(request, cancellationToken);
    }
}
