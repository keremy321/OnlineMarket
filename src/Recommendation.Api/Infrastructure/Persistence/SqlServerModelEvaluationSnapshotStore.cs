using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class SqlServerModelEvaluationSnapshotStore(
    RecommendationDbContext dbContext)
    : IModelEvaluationSnapshotStore
{
    public async Task<ModelEvaluationSnapshot> GetSnapshotAsync(
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
            .OrderBy(item => item.Order.SubjectId)
            .ThenBy(item => item.Order.OccurredAtUtc)
            .ThenBy(item => item.OrderId)
            .ThenBy(item => item.ProductId)
            .Select(item => new
            {
                item.OrderId,
                item.Order.SubjectId,
                item.Order.OccurredAtUtc,
                Item = new ModelOrderInteractionItem(
                    item.ProductId,
                    item.Quantity)
            })
            .ToArrayAsync(cancellationToken);
        if (items.Any(item =>
                !RecommendationSubjectIdContract.IsValid(item.SubjectId)))
        {
            throw new InvalidOperationException(
                "Evaluation requires a completed SubjectId backfill.");
        }

        var interactions = items
            .GroupBy(item => new
            {
                item.OrderId,
                item.SubjectId,
                item.OccurredAtUtc
            })
            .Select(group => new ModelEvaluationOrder(
                group.Key.OrderId,
                group.Key.SubjectId!,
                DateTime.SpecifyKind(
                    group.Key.OccurredAtUtc,
                    DateTimeKind.Utc),
                group.Select(item => item.Item).ToArray()))
            .ToArray();
        return new ModelEvaluationSnapshot(
            products,
            products.Select(product => product.ProductId).ToArray(),
            products
                .Where(product => product.IsActive && product.IsInStock)
                .Select(product => product.ProductId)
                .ToArray(),
            interactions);
    }
}
