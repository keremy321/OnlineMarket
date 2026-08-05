using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerPersonalizedRecommendationStore(
    RecommendationDbContext dbContext)
    : IPersonalizedRecommendationStore
{
    public async Task<PersonalizedRecommendationContext> GetContextAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A customer ID is required.",
                nameof(customerId));
        }

        var products = await dbContext.ProductSnapshots
            .AsNoTracking()
            .OrderBy(product => product.ProductId)
            .Select(product => new PersonalizedProductSnapshot(
                product.ProductId,
                product.CategoryId,
                product.BrandId,
                product.IsActive,
                product.IsInStock,
                product.Popularity == null
                    ? 0m
                    : product.Popularity.Score))
            .ToArrayAsync(cancellationToken);
        var purchases = await dbContext.OrderSnapshotItems
            .AsNoTracking()
            .Where(item => item.Order.CustomerId == customerId)
            .OrderBy(item => item.OrderId)
            .ThenBy(item => item.ProductId)
            .Select(item => new PersonalizedPurchaseSnapshot(
                item.ProductId,
                item.Product.CategoryId,
                item.Product.BrandId,
                item.Quantity))
            .ToArrayAsync(cancellationToken);
        return new PersonalizedRecommendationContext(products, purchases);
    }
}
