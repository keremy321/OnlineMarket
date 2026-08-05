using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerSimilarProductStore(
    RecommendationDbContext dbContext)
    : ISimilarProductStore
{
    public async Task<SimilarProductContext> GetContextAsync(
        Guid sourceProductId,
        CancellationToken cancellationToken = default)
    {
        var products = await dbContext.ProductSnapshots
            .AsNoTracking()
            .Select(product => new SimilarProductSnapshot(
                product.ProductId,
                product.CategoryId,
                product.ParentCategoryId,
                product.BrandId,
                product.Price,
                product.NetContent,
                product.UnitType,
                product.IsActive,
                product.IsInStock,
                product.Popularity == null
                    ? 0m
                    : product.Popularity.Score,
                0m))
            .ToArrayAsync(cancellationToken);
        var affinityScores = await dbContext.ProductAffinities
            .AsNoTracking()
            .Where(affinity => affinity.SourceProductId == sourceProductId)
            .ToDictionaryAsync(
                affinity => affinity.RecommendedProductId,
                affinity => affinity.Score,
                cancellationToken);
        var withAffinity = products
            .Select(product => product with
            {
                FrequentlyBoughtTogetherScore = affinityScores.GetValueOrDefault(
                    product.ProductId)
            })
            .ToArray();
        return new SimilarProductContext(
            withAffinity.SingleOrDefault(
                product => product.ProductId == sourceProductId),
            withAffinity);
    }
}
