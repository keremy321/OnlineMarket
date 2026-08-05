using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerModelTrainingSnapshotStore(
    RecommendationDbContext dbContext)
    : IModelTrainingSnapshotStore
{
    public async Task<ModelTrainingSnapshot> GetTrainingSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await dbContext.ProductSnapshots
            .AsNoTracking()
            .OrderBy(product => product.ProductId)
            .Select(product => new ModelProductTrainingSnapshot(
                product.ProductId,
                product.CategoryId,
                product.BrandId,
                product.Name,
                product.Description,
                product.UnitType,
                product.NetContent,
                product.Price,
                product.IsActive,
                product.IsInStock))
            .ToArrayAsync(cancellationToken);
        var items = await dbContext.OrderSnapshotItems
            .AsNoTracking()
            .OrderBy(item => item.OrderId)
            .ThenBy(item => item.ProductId)
            .Select(item => new
            {
                item.OrderId,
                Item = new ModelOrderInteractionItem(
                    item.ProductId,
                    item.Quantity)
            })
            .ToArrayAsync(cancellationToken);
        var interactions = items
            .GroupBy(item => item.OrderId)
            .Select(group => new ModelOrderInteraction(
                group.Key,
                group.Select(item => item.Item).ToArray()))
            .ToArray();
        return new ModelTrainingSnapshot(products, interactions);
    }
}
