using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Application.Models;

public sealed record ModelTrainingSnapshot(
    IReadOnlyList<ModelProductTrainingSnapshot> Products,
    IReadOnlyList<ModelOrderInteraction> Interactions);

public sealed record ModelProductTrainingSnapshot(
    Guid ProductId,
    Guid CategoryId,
    Guid BrandId,
    string Name,
    string? Description,
    UnitType UnitType,
    decimal NetContent,
    decimal Price,
    bool IsActive,
    bool IsInStock);

public sealed record ModelOrderInteraction(
    Guid OrderId,
    IReadOnlyList<ModelOrderInteractionItem> Items);

public sealed record ModelOrderInteractionItem(
    Guid ProductId,
    int Quantity);

public sealed record RecommendationModelTrainingRequest(
    string ModelVersion,
    Guid CorrelationId,
    IReadOnlyList<RecommendationModelProductRequest> Products,
    IReadOnlyList<RecommendationModelOrderInteractionRequest> Interactions);

public sealed record RecommendationModelProductRequest(
    Guid ProductId,
    Guid CategoryId,
    Guid BrandId,
    string Name,
    string? Description,
    string UnitType,
    decimal NetContent,
    decimal Price,
    bool IsActive,
    bool IsInStock);

public sealed record RecommendationModelOrderInteractionRequest(
    Guid OrderId,
    IReadOnlyList<RecommendationModelOrderInteractionItemRequest> Items);

public sealed record RecommendationModelOrderInteractionItemRequest(
    Guid ProductId,
    int Quantity);

public sealed record RecommendationModelTrainingClientResponse(
    string Status,
    RecommendationModelMetadataResponse Metadata);

public sealed record RecommendationModelMetadataResponse(
    string ModelVersion,
    Guid CorrelationId,
    DateTime TrainedAtUtc,
    int ProductCount,
    string InputHash,
    string Algorithm,
    IReadOnlyDictionary<string, string> LibraryVersions);

public sealed record RecommendationModelSimilarRequest(
    Guid ProductId,
    int Limit);

public sealed record RecommendationModelSimilarClientResponse(
    string ModelVersion,
    Guid SourceProductId,
    IReadOnlyList<RecommendationModelSimilarItemResponse> Items);

public sealed record RecommendationModelSimilarItemResponse(
    Guid ProductId,
    decimal TfidfScore);

public enum RecommendationModelClientOutcome
{
    Succeeded,
    Unavailable,
    InvalidResponse
}

public sealed record RecommendationModelClientResult<T>(
    RecommendationModelClientOutcome Outcome,
    T? Value = default)
    where T : class;

public sealed record RecommendationModelRecalculationResult(
    RecommendationModelClientOutcome Outcome,
    RecommendationModelMetadataResponse? Metadata);
