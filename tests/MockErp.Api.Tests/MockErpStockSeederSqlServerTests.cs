using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Infrastructure.Persistence;

namespace MockErp.Api.Tests;

[Collection(MockErpSqlServerCollection.CollectionName)]
public sealed class MockErpStockSeederSqlServerTests(
    MockErpSqlServerFixture fixture)
{
    [Fact]
    public async Task Canonical_stock_seed_is_repeatable_and_preserves_erp_balance()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var catalogPath = GetCanonicalCatalogPath();
        var expectedProducts = await ReadExpectedProductsAsync(catalogPath);
        var seeder = new MockErpStockSeeder(context);

        var firstInsertCount = await seeder.SeedAsync(catalogPath);
        context.ChangeTracker.Clear();

        var firstStocks = await context.ErpStocks
            .OrderBy(stock => stock.Sku)
            .ToArrayAsync();

        Assert.Equal(expectedProducts.Count, firstInsertCount);
        Assert.Equal(expectedProducts.Count, firstStocks.Length);
        Assert.Equal(
            firstStocks.Length,
            firstStocks.Select(stock => stock.ExternalProductId).Distinct().Count());
        Assert.Equal(
            firstStocks.Length,
            firstStocks.Select(stock => stock.Sku).Distinct(
                StringComparer.OrdinalIgnoreCase).Count());

        foreach (var stock in firstStocks)
        {
            var expected = expectedProducts[stock.ExternalProductId];
            Assert.Equal(expected.Sku, stock.Sku);
            Assert.Equal(expected.Name, stock.ProductName);
            Assert.Equal(expected.UnitType, (byte)stock.UnitType);
            Assert.Equal(expected.NetContent, stock.NetContent);
            Assert.Equal(expected.InitialStock, stock.Quantity);
            Assert.Equal(10, stock.ReorderLevel);
        }

        var independentlyChangedStock = firstStocks[0];
        independentlyChangedStock.Quantity--;
        var preservedQuantity = independentlyChangedStock.Quantity;
        context.ErpStocks.Update(independentlyChangedStock);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var secondInsertCount = await seeder.SeedAsync(catalogPath);
        context.ChangeTracker.Clear();

        Assert.Equal(0, secondInsertCount);
        Assert.Equal(
            expectedProducts.Count,
            await context.ErpStocks.CountAsync());
        Assert.Equal(
            preservedQuantity,
            await context.ErpStocks
                .Where(stock => stock.Id == independentlyChangedStock.Id)
                .Select(stock => stock.Quantity)
                .SingleAsync());
        Assert.Empty(await context.ErpCustomers.ToArrayAsync());
        Assert.Empty(await context.ErpOrders.ToArrayAsync());
        Assert.Empty(await context.ErpIdempotencyRecords.ToArrayAsync());
    }

    private static string GetCanonicalCatalogPath()
    {
        var path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../scripts/seed/catalog.v1.json"));

        Assert.True(
            File.Exists(path),
            $"Canonical catalogue was not found at '{path}'.");

        return path;
    }

    private static async Task<Dictionary<Guid, ExpectedProduct>>
        ReadExpectedProductsAsync(string catalogPath)
    {
        await using var stream = File.OpenRead(catalogPath);
        using var document = await JsonDocument.ParseAsync(stream);

        return document.RootElement
            .GetProperty("products")
            .EnumerateArray()
            .Select(product => new ExpectedProduct(
                product.GetProperty("id").GetGuid(),
                product.GetProperty("sku").GetString()!,
                product.GetProperty("name").GetString()!,
                product.GetProperty("unitType").GetByte(),
                product.GetProperty("netContent").GetDecimal(),
                product.GetProperty("initialStock").GetInt32()))
            .ToDictionary(product => product.Id);
    }

    private sealed record ExpectedProduct(
        Guid Id,
        string Sku,
        string Name,
        byte UnitType,
        decimal NetContent,
        int InitialStock);
}
