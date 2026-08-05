namespace Recommendation.Api.Application.Models;

public sealed record PersonalizedProductSnapshot(
    Guid ProductId,
    Guid CategoryId,
    Guid BrandId,
    bool IsActive,
    bool IsInStock,
    decimal PopularityScore);

public sealed record PersonalizedPurchaseSnapshot(
    Guid ProductId,
    Guid CategoryId,
    Guid BrandId,
    int Quantity);

public sealed record PersonalizedRecommendationContext(
    IReadOnlyList<PersonalizedProductSnapshot> Products,
    IReadOnlyList<PersonalizedPurchaseSnapshot> Purchases);

public sealed record PersonalizedRecommendationItem(
    Guid ProductId,
    decimal Score,
    decimal? Confidence,
    PersonalizedRankingSource RankingSource,
    string? ModelVersion);

public enum PersonalizedRankingSource
{
    PythonImplicitAls,
    CSharpPreferenceFallback
}
