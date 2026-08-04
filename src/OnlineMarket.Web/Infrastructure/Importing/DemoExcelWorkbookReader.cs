using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Infrastructure.Importing;

internal sealed class DemoExcelWorkbookReader
{
    private static readonly string[] CategoryHeaders =
        ["Id", "ParentCategoryId", "Name", "Slug"];
    private static readonly string[] BrandHeaders =
        ["Id", "Name", "Slug"];
    private static readonly string[] ProductHeaders =
        ["Id", "Sku", "Name", "CategoryId", "BrandId", "Price", "VatRate", "NetContent", "UnitType", "InitialStock"];
    private static readonly string[] CustomerHeaders =
        ["Id", "FirstName", "LastName", "Email", "Persona"];
    private static readonly string[] CustomerAddressHeaders =
        ["Id", "CustomerId", "RecipientName", "PhoneNumber", "AddressLine1", "District", "City", "PostalCode", "CountryCode", "IsDefault"];
    private static readonly string[] OrderHeaders =
        ["Id", "OrderNumber", "CustomerId", "PlacedAtUtc", "Subtotal", "VatTotal", "GrandTotal"];
    private static readonly string[] OrderItemHeaders =
        ["OrderId", "ProductId", "SkuSnapshot", "ProductNameSnapshot", "Quantity", "UnitPrice", "VatRate", "NetLineAmount", "VatAmount", "LineTotal"];

    public DemoImportDocument Read(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException("The demo Excel workbook was not found.", workbookPath);
        }

        using var workbook = new XLWorkbook(workbookPath);

        var categories = ReadRows(
            GetRequiredWorksheet(workbook, "Categories", CategoryHeaders),
            CategoryHeaders,
            row => new DemoCategoryRow(
                ReadGuid(row, 1, "Id"),
                ReadNullableGuid(row, 2, "ParentCategoryId"),
                ReadRequiredText(row, 3, "Name", 150),
                ReadRequiredText(row, 4, "Slug", 180)));

        var brands = ReadRows(
            GetRequiredWorksheet(workbook, "Brands", BrandHeaders),
            BrandHeaders,
            row => new DemoBrandRow(
                ReadGuid(row, 1, "Id"),
                ReadRequiredText(row, 2, "Name", 150),
                ReadRequiredText(row, 3, "Slug", 180)));

        var rawProducts = ReadRows(
            GetRequiredWorksheet(workbook, "Products", ProductHeaders),
            ProductHeaders,
            row => new RawProductRow(
                ReadGuid(row, 1, "Id"),
                ReadRequiredText(row, 2, "Sku", 64),
                ReadRequiredText(row, 3, "Name", 200),
                ReadGuid(row, 4, "CategoryId"),
                ReadGuid(row, 5, "BrandId"),
                ReadDecimal(row, 6, "Price"),
                ReadDecimal(row, 7, "VatRate"),
                ReadDecimal(row, 8, "NetContent"),
                ReadUnitType(row, 9),
                ReadInt32(row, 10, "InitialStock")));
        var products = AddDeterministicProductSlugs(rawProducts);

        var customers = ReadRows(
            GetRequiredWorksheet(workbook, "Customers", CustomerHeaders),
            CustomerHeaders,
            row => new DemoCustomerRow(
                ReadGuid(row, 1, "Id"),
                ReadRequiredText(row, 2, "FirstName", 100),
                ReadRequiredText(row, 3, "LastName", 100),
                ReadRequiredText(row, 4, "Email", 256),
                ReadRequiredText(row, 5, "Persona", 200)));

        var customerAddresses = ReadRows(
            GetRequiredWorksheet(workbook, "CustomerAddresses", CustomerAddressHeaders),
            CustomerAddressHeaders,
            row => new DemoCustomerAddressRow(
                ReadGuid(row, 1, "Id"),
                ReadGuid(row, 2, "CustomerId"),
                ReadRequiredText(row, 3, "RecipientName", 200),
                ReadRequiredText(row, 4, "PhoneNumber", 30),
                ReadRequiredText(row, 5, "AddressLine1", 250),
                ReadRequiredText(row, 6, "District", 100),
                ReadRequiredText(row, 7, "City", 100),
                ReadOptionalText(row, 8, "PostalCode", 20),
                ReadRequiredText(row, 9, "CountryCode", 2).ToUpperInvariant(),
                ReadBoolean(row, 10, "IsDefault")));

        var orders = ReadRows(
            GetRequiredWorksheet(workbook, "Orders", OrderHeaders),
            OrderHeaders,
            row => new DemoOrderRow(
                ReadGuid(row, 1, "Id"),
                ReadRequiredText(row, 2, "OrderNumber", 32),
                ReadGuid(row, 3, "CustomerId"),
                ReadUtcDateTime(row, 4, "PlacedAtUtc"),
                ReadDecimal(row, 5, "Subtotal"),
                ReadDecimal(row, 6, "VatTotal"),
                ReadDecimal(row, 7, "GrandTotal")));

        var orderItems = ReadRows(
            GetRequiredWorksheet(workbook, "OrderItems", OrderItemHeaders),
            OrderItemHeaders,
            row => new DemoOrderItemRow(
                ReadGuid(row, 1, "OrderId"),
                ReadGuid(row, 2, "ProductId"),
                ReadRequiredText(row, 3, "SkuSnapshot", 64),
                ReadRequiredText(row, 4, "ProductNameSnapshot", 200),
                ReadInt32(row, 5, "Quantity"),
                ReadDecimal(row, 6, "UnitPrice"),
                ReadDecimal(row, 7, "VatRate"),
                ReadDecimal(row, 8, "NetLineAmount"),
                ReadDecimal(row, 9, "VatAmount"),
                ReadDecimal(row, 10, "LineTotal")));

        return ValidateAndCreateDocument(
            categories,
            brands,
            products,
            customers,
            customerAddresses,
            orders,
            orderItems);
    }

    private static IXLWorksheet GetRequiredWorksheet(
        XLWorkbook workbook,
        string worksheetName,
        IReadOnlyList<string> expectedHeaders)
    {
        if (!workbook.TryGetWorksheet(worksheetName, out var worksheet))
        {
            throw new DemoExcelImportException(
                $"Workbook is missing required worksheet '{worksheetName}'.");
        }

        ValidateHeaders(worksheet, expectedHeaders);
        return worksheet;
    }

    private static void ValidateHeaders(
        IXLWorksheet worksheet,
        IReadOnlyList<string> expectedHeaders)
    {
        var actualHeaders = new List<string>();
        var lastCell = worksheet.Row(1).LastCellUsed(XLCellsUsedOptions.Contents);
        var lastColumn = lastCell?.Address.ColumnNumber ?? 0;

        for (var column = 1; column <= lastColumn; column++)
        {
            actualHeaders.Add(worksheet.Cell(1, column).GetString().Trim());
        }

        if (!actualHeaders.SequenceEqual(expectedHeaders, StringComparer.Ordinal))
        {
            throw new DemoExcelImportException(
                $"Worksheet '{worksheet.Name}' headers are invalid. Expected: "
                + $"{string.Join(", ", expectedHeaders)}. Actual: {string.Join(", ", actualHeaders)}.");
        }
    }

    private static List<T> ReadRows<T>(
        IXLWorksheet worksheet,
        IReadOnlyList<string> headers,
        Func<IXLRow, T> map)
    {
        var rows = new List<T>();
        var lastRowNumber = worksheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;

        for (var rowNumber = 2; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.Cells(1, headers.Count).All(cell => cell.IsEmpty()))
            {
                continue;
            }

            try
            {
                rows.Add(map(row));
            }
            catch (DemoExcelImportException)
            {
                throw;
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException)
            {
                throw new DemoExcelImportException(
                    $"Worksheet '{worksheet.Name}' row {rowNumber} contains an invalid value: {exception.Message}");
            }
        }

        if (rows.Count == 0)
        {
            throw new DemoExcelImportException(
                $"Worksheet '{worksheet.Name}' must contain at least one data row.");
        }

        return rows;
    }

    private static DemoImportDocument ValidateAndCreateDocument(
        IReadOnlyList<DemoCategoryRow> categories,
        IReadOnlyList<DemoBrandRow> brands,
        IReadOnlyList<DemoProductRow> products,
        IReadOnlyList<DemoCustomerRow> customers,
        IReadOnlyList<DemoCustomerAddressRow> customerAddresses,
        IReadOnlyList<DemoOrderRow> orders,
        IReadOnlyList<DemoOrderItemRow> orderItems)
    {
        EnsureUnique(categories, row => row.Id, "Categories.Id");
        EnsureUnique(categories, row => row.Slug, "Categories.Slug", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(brands, row => row.Id, "Brands.Id");
        EnsureUnique(brands, row => row.Name, "Brands.Name", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(brands, row => row.Slug, "Brands.Slug", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(products, row => row.Id, "Products.Id");
        EnsureUnique(products, row => row.Sku, "Products.Sku", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(products, row => row.Slug, "derived Products.Slug", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(customers, row => row.Id, "Customers.Id");
        EnsureUnique(customers, row => row.Email, "Customers.Email", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(customerAddresses, row => row.Id, "CustomerAddresses.Id");
        EnsureUnique(orders, row => row.Id, "Orders.Id");
        EnsureUnique(orders, row => row.OrderNumber, "Orders.OrderNumber", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(
            orderItems,
            row => (row.OrderId, row.ProductId),
            "OrderItems.(OrderId, ProductId)");

        var categoryIds = categories.Select(row => row.Id).ToHashSet();
        foreach (var category in categories)
        {
            if (category.ParentCategoryId == category.Id)
            {
                throw new DemoExcelImportException(
                    $"Category '{category.Id}' cannot reference itself as its parent.");
            }

            if (category.ParentCategoryId.HasValue
                && !categoryIds.Contains(category.ParentCategoryId.Value))
            {
                throw new DemoExcelImportException(
                    $"Category '{category.Id}' references unknown parent '{category.ParentCategoryId}'.");
            }
        }

        EnsureCategoryGraphHasNoCycles(categories);

        var brandIds = brands.Select(row => row.Id).ToHashSet();
        foreach (var product in products)
        {
            if (!categoryIds.Contains(product.CategoryId))
            {
                throw new DemoExcelImportException(
                    $"Product '{product.Sku}' references unknown category '{product.CategoryId}'.");
            }

            if (!brandIds.Contains(product.BrandId))
            {
                throw new DemoExcelImportException(
                    $"Product '{product.Sku}' references unknown brand '{product.BrandId}'.");
            }

            if (product.Price < 0m)
            {
                throw new DemoExcelImportException($"Product '{product.Sku}' has a negative price.");
            }

            if (product.VatRate is < 0m or > 100m)
            {
                throw new DemoExcelImportException($"Product '{product.Sku}' has an invalid VAT rate.");
            }

            if (product.NetContent <= 0m)
            {
                throw new DemoExcelImportException($"Product '{product.Sku}' has non-positive net content.");
            }

            if (product.InitialStock < 0)
            {
                throw new DemoExcelImportException($"Product '{product.Sku}' has negative initial stock.");
            }
        }

        var customerIds = customers.Select(row => row.Id).ToHashSet();
        foreach (var customer in customers)
        {
            if (!customer.Email.Contains('@', StringComparison.Ordinal))
            {
                throw new DemoExcelImportException(
                    $"Customer '{customer.Id}' has an invalid email value.");
            }
        }

        foreach (var address in customerAddresses)
        {
            if (!customerIds.Contains(address.CustomerId))
            {
                throw new DemoExcelImportException(
                    $"Address '{address.Id}' references unknown customer '{address.CustomerId}'.");
            }
        }

        var addressGroups = customerAddresses.ToLookup(row => row.CustomerId);
        foreach (var customer in customers)
        {
            var addresses = addressGroups[customer.Id].ToArray();
            if (addresses.Length == 0)
            {
                throw new DemoExcelImportException(
                    $"Customer '{customer.Id}' has no address for historical order snapshots.");
            }

            if (addresses.Count(address => address.IsDefault) > 1)
            {
                throw new DemoExcelImportException(
                    $"Customer '{customer.Id}' has more than one default address.");
            }
        }

        var orderIds = orders.Select(row => row.Id).ToHashSet();
        foreach (var order in orders)
        {
            if (!customerIds.Contains(order.CustomerId))
            {
                throw new DemoExcelImportException(
                    $"Order '{order.OrderNumber}' references unknown customer '{order.CustomerId}'.");
            }

            if (order.Subtotal < 0m || order.VatTotal < 0m || order.GrandTotal < 0m)
            {
                throw new DemoExcelImportException(
                    $"Order '{order.OrderNumber}' has a negative money value.");
            }

            if (order.Subtotal + order.VatTotal != order.GrandTotal)
            {
                throw new DemoExcelImportException(
                    $"Order '{order.OrderNumber}' GrandTotal does not equal Subtotal plus VatTotal.");
            }
        }

        var productIds = products.Select(row => row.Id).ToHashSet();
        foreach (var item in orderItems)
        {
            if (!orderIds.Contains(item.OrderId))
            {
                throw new DemoExcelImportException(
                    $"Order item references unknown order '{item.OrderId}'.");
            }

            if (!productIds.Contains(item.ProductId))
            {
                throw new DemoExcelImportException(
                    $"Order item in order '{item.OrderId}' references unknown product '{item.ProductId}'.");
            }

            if (item.Quantity <= 0)
            {
                throw new DemoExcelImportException(
                    $"Order item '{item.OrderId}/{item.ProductId}' has non-positive quantity.");
            }

            if (item.UnitPrice < 0m || item.VatRate is < 0m or > 100m)
            {
                throw new DemoExcelImportException(
                    $"Order item '{item.OrderId}/{item.ProductId}' has invalid price or VAT.");
            }

            var expectedNet = RoundMoney(item.UnitPrice * item.Quantity);
            var expectedVat = RoundMoney(expectedNet * item.VatRate / 100m);
            var expectedTotal = expectedNet + expectedVat;
            if (item.NetLineAmount != expectedNet
                || item.VatAmount != expectedVat
                || item.LineTotal != expectedTotal)
            {
                throw new DemoExcelImportException(
                    $"Order item '{item.OrderId}/{item.ProductId}' money values do not match the approved rounding calculation.");
            }
        }

        var itemsByOrder = orderItems.ToLookup(row => row.OrderId);
        foreach (var order in orders)
        {
            var items = itemsByOrder[order.Id].ToArray();
            if (items.Length == 0)
            {
                throw new DemoExcelImportException(
                    $"Order '{order.OrderNumber}' has no order items.");
            }

            var expectedSubtotal = items.Sum(item => item.NetLineAmount);
            var expectedVat = items.Sum(item => item.VatAmount);
            var expectedGrandTotal = items.Sum(item => item.LineTotal);
            if (order.Subtotal != expectedSubtotal
                || order.VatTotal != expectedVat
                || order.GrandTotal != expectedGrandTotal)
            {
                throw new DemoExcelImportException(
                    $"Order '{order.OrderNumber}' totals do not equal its recalculated line totals.");
            }
        }

        var finalStock = products.ToDictionary(row => row.Id, row => row.InitialStock);
        foreach (var order in orders
                     .OrderBy(row => row.PlacedAtUtc)
                     .ThenBy(row => row.OrderNumber, StringComparer.Ordinal)
                     .ThenBy(row => row.Id))
        {
            foreach (var item in itemsByOrder[order.Id])
            {
                var newQuantity = finalStock[item.ProductId] - item.Quantity;
                if (newQuantity < 0)
                {
                    throw new DemoExcelImportException(
                        $"Historical order '{order.OrderNumber}' makes product '{item.ProductId}' stock negative.");
                }

                finalStock[item.ProductId] = newQuantity;
            }
        }

        var earliestOrderAtUtc = orders.Min(row => row.PlacedAtUtc);
        var catalogueOccurredAtUtc = earliestOrderAtUtc > DateTime.MinValue.AddMilliseconds(1)
            ? earliestOrderAtUtc.AddMilliseconds(-1)
            : earliestOrderAtUtc;

        return new DemoImportDocument(
            categories,
            brands,
            products,
            customers,
            customerAddresses,
            orders
                .OrderBy(row => row.PlacedAtUtc)
                .ThenBy(row => row.OrderNumber, StringComparer.Ordinal)
                .ThenBy(row => row.Id)
                .ToArray(),
            orderItems,
            finalStock,
            catalogueOccurredAtUtc);
    }

    private static IReadOnlyList<DemoProductRow> AddDeterministicProductSlugs(
        IReadOnlyList<RawProductRow> products)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DemoProductRow>(products.Count);

        foreach (var product in products)
        {
            var slug = GenerateSlug(product.Name);
            if (!slugs.Add(slug))
            {
                slug = $"{slug}-{product.Id:N}";
                if (!slugs.Add(slug))
                {
                    throw new DemoExcelImportException(
                        $"A unique slug could not be derived for product '{product.Sku}'.");
                }
            }

            if (slug.Length > 240)
            {
                var suffix = $"-{product.Id:N}";
                slug = slug[..(240 - suffix.Length)] + suffix;
            }

            result.Add(new DemoProductRow(
                product.Id,
                product.Sku,
                product.Name,
                slug,
                product.CategoryId,
                product.BrandId,
                product.Price,
                product.VatRate,
                product.NetContent,
                product.UnitType,
                product.InitialStock));
        }

        return result;
    }

    private static string GenerateSlug(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Replace('ı', 'i').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var appendSeparator = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                if (appendSeparator && builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(character);
                appendSeparator = false;
            }
            else
            {
                appendSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            throw new DemoExcelImportException(
                $"Product name '{value}' cannot be converted to a URL-safe slug.");
        }

        return slug;
    }

    private static void EnsureCategoryGraphHasNoCycles(IReadOnlyList<DemoCategoryRow> categories)
    {
        var parents = categories.ToDictionary(row => row.Id, row => row.ParentCategoryId);
        foreach (var category in categories)
        {
            var visited = new HashSet<Guid>();
            var current = category.Id;
            while (parents[current].HasValue)
            {
                if (!visited.Add(current))
                {
                    throw new DemoExcelImportException(
                        $"Category hierarchy contains a cycle involving '{category.Id}'.");
                }

                current = parents[current]!.Value;
            }
        }
    }

    private static void EnsureUnique<T, TKey>(
        IEnumerable<T> rows,
        Func<T, TKey> keySelector,
        string fieldName,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var values = new HashSet<TKey>(comparer);
        foreach (var row in rows)
        {
            var key = keySelector(row);
            if (!values.Add(key))
            {
                throw new DemoExcelImportException(
                    $"Workbook contains duplicate {fieldName} value '{key}'.");
            }
        }
    }

    private static Guid ReadGuid(IXLRow row, int column, string fieldName)
    {
        var text = ReadRequiredText(row, column, fieldName, 36);
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty)
        {
            throw InvalidCell(row, column, fieldName, "a non-empty GUID");
        }

        return value;
    }

    private static Guid? ReadNullableGuid(IXLRow row, int column, string fieldName)
    {
        var text = GetCellText(row.Cell(column));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!Guid.TryParse(text, out var value) || value == Guid.Empty)
        {
            throw InvalidCell(row, column, fieldName, "a GUID or blank");
        }

        return value;
    }

    private static string ReadRequiredText(
        IXLRow row,
        int column,
        string fieldName,
        int maximumLength)
    {
        var value = GetCellText(row.Cell(column));
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidCell(row, column, fieldName, "a non-empty value");
        }

        if (value.Length > maximumLength)
        {
            throw InvalidCell(row, column, fieldName, $"at most {maximumLength} characters");
        }

        return value;
    }

    private static string? ReadOptionalText(
        IXLRow row,
        int column,
        string fieldName,
        int maximumLength)
    {
        var value = GetCellText(row.Cell(column));
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > maximumLength)
        {
            throw InvalidCell(row, column, fieldName, $"at most {maximumLength} characters");
        }

        return value;
    }

    private static decimal ReadDecimal(IXLRow row, int column, string fieldName)
    {
        var cell = row.Cell(column);
        if (cell.TryGetValue<decimal>(out var value))
        {
            return value;
        }

        var text = GetCellText(cell);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        throw InvalidCell(row, column, fieldName, "a decimal number");
    }

    private static int ReadInt32(IXLRow row, int column, string fieldName)
    {
        var cell = row.Cell(column);
        if (cell.TryGetValue<int>(out var value))
        {
            return value;
        }

        var text = GetCellText(cell);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        throw InvalidCell(row, column, fieldName, "a 32-bit integer");
    }

    private static UnitType ReadUnitType(IXLRow row, int column)
    {
        var rawValue = ReadInt32(row, column, "UnitType");
        if (rawValue is < byte.MinValue or > byte.MaxValue
            || !Enum.IsDefined(typeof(UnitType), (byte)rawValue))
        {
            throw InvalidCell(row, column, "UnitType", "a released UnitType numeric value");
        }

        return (UnitType)rawValue;
    }

    private static bool ReadBoolean(IXLRow row, int column, string fieldName)
    {
        var cell = row.Cell(column);
        if (cell.TryGetValue<bool>(out var value))
        {
            return value;
        }

        var text = GetCellText(cell);
        if (text == "1")
        {
            return true;
        }

        if (text == "0")
        {
            return false;
        }

        if (bool.TryParse(text, out value))
        {
            return value;
        }

        throw InvalidCell(row, column, fieldName, "true, false, 1, or 0");
    }

    private static DateTime ReadUtcDateTime(IXLRow row, int column, string fieldName)
    {
        var cell = row.Cell(column);
        DateTime value;
        if (!cell.TryGetValue(out value))
        {
            var text = GetCellText(cell);
            if (!DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out value))
            {
                throw InvalidCell(row, column, fieldName, "a UTC date/time");
            }
        }

        value = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw InvalidCell(row, column, fieldName, "UTC time at millisecond precision");
        }

        return value;
    }

    private static string GetCellText(IXLCell cell)
    {
        return cell.GetFormattedString(CultureInfo.InvariantCulture).Trim();
    }

    private static DemoExcelImportException InvalidCell(
        IXLRow row,
        int column,
        string fieldName,
        string expectation)
    {
        return new DemoExcelImportException(
            $"Worksheet '{row.Worksheet.Name}' cell {row.Cell(column).Address} ({fieldName}) must contain {expectation}.");
    }

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record RawProductRow(
        Guid Id,
        string Sku,
        string Name,
        Guid CategoryId,
        Guid BrandId,
        decimal Price,
        decimal VatRate,
        decimal NetContent,
        UnitType UnitType,
        int InitialStock);
}
