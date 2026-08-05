using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Services;

public sealed class RecommendationModelOrchestrationService(
    IModelTrainingSnapshotStore snapshotStore,
    IRecommendationModelClient modelClient,
    TimeProvider timeProvider)
    : IRecommendationModelOrchestrationService
{
    public async Task<RecommendationModelRecalculationResult>
        RecalculateAsync(
            CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshotStore.GetTrainingSnapshotAsync(
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = CreateRequest(
            snapshot,
            $"model-set-v3-{now:yyyyMMddHHmmssfff}",
            Guid.NewGuid());
        var result = await modelClient.TrainAsync(request, cancellationToken);
        return new RecommendationModelRecalculationResult(
            result.Outcome,
            result.Value?.Metadata);
    }

    public static RecommendationModelTrainingRequest CreateRequest(
        ModelTrainingSnapshot snapshot,
        string modelVersion,
        Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A model-training correlation ID is required.",
                nameof(correlationId));
        }

        if (snapshot.Interactions.Any(interaction =>
                !RecommendationSubjectIdContract.IsValid(
                    interaction.SubjectId)))
        {
            throw new InvalidOperationException(
                "Every model-training interaction requires a valid SubjectId.");
        }

        return new RecommendationModelTrainingRequest(
            modelVersion,
            correlationId,
            snapshot.Products
                .OrderBy(product => product.ProductId)
                .Select(product => new RecommendationModelProductRequest(
                    product.ProductId,
                    product.CategoryId,
                    product.BrandId,
                    product.Name,
                    product.Description,
                    product.UnitType.ToString(),
                    product.NetContent,
                    product.Price,
                    product.IsActive,
                    product.IsInStock))
                .ToArray(),
            snapshot.Interactions
                .OrderBy(interaction => interaction.OrderId)
                .Select(interaction =>
                    new RecommendationModelOrderInteractionRequest(
                        interaction.OrderId,
                        interaction.SubjectId,
                        interaction.Items
                            .OrderBy(item => item.ProductId)
                            .Select(item =>
                                new RecommendationModelOrderInteractionItemRequest(
                                    item.ProductId,
                                    item.Quantity))
                            .ToArray()))
                .ToArray());
    }
}
