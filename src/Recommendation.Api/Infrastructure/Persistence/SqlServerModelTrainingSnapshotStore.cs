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
                item.Order.SubjectId,
                Item = new ModelOrderInteractionItem(
                    item.ProductId,
                    item.Quantity)
            })
            .ToArrayAsync(cancellationToken);
        if (items.Any(item =>
                !RecommendationSubjectIdContract.IsValid(item.SubjectId)))
        {
            throw new InvalidOperationException(
                "Recommendation order interactions require a completed SubjectId backfill before model training.");
        }

        var interactions = items
            .GroupBy(item => new
            {
                item.OrderId,
                item.SubjectId
            })
            .Select(group => new ModelOrderInteraction(
                group.Key.OrderId,
                group.Key.SubjectId!,
                group.Select(item => item.Item).ToArray()))
            .ToArray();
        return new ModelTrainingSnapshot(products, interactions);
    }
}
