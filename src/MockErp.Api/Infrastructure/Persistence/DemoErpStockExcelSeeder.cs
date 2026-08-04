using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Infrastructure.Persistence;

public sealed class DemoErpStockExcelSeeder(
    MockErpDbContext context,
    TimeProvider timeProvider)
{
    private static readonly string[] ProductHeaders =
    [
        "Id",
        "Sku",
        "Name",
        "CategoryId",
        "BrandId",
        "Price",
        "VatRate",
        "NetContent",
        "UnitType",
        "InitialStock"
    ];

    public async Task<DemoErpStockSeedResult> SeedAsync(
        string workbookPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        var products = ReadAndValidateProducts(workbookPath);

        if (!context.Database.IsSqlServer())
        {
            throw new InvalidOperationException(
                "The demo ERP stock seeder requires the SQL Server provider.");
        }

        var pendingMigrations = await context.Database
            .GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException(
                "Demo ERP stocks cannot be seeded while MockErpDb migrations are pending.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var existingStocks = await context.ErpStocks
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var existingByProductId = existingStocks.ToDictionary(
                stock => stock.ExternalProductId);
            var existingBySku = existingStocks.ToDictionary(
                stock => stock.Sku,
                StringComparer.OrdinalIgnoreCase);
            var existingProductCount = products.Count(product =>
                existingByProductId.ContainsKey(product.Id));

            if (existingProductCount != 0 && existingProductCount != products.Count)
            {
                throw Conflict(
                    "Only part of the workbook product set already exists. " +
                    "Partial demo ERP stock seeds are not resumed.");
            }

            if (existingProductCount == products.Count)
            {
                foreach (var product in products)
                {
                    EnsureMatchingStock(
                        existingByProductId[product.Id],
                        product);
                }

                await transaction.CommitAsync(cancellationToken);
                return new DemoErpStockSeedResult(products.Count, 0, true);
            }

            foreach (var product in products)
            {
                if (existingBySku.TryGetValue(product.Sku, out var existingStock))
                {
                    throw Conflict(
                        $"Workbook SKU '{product.Sku}' is already assigned to " +
                        $"external product '{existingStock.ExternalProductId}'.");
                }
            }

            var importTimestampUtc = TruncateToMilliseconds(
                timeProvider.GetUtcNow().UtcDateTime);
            foreach (var product in products)
            {
                context.ErpStocks.Add(new ErpStock
                {
                    Id = Guid.NewGuid(),
                    ExternalProductId = product.Id,
                    Sku = product.Sku,
                    ProductName = product.Name,
                    UnitType = product.UnitType,
                    NetContent = product.NetContent,
                    Quantity = product.InitialStock,
                    ReorderLevel = 0,
                    UpdatedAtUtc = importTimestampUtc
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new DemoErpStockSeedResult(
                products.Count,
                products.Count,
                false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static IReadOnlyList<DemoErpProductRow> ReadAndValidateProducts(
        string workbookPath)
    {
        var fullPath = Path.GetFullPath(workbookPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The demo ERP workbook was not found.",
                fullPath);
        }

        using var workbook = new XLWorkbook(fullPath);
        if (!workbook.TryGetWorksheet("Products", out var worksheet))
        {
            throw InvalidWorkbook("Required worksheet 'Products' is missing.");
        }

        ValidateHeaders(worksheet);

        var rows = worksheet.RowsUsed(XLCellsUsedOptions.Contents)
            .Where(row => row.RowNumber() > 1)
            .Select(ReadProduct)
            .ToArray();
        if (rows.Length == 0)
        {
            throw InvalidWorkbook("Worksheet 'Products' contains no products.");
        }

        EnsureUnique(rows, product => product.Id, "Products.Id");
        EnsureUnique(
            rows,
            product => product.Sku,
            "Products.Sku",
            StringComparer.OrdinalIgnoreCase);

        return rows;
    }

    private static void ValidateHeaders(IXLWorksheet worksheet)
    {
        var actualHeaders = worksheet.Row(1)
            .CellsUsed(XLCellsUsedOptions.Contents)
            .Select(cell => cell.GetString().Trim())
            .ToArray();

        if (!actualHeaders.SequenceEqual(ProductHeaders, StringComparer.Ordinal))
        {
            throw InvalidWorkbook(
                "Worksheet 'Products' headers must be exactly: " +
                string.Join(", ", ProductHeaders) + ".");
        }
    }

    private static DemoErpProductRow ReadProduct(IXLRow row)
    {
        var idText = CellText(row.Cell(1));
        if (!Guid.TryParse(idText, out var id) || id == Guid.Empty)
        {
            throw InvalidCell(row, 1, "Id", "a non-empty GUID");
        }

        var sku = RequiredText(row, 2, "Sku", 64);
        var name = RequiredText(row, 3, "Name", 200);
        var netContent = DecimalValue(row, 8, "NetContent");
        if (netContent <= 0)
        {
            throw InvalidCell(row, 8, "NetContent", "a positive decimal number");
        }

        var rawUnitType = Int32Value(row, 9, "UnitType");
        if (rawUnitType is < byte.MinValue or > byte.MaxValue
            || !Enum.IsDefined(typeof(UnitType), (byte)rawUnitType))
        {
            throw InvalidCell(
                row,
                9,
                "UnitType",
                "a released UnitType numeric value");
        }

        var initialStock = Int32Value(row, 10, "InitialStock");
        if (initialStock < 0)
        {
            throw InvalidCell(
                row,
                10,
                "InitialStock",
                "a non-negative 32-bit integer");
        }

        return new DemoErpProductRow(
            id,
            sku,
            name,
            (UnitType)rawUnitType,
            netContent,
            initialStock);
    }

    private static string RequiredText(
        IXLRow row,
        int column,
        string fieldName,
        int maximumLength)
    {
        var value = CellText(row.Cell(column));
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw InvalidCell(
                row,
                column,
                fieldName,
                $"a non-empty value of at most {maximumLength} characters");
        }

        return value;
    }

    private static decimal DecimalValue(
        IXLRow row,
        int column,
        string fieldName)
    {
        var cell = row.Cell(column);
        if (cell.TryGetValue<decimal>(out var value)
            || decimal.TryParse(
                CellText(cell),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value))
        {
            return value;
        }

        throw InvalidCell(row, column, fieldName, "a decimal number");
    }

    private static int Int32Value(
        IXLRow row,
        int column,
        string fieldName)
    {
        var cell = row.Cell(column);
        if (cell.TryGetValue<int>(out var value)
            || int.TryParse(
                CellText(cell),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return value;
        }

        throw InvalidCell(row, column, fieldName, "a 32-bit integer");
    }

    private static string CellText(IXLCell cell)
    {
        return cell.GetFormattedString(CultureInfo.InvariantCulture).Trim();
    }

    private static void EnsureUnique<TKey>(
        IEnumerable<DemoErpProductRow> products,
        Func<DemoErpProductRow, TKey> keySelector,
        string fieldName,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>(comparer);
        foreach (var product in products)
        {
            var key = keySelector(product);
            if (!seen.Add(key))
            {
                throw InvalidWorkbook(
                    $"Worksheet 'Products' contains duplicate {fieldName} value '{key}'.");
            }
        }
    }

    private static void EnsureMatchingStock(
        ErpStock existing,
        DemoErpProductRow expected)
    {
        if (!string.Equals(existing.Sku, expected.Sku, StringComparison.Ordinal)
            || !string.Equals(
                existing.ProductName,
                expected.Name,
                StringComparison.Ordinal)
            || existing.UnitType != expected.UnitType
            || existing.NetContent != expected.NetContent
            || existing.ReorderLevel != 0)
        {
            throw Conflict(
                $"Existing ERP stock for external product '{expected.Id}' " +
                "conflicts with the demo workbook.");
        }
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
        return new DateTime(
            utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }

    private static DemoErpStockSeedException InvalidCell(
        IXLRow row,
        int column,
        string fieldName,
        string expectation)
    {
        return InvalidWorkbook(
            $"Worksheet 'Products' cell {row.Cell(column).Address} " +
            $"({fieldName}) must contain {expectation}.");
    }

    private static DemoErpStockSeedException InvalidWorkbook(string message)
    {
        return new DemoErpStockSeedException(message);
    }

    private static DemoErpStockSeedException Conflict(string message)
    {
        return new DemoErpStockSeedException(message);
    }

    private sealed record DemoErpProductRow(
        Guid Id,
        string Sku,
        string Name,
        UnitType UnitType,
        decimal NetContent,
        int InitialStock);
}

public sealed record DemoErpStockSeedResult(
    int ProductCount,
    int InsertedCount,
    bool AlreadySeeded);

public sealed class DemoErpStockSeedException(string message)
    : Exception(message);
