using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;
using MockErp.Api.Infrastructure.Persistence;

namespace MockErp.Api.Tests;

[Collection(MockErpSqlServerCollection.CollectionName)]
public sealed class DemoErpStockExcelSeederSqlServerTests(
    MockErpSqlServerFixture fixture)
{
    [Fact]
    public async Task Workbook_headers_are_validated_before_persistence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var path = CreateWorkbookCopy();

        try
        {
            UpdateWorkbook(path, workbook =>
                workbook.Worksheet("Products").Cell(1, 2).Value = "Code");

            var exception = await Assert.ThrowsAsync<DemoErpStockSeedException>(
                () => CreateSeeder(context).SeedAsync(path));

            Assert.Contains("headers", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await context.ErpStocks.CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_duplicate_and_negative_products_fail_before_persistence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var paths = new[]
        {
            CreateWorkbookCopy(),
            CreateWorkbookCopy(),
            CreateWorkbookCopy()
        };

        try
        {
            UpdateWorkbook(paths[0], workbook =>
                workbook.Worksheet("Products").Cell(2, 1).Clear());
            UpdateWorkbook(paths[1], workbook =>
            {
                var products = workbook.Worksheet("Products");
                products.Cell(3, 1).Value = products.Cell(2, 1).Value;
            });
            UpdateWorkbook(paths[2], workbook =>
                workbook.Worksheet("Products").Cell(2, 10).Value = -1);

            foreach (var path in paths)
            {
                await Assert.ThrowsAsync<DemoErpStockSeedException>(
                    () => CreateSeeder(context).SeedAsync(path));
            }

            Assert.Equal(0, await context.ErpStocks.CountAsync());
        }
        finally
        {
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Workbook_products_seed_only_initial_erp_stocks()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var workbookPath = GetDemoWorkbookPath();
        var expectedProducts = ReadExpectedProducts(workbookPath);

        var result = await CreateSeeder(context).SeedAsync(workbookPath);
        context.ChangeTracker.Clear();

        var stocks = await context.ErpStocks
            .AsNoTracking()
            .ToArrayAsync();
        Assert.False(result.AlreadySeeded);
        Assert.Equal(expectedProducts.Count, result.ProductCount);
        Assert.Equal(expectedProducts.Count, result.InsertedCount);
        Assert.Equal(expectedProducts.Count, stocks.Length);
        Assert.Single(stocks.Select(stock => stock.UpdatedAtUtc).Distinct());

        foreach (var stock in stocks)
        {
            var expected = expectedProducts[stock.ExternalProductId];
            Assert.Equal(expected.Sku, stock.Sku);
            Assert.Equal(expected.Name, stock.ProductName);
            Assert.Equal(expected.UnitType, stock.UnitType);
            Assert.Equal(expected.NetContent, stock.NetContent);
            Assert.Equal(expected.InitialStock, stock.Quantity);
            Assert.Equal(0, stock.ReorderLevel);
        }

        Assert.Equal(0, await context.ErpCustomers.CountAsync());
        Assert.Equal(0, await context.ErpOrders.CountAsync());
        Assert.Equal(0, await context.ErpOrderAddresses.CountAsync());
        Assert.Equal(0, await context.ErpOrderLines.CountAsync());
        Assert.Equal(0, await context.ErpStockMovements.CountAsync());
        Assert.Equal(0, await context.ErpAccountingEntries.CountAsync());
        Assert.Equal(0, await context.ErpAccountingEntryLines.CountAsync());
        Assert.Equal(0, await context.ErpIdempotencyRecords.CountAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Second_run_is_non_destructive_after_legitimate_stock_change()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var workbookPath = GetDemoWorkbookPath();
        var seeder = CreateSeeder(context);

        var firstResult = await seeder.SeedAsync(workbookPath);
        context.ChangeTracker.Clear();

        var changedStock = await context.ErpStocks
            .OrderBy(stock => stock.Sku)
            .FirstAsync();
        changedStock.Quantity--;
        changedStock.UpdatedAtUtc = changedStock.UpdatedAtUtc.AddMinutes(1);
        var expectedQuantity = changedStock.Quantity;
        var expectedUpdatedAtUtc = changedStock.UpdatedAtUtc;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var secondResult = await seeder.SeedAsync(workbookPath);
        context.ChangeTracker.Clear();
        var preservedStock = await context.ErpStocks
            .AsNoTracking()
            .SingleAsync(stock => stock.Id == changedStock.Id);

        Assert.False(firstResult.AlreadySeeded);
        Assert.True(secondResult.AlreadySeeded);
        Assert.Equal(0, secondResult.InsertedCount);
        Assert.Equal(expectedQuantity, preservedStock.Quantity);
        Assert.Equal(expectedUpdatedAtUtc, preservedStock.UpdatedAtUtc);
    }

    [Fact]
    public async Task Existing_conflicting_stock_is_rejected_without_modification()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var workbookPath = GetDemoWorkbookPath();
        var expected = ReadExpectedProducts(workbookPath).Values.First();
        var existing = new ErpStock
        {
            Id = Guid.NewGuid(),
            ExternalProductId = Guid.NewGuid(),
            Sku = expected.Sku,
            ProductName = "Existing ERP product",
            UnitType = UnitType.Piece,
            NetContent = 1m,
            Quantity = 7,
            ReorderLevel = 3,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.ErpStocks.Add(existing);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<DemoErpStockSeedException>(
            () => CreateSeeder(context).SeedAsync(workbookPath));
        context.ChangeTracker.Clear();

        Assert.Contains(expected.Sku, exception.Message, StringComparison.Ordinal);
        var preserved = await context.ErpStocks.AsNoTracking().SingleAsync();
        Assert.Equal(existing.Id, preserved.Id);
        Assert.Equal(7, preserved.Quantity);
        Assert.Equal(3, preserved.ReorderLevel);
    }

    [Fact]
    public async Task Database_failure_rolls_back_all_stocks()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER [dbo].[TR_ErpStocks_RejectDemoSeed]
            ON [dbo].[ErpStocks]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted)
                    THROW 51000, 'Injected demo stock seed failure.', 1;
            END;
            """);

        await Assert.ThrowsAnyAsync<Exception>(
            () => CreateSeeder(context).SeedAsync(GetDemoWorkbookPath()));
        context.ChangeTracker.Clear();

        Assert.Equal(0, await context.ErpStocks.CountAsync());
        Assert.Equal(0, await context.ErpStockMovements.CountAsync());
    }

    [Fact]
    public async Task Pending_migrations_are_refused()
    {
        await using var database = await fixture.CreateDatabaseAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context).SeedAsync(GetDemoWorkbookPath()));

        Assert.Contains(
            "migrations are pending",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static DemoErpStockExcelSeeder CreateSeeder(
        MockErpDbContext context)
    {
        return new DemoErpStockExcelSeeder(context, TimeProvider.System);
    }

    private static string GetDemoWorkbookPath()
    {
        var path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../scripts/seed/demo_erp_veritabani.xlsx"));
        Assert.True(
            File.Exists(path),
            $"Demo ERP workbook was not found at '{path}'.");
        return path;
    }

    private static string CreateWorkbookCopy()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mock-erp-stock-seed-{Guid.NewGuid():N}.xlsx");
        File.Copy(GetDemoWorkbookPath(), path);
        return path;
    }

    private static void UpdateWorkbook(
        string path,
        Action<XLWorkbook> update)
    {
        using var workbook = new XLWorkbook(path);
        update(workbook);
        workbook.Save();
    }

    private static Dictionary<Guid, ExpectedProduct> ReadExpectedProducts(
        string workbookPath)
    {
        using var workbook = new XLWorkbook(workbookPath);
        return workbook.Worksheet("Products")
            .RowsUsed(XLCellsUsedOptions.Contents)
            .Where(row => row.RowNumber() > 1)
            .Select(row => new ExpectedProduct(
                Guid.Parse(row.Cell(1).GetString()),
                row.Cell(2).GetString(),
                row.Cell(3).GetString(),
                (UnitType)row.Cell(9).GetValue<int>(),
                row.Cell(8).GetValue<decimal>(),
                row.Cell(10).GetValue<int>()))
            .ToDictionary(product => product.Id);
    }

    private sealed record ExpectedProduct(
        Guid Id,
        string Sku,
        string Name,
        UnitType UnitType,
        decimal NetContent,
        int InitialStock);
}
