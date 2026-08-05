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
    string SubjectId,
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
    string SubjectId,
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
    int SubjectCount,
    int InteractionCount,
    string InputHash,
    string Algorithm,
    IReadOnlyList<string> AlgorithmComponents,
    RecommendationModelComponentStatusesResponse Components,
    RecommendationModelAlsParametersResponse AlsParameters,
    RecommendationModelHybridParametersResponse? HybridParameters,
    IReadOnlyDictionary<string, string> LibraryVersions);

public sealed record RecommendationModelComponentStatusesResponse(
    RecommendationModelComponentStatusResponse Tfidf,
    RecommendationModelComponentStatusResponse Als,
    RecommendationModelComponentStatusResponse? Popularity,
    RecommendationModelComponentStatusResponse? Association,
    RecommendationModelComponentStatusResponse? Hybrid);

public sealed record RecommendationModelComponentStatusResponse(
    string Status,
    int TrainingDurationMilliseconds);

public sealed record RecommendationModelAlsParametersResponse(
    int Factors,
    decimal Regularization,
    int Iterations,
    decimal Alpha,
    int RandomSeed);

public sealed record RecommendationModelHybridParametersResponse(
    RecommendationModelPersonalizedHybridWeightsResponse PersonalizedWeights,
    RecommendationModelSimilarHybridWeightsResponse SimilarWeights,
    int CandidatePoolMultiplier,
    int CandidatePoolCap,
    string ContentAffinityAggregation,
    string MissingComponentPolicy);

public sealed record RecommendationModelPersonalizedHybridWeightsResponse(
    decimal Als,
    decimal ContentAffinity,
    decimal Association,
    decimal Popularity);

public sealed record RecommendationModelSimilarHybridWeightsResponse(
    decimal ContentSimilarity,
    decimal CoPurchaseSimilarity,
    decimal Popularity);

public sealed record RecommendationModelSimilarRequest(
    Guid ProductId,
    int Limit);

public sealed record RecommendationModelSimilarClientResponse(
    string ModelVersion,
    string Strategy,
    Guid SourceProductId,
    IReadOnlyList<RecommendationModelSimilarItemResponse> Items);

public sealed record RecommendationModelSimilarItemResponse(
    Guid ProductId,
    decimal TfidfScore,
    decimal CoPurchaseScore,
    decimal PopularityScore,
    decimal FinalScore,
    string ReasonCode,
    string ReasonText);

public sealed record RecommendationModelPersonalizedRequest(
    string SubjectId,
    int Limit,
    bool ExcludePreviouslyPurchased,
    string Strategy = "Als");

public sealed record RecommendationModelPersonalizedClientResponse(
    string ModelVersion,
    string Strategy,
    IReadOnlyList<RecommendationModelPersonalizedItemResponse>
        Recommendations);

public sealed record RecommendationModelPersonalizedItemResponse(
    Guid ProductId,
    decimal Score,
    decimal? Confidence,
    string ReasonCode,
    string ReasonText,
    decimal? AlsScore = null,
    decimal? ContentAffinityScore = null,
    decimal? AssociationScore = null,
    decimal? PopularityScore = null,
    decimal? FinalScore = null);

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
