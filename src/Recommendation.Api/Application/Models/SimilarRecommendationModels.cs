using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Application.Models;

public sealed record SimilarProductSnapshot(
    Guid ProductId,
    Guid CategoryId,
    Guid? ParentCategoryId,
    Guid BrandId,
    decimal Price,
    decimal NetContent,
    UnitType UnitType,
    bool IsActive,
    bool IsInStock,
    decimal PopularityScore,
    decimal FrequentlyBoughtTogetherScore);

public sealed record SimilarProductContext(
    SimilarProductSnapshot? Source,
    IReadOnlyList<SimilarProductSnapshot> Candidates);

public sealed record SimilarRecommendationItem(
    Guid ProductId,
    decimal Score,
    decimal? TfidfScore,
    decimal PopularityScore,
    decimal FrequentlyBoughtTogetherScore,
    decimal? FallbackScore,
    SimilarRankingSource RankingSource,
    string? ModelVersion,
    decimal? CoPurchaseScore = null,
    string? ReasonCode = null,
    string? ReasonText = null);

public enum SimilarRankingSource
{
    PythonTfidf,
    PythonHybrid,
    CSharpContentFallback
}
