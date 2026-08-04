using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerCartCompletionRecommendationStore(
    RecommendationDbContext dbContext)
    : ICartCompletionRecommendationStore
{
    public async Task<IReadOnlyList<CartAffinityCandidate>> GetCandidatesAsync(
        IReadOnlyCollection<Guid> cartProductIds,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ProductAffinities
            .AsNoTracking()
            .Where(rule =>
                cartProductIds.Contains(rule.SourceProductId)
                && !cartProductIds.Contains(rule.RecommendedProductId)
                && rule.RecommendedProduct.IsActive
                && rule.RecommendedProduct.IsInStock)
            .Select(rule => new CartAffinityCandidate(
                rule.SourceProductId,
                rule.RecommendedProductId,
                rule.Score,
                rule.Confidence,
                rule.Lift))
            .ToListAsync(cancellationToken);
    }
}
