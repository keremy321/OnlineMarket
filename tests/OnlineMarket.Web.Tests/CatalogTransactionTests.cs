using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class CatalogTransactionTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public CatalogTransactionTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ProductUpdateAndSnapshotOutboxCommitAtomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(context);
            var current = await context.Products
                .Include(product => product.Category)
                .Include(product => product.Brand)
                .Include(product => product.Stock)
                .SingleAsync(product => product.Id == scenario.ProductId);
            var dto = ToDto(current) with
            {
                Name = "Updated product",
                Price = 75m,
                IsActive = false
            };
            var service = new CatalogService(
                context,
                new SqlServerStockMutationService(context));

            var updated = await service.UpdateProductAsync(scenario.ProductId, dto);

            Assert.NotNull(updated);
            Assert.Equal("Updated product", updated.Name);
            Assert.False(updated.IsActive);
        }

        await using var verification = database.CreateContext();
        var product = await verification.Products.SingleAsync(
            candidate => candidate.Id == scenario.ProductId);
        var message = await verification.OutboxMessages.SingleAsync();
        Assert.Equal("Updated product", product.Name);
        Assert.Equal(75m, product.Price);
        Assert.False(product.IsActive);
        Assert.Equal(nameof(ProductSnapshotChangedV1), message.EventType);

        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;
        Assert.Equal("Updated product", root.GetProperty("Name").GetString());
        Assert.Equal(
            product.Description,
            root.GetProperty("Description").GetString());
        Assert.Equal(75m, root.GetProperty("Price").GetDecimal());
        Assert.False(root.GetProperty("IsActive").GetBoolean());
        Assert.Equal(
            product.UpdatedAtUtc,
            root.GetProperty("SourceUpdatedAtUtc").GetDateTime());
    }

    [Fact]
    public async Task ProductTransactionFailureLeavesSourceAndOutboxUnchanged()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        string originalName;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(context);
            var original = await context.Products
                .Include(product => product.Category)
                .Include(product => product.Brand)
                .Include(product => product.Stock)
                .SingleAsync(product => product.Id == scenario.ProductId);
            originalName = original.Name;
            var conflictingSku = $"CONFLICT-{Guid.NewGuid():N}";
            context.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Sku = conflictingSku,
                Name = "Conflict",
                Slug = $"conflict-{Guid.NewGuid():N}",
                CategoryId = scenario.CategoryId,
                BrandId = scenario.BrandId,
                Price = 1m,
                VatRate = 1m,
                NetContent = 1m,
                UnitType = UnitType.Piece,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            original = await context.Products
                .Include(product => product.Category)
                .Include(product => product.Brand)
                .Include(product => product.Stock)
                .SingleAsync(product => product.Id == scenario.ProductId);
            var dto = ToDto(original) with
            {
                Name = "Must roll back",
                Sku = conflictingSku
            };
            var service = new CatalogService(
                context,
                new SqlServerStockMutationService(context));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.UpdateProductAsync(scenario.ProductId, dto));
        }

        await using var verification = database.CreateContext();
        Assert.Equal(
            originalName,
            await verification.Products
                .Where(product => product.Id == scenario.ProductId)
                .Select(product => product.Name)
                .SingleAsync());
        Assert.Equal(0, await verification.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task StockAvailabilityTransitionsEmitEventsButPositiveAdjustmentsDoNot()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 0);
            var service = new CatalogService(
                context,
                new SqlServerStockMutationService(context));

            Assert.True(await service.AdjustStockAsync(
                scenario.ProductId,
                1,
                "Available",
                scenario.UserId));
            Assert.True(await service.AdjustStockAsync(
                scenario.ProductId,
                1,
                "Still available",
                scenario.UserId));
            Assert.True(await service.AdjustStockAsync(
                scenario.ProductId,
                -2,
                "Unavailable",
                scenario.UserId));
        }

        await using var verification = database.CreateContext();
        Assert.Equal(0, await verification.Stocks
            .Where(stock => stock.ProductId == scenario.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
        Assert.Equal(3, await verification.StockMovements.CountAsync());
        var messages = await verification.OutboxMessages
            .OrderBy(message => message.Id)
            .ToListAsync();
        Assert.Equal(2, messages.Count);

        using var firstDocument = JsonDocument.Parse(messages[0].Payload);
        using var secondDocument = JsonDocument.Parse(messages[1].Payload);
        Assert.True(firstDocument.RootElement.GetProperty("IsInStock").GetBoolean());
        Assert.False(secondDocument.RootElement.GetProperty("IsInStock").GetBoolean());
        Assert.All(messages, message =>
            Assert.Equal(nameof(ProductSnapshotChangedV1), message.EventType));
    }

    [Fact]
    public async Task ProductCreateCommitsStockMovementAndSnapshotTogether()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        Guid createdProductId;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(context);
            var service = new CatalogService(
                context,
                new SqlServerStockMutationService(context));
            var dto = new ProductDto(
                Guid.Empty,
                $"NEW-{Guid.NewGuid():N}",
                "New product",
                $"new-product-{Guid.NewGuid():N}",
                null,
                scenario.CategoryId,
                string.Empty,
                scenario.BrandId,
                string.Empty,
                25m,
                10m,
                1m,
                UnitType.Piece,
                null,
                true,
                4,
                true);

            var created = await service.CreateProductAsync(dto, 4);
            createdProductId = created.Id;
        }

        await using var verification = database.CreateContext();
        Assert.True(await verification.Products.AnyAsync(
            product => product.Id == createdProductId));
        Assert.Equal(4, await verification.Stocks
            .Where(stock => stock.ProductId == createdProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
        Assert.Single(await verification.StockMovements
            .Where(movement => movement.ProductId == createdProductId)
            .ToListAsync());
        Assert.Single(await verification.OutboxMessages
            .Where(message => message.AggregateId == createdProductId)
            .ToListAsync());
    }

    private static ProductDto ToDto(Product product)
    {
        var stockQuantity = product.Stock?.Quantity ?? 0;
        return new ProductDto(
            product.Id,
            product.Sku,
            product.Name,
            product.Slug,
            product.Description,
            product.CategoryId,
            product.Category?.Name ?? string.Empty,
            product.BrandId,
            product.Brand?.Name ?? string.Empty,
            product.Price,
            product.VatRate,
            product.NetContent,
            product.UnitType,
            product.ImageUrl,
            product.IsActive,
            stockQuantity,
            stockQuantity > 0);
    }
}
