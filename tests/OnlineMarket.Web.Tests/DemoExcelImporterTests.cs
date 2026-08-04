using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Importing;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class DemoExcelImporterTests
{
    private const string TestPassword = "DemoCustomer!1Aa";
    private readonly OnlineMarketSqlServerFixture fixture;

    public DemoExcelImporterTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task WorkbookSheetsAndHeadersAreValidated()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var provider = CreateServiceProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
        var missingSheetPath = CopyCanonicalWorkbook(workbook =>
            workbook.Worksheet("Brands").Delete());
        var invalidHeaderPath = CopyCanonicalWorkbook(workbook =>
            workbook.Worksheet("Products").Cell(1, 2).Value = "InvalidSkuHeader");

        try
        {
            var missingSheet = await Assert.ThrowsAsync<DemoExcelImportException>(() =>
                importer.ImportAsync(missingSheetPath, TestPassword, true));
            Assert.Contains("Brands", missingSheet.Message, StringComparison.Ordinal);

            var invalidHeader = await Assert.ThrowsAsync<DemoExcelImportException>(() =>
                importer.ImportAsync(invalidHeaderPath, TestPassword, true));
            Assert.Contains("headers are invalid", invalidHeader.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(missingSheetPath);
            File.Delete(invalidHeaderPath);
        }
    }

    [Fact]
    public async Task InvalidReferencesFailBeforePersistence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var provider = CreateServiceProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
        var workbookPath = CopyCanonicalWorkbook(workbook =>
            workbook.Worksheet("OrderItems").Cell(2, 2).Value = Guid.NewGuid().ToString());

        try
        {
            var exception = await Assert.ThrowsAsync<DemoExcelImportException>(() =>
                importer.ImportAsync(workbookPath, TestPassword, true));
            Assert.Contains("unknown product", exception.Message, StringComparison.Ordinal);
            await AssertNoImportedRowsAsync(scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>());
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    [Fact]
    public async Task MoneyMismatchFailsBeforePersistence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var provider = CreateServiceProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
        var workbookPath = CopyCanonicalWorkbook(workbook =>
        {
            var cell = workbook.Worksheet("OrderItems").Cell(2, 8);
            cell.Value = cell.GetValue<decimal>() + 0.01m;
        });

        try
        {
            var exception = await Assert.ThrowsAsync<DemoExcelImportException>(() =>
                importer.ImportAsync(workbookPath, TestPassword, true));
            Assert.Contains("money values", exception.Message, StringComparison.Ordinal);
            await AssertNoImportedRowsAsync(scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>());
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    [Fact]
    public async Task CanonicalWorkbookImportsApprovedModelAndSecondRunIsIdempotent()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        DemoExcelImportResult firstResult;
        await using (var provider = CreateServiceProvider(database.ConnectionString))
        await using (var scope = provider.CreateAsyncScope())
        {
            var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
            firstResult = await importer.ImportAsync(
                GetCanonicalWorkbookPath(),
                TestPassword,
                emitHistoricalErpEvents: true);
        }

        Assert.False(firstResult.AlreadyImported);
        Assert.Equal(32, firstResult.Categories);
        Assert.Equal(20, firstResult.Brands);
        Assert.Equal(205, firstResult.Products);
        Assert.Equal(110, firstResult.Customers);
        Assert.Equal(110, firstResult.CustomerAddresses);
        Assert.Equal(502, firstResult.Orders);
        Assert.Equal(1505, firstResult.OrderItems);
        Assert.Equal(205, firstResult.ProductOutboxMessages);
        Assert.Equal(502, firstResult.RecommendationOutboxMessages);
        Assert.Equal(502, firstResult.ErpOutboxMessages);

        await using (var verification = database.CreateContext())
        {
            Assert.Equal(32, await verification.Categories.CountAsync());
            Assert.Equal(20, await verification.Brands.CountAsync());
            Assert.Equal(205, await verification.Products.CountAsync());
            Assert.Equal(205, await verification.Stocks.CountAsync());
            Assert.Equal(110, await verification.Users.CountAsync());
            Assert.Equal(110, await verification.Customers.CountAsync());
            Assert.Equal(110, await verification.CustomerAddresses.CountAsync());
            Assert.Equal(502, await verification.Carts.CountAsync());
            Assert.Equal(1505, await verification.CartItems.CountAsync());
            Assert.Equal(502, await verification.Orders.CountAsync());
            Assert.Equal(502, await verification.OrderAddresses.CountAsync());
            Assert.Equal(1505, await verification.OrderItems.CountAsync());
            Assert.Equal(502, await verification.Payments.CountAsync());

            var customerRoleId = await verification.Roles
                .Where(role => role.Name == "Customer")
                .Select(role => role.Id)
                .SingleAsync();
            Assert.Equal(
                110,
                await verification.UserRoles.CountAsync(row => row.RoleId == customerRoleId));
            Assert.All(
                await verification.Payments.ToListAsync(),
                payment =>
                {
                    Assert.Equal(PaymentMethod.CashSimulation, payment.Method);
                    Assert.Equal(PaymentStatus.Succeeded, payment.Status);
                });
            Assert.All(
                await verification.Carts.ToListAsync(),
                cart => Assert.Equal(CartStatus.Converted, cart.Status));

            await AssertFinalStockAsync(verification);
            await AssertSaleMovementsReferenceOrdersAsync(verification);
            await AssertApprovedOutboxAsync(verification);
        }

        var countsBeforeSecondRun = await ReadImportCountsAsync(database);
        DemoExcelImportResult secondResult;
        await using (var provider = CreateServiceProvider(database.ConnectionString))
        await using (var scope = provider.CreateAsyncScope())
        {
            secondResult = await scope.ServiceProvider
                .GetRequiredService<DemoExcelImporter>()
                .ImportAsync(GetCanonicalWorkbookPath(), TestPassword, true);
        }

        Assert.True(secondResult.AlreadyImported);
        Assert.Equal(countsBeforeSecondRun, await ReadImportCountsAsync(database));
    }

    [Fact]
    public async Task PersistenceFailureRollsBackIdentityBusinessAndOutboxRows()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [TR_OutboxMessages_ForceDemoImportFailure]
                ON [OutboxMessages]
                INSTEAD OF INSERT
                AS
                BEGIN
                    THROW 51000, 'Forced demo import failure.', 1;
                END
                """);
        }

        var workbookPath = CreateMinimalValidWorkbook();
        try
        {
            await using var provider = CreateServiceProvider(database.ConnectionString);
            await using var scope = provider.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                importer.ImportAsync(workbookPath, TestPassword, true));

            await using var verification = database.CreateContext();
            await AssertNoImportedRowsAsync(verification);
            Assert.Equal(0, await verification.Users.CountAsync());
            Assert.Equal(0, await verification.Roles.CountAsync());
            Assert.Equal(0, await verification.UserRoles.CountAsync());
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    [Fact]
    public async Task ConflictingCatalogueDataIsRefusedWithoutReset()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var workbookPath = CreateMinimalValidWorkbook();
        Guid categoryId;
        using (var workbook = new XLWorkbook(workbookPath))
        {
            categoryId = Guid.Parse(workbook.Worksheet("Categories").Cell(2, 1).GetString());
        }

        await using (var setup = database.CreateContext())
        {
            var now = DateTime.UtcNow;
            setup.Categories.Add(new Category
            {
                Id = categoryId,
                Name = "Conflicting category",
                Slug = "category",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var provider = CreateServiceProvider(database.ConnectionString);
            await using var scope = provider.CreateAsyncScope();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<DemoExcelImporter>()
                    .ImportAsync(workbookPath, TestPassword, true));
            Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);

            await using var verification = database.CreateContext();
            Assert.Equal("Conflicting category", await verification.Categories
                .Where(category => category.Id == categoryId)
                .Select(category => category.Name)
                .SingleAsync());
            Assert.Equal(0, await verification.Orders.CountAsync());
            Assert.Equal(0, await verification.OutboxMessages.CountAsync());
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    [Fact]
    public async Task ExistingMigrationsRemainAlignedWithTheCurrentModel()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private static async Task AssertFinalStockAsync(OnlineMarketDbContext context)
    {
        var expected = ReadExpectedFinalStock();
        var actual = await context.Stocks
            .AsNoTracking()
            .ToDictionaryAsync(stock => stock.ProductId, stock => stock.Quantity);
        Assert.Equal(expected.Count, actual.Count);
        Assert.All(expected, pair => Assert.Equal(pair.Value, actual[pair.Key]));
    }

    private static async Task AssertSaleMovementsReferenceOrdersAsync(
        OnlineMarketDbContext context)
    {
        var orderIds = await context.Orders.Select(order => order.Id).ToHashSetAsync();
        var sales = await context.StockMovements
            .Where(movement => movement.MovementType == StockMovementType.Sale)
            .ToListAsync();
        Assert.Equal(1505, sales.Count);
        Assert.All(sales, movement =>
        {
            Assert.Equal(StockReferenceType.Order, movement.ReferenceType);
            Assert.NotNull(movement.ReferenceId);
            Assert.Contains(movement.ReferenceId!.Value, orderIds);
        });
    }

    private static async Task AssertApprovedOutboxAsync(OnlineMarketDbContext context)
    {
        var productMessages = await context.OutboxMessages
            .Where(message => message.EventType == nameof(ProductSnapshotChangedV1))
            .OrderBy(message => message.Id)
            .ToListAsync();
        var recommendationMessages = await context.OutboxMessages
            .Where(message => message.EventType == nameof(OrderConfirmedForRecommendationV1))
            .OrderBy(message => message.Id)
            .ToListAsync();
        var erpMessages = await context.OutboxMessages
            .Where(message => message.EventType == nameof(OrderReadyForErpV1))
            .OrderBy(message => message.Id)
            .ToListAsync();

        Assert.Equal(205, productMessages.Count);
        Assert.Equal(502, recommendationMessages.Count);
        Assert.Equal(502, erpMessages.Count);
        Assert.True(productMessages.Max(message => message.Id) < recommendationMessages.Min(message => message.Id));
        Assert.Equal(
            recommendationMessages.Select(message => message.OccurredAtUtc).Order().ToArray(),
            recommendationMessages.Select(message => message.OccurredAtUtc).ToArray());

        foreach (var message in recommendationMessages)
        {
            using var document = JsonDocument.Parse(message.Payload);
            var root = document.RootElement;
            Assert.Equal(
                ["CorrelationId", "CustomerId", "EventId", "Items", "OccurredAtUtc", "OrderId", "OrderNumber"],
                root.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.All(root.GetProperty("Items").EnumerateArray(), item =>
                Assert.Equal(
                    ["ProductId", "Quantity"],
                    item.EnumerateObject().Select(property => property.Name).Order().ToArray()));
            string[] forbidden =
                ["Email", "Address", "Payment", "Price", "Vat", "Subtotal", "GrandTotal"];
            Assert.All(forbidden, term =>
                Assert.DoesNotContain(term, message.Payload, StringComparison.OrdinalIgnoreCase));
        }

        var erpMessage = erpMessages[0];
        using var erpDocument = JsonDocument.Parse(erpMessage.Payload);
        var erpRoot = erpDocument.RootElement;
        Assert.Equal(
            [
                "Address", "CorrelationId", "Customer", "EventId", "Items", "OccurredAtUtc",
                "OrderId", "OrderNumber", "OrderPlacedAtUtc", "PaymentMethod", "Totals"
            ],
            erpRoot.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal((byte)PaymentMethod.CashSimulation, erpRoot.GetProperty("PaymentMethod").GetByte());
        Assert.Equal(
            ["CustomerId", "Email", "FirstName", "LastName"],
            erpRoot.GetProperty("Customer").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            [
                "AddressLine1", "AddressLine2", "City", "CountryCode", "District", "PhoneNumber",
                "PostalCode", "RecipientName"
            ],
            erpRoot.GetProperty("Address").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["Currency", "GrandTotal", "Subtotal", "VatTotal"],
            erpRoot.GetProperty("Totals").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        Assert.All(erpRoot.GetProperty("Items").EnumerateArray(), item =>
            Assert.Equal(
                [
                    "LineTotal", "NetLineAmount", "ProductId", "ProductName", "Quantity", "Sku",
                    "UnitPrice", "VatAmount", "VatRate"
                ],
                item.EnumerateObject().Select(property => property.Name).Order().ToArray()));

        var orderId = erpRoot.GetProperty("OrderId").GetGuid();
        var order = await context.Orders
            .Include(candidate => candidate.AddressSnapshot)
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.Customer)
                .ThenInclude(customer => customer!.User)
            .SingleAsync(candidate => candidate.Id == orderId);
        Assert.Equal(order.OrderNumber, erpRoot.GetProperty("OrderNumber").GetString());
        Assert.Equal(order.PlacedAtUtc, erpRoot.GetProperty("OrderPlacedAtUtc").GetDateTime());
        Assert.Equal(order.Subtotal, erpRoot.GetProperty("Totals").GetProperty("Subtotal").GetDecimal());
        Assert.Equal(order.VatTotal, erpRoot.GetProperty("Totals").GetProperty("VatTotal").GetDecimal());
        Assert.Equal(order.GrandTotal, erpRoot.GetProperty("Totals").GetProperty("GrandTotal").GetDecimal());
        Assert.Equal(order.Customer!.User!.Email, erpRoot.GetProperty("Customer").GetProperty("Email").GetString());
        Assert.Equal(order.AddressSnapshot!.PhoneNumber, erpRoot.GetProperty("Address").GetProperty("PhoneNumber").GetString());
        Assert.Equal(order.Items.Count, erpRoot.GetProperty("Items").GetArrayLength());

        string[] forbiddenSecrets =
            ["CardNumber", "Cvv", "Expiry", "PaymentToken", "ProviderSecret"];
        Assert.All(forbiddenSecrets, term =>
            Assert.DoesNotContain(term, erpMessage.Payload, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<Guid, int> ReadExpectedFinalStock()
    {
        using var workbook = new XLWorkbook(GetCanonicalWorkbookPath());
        var products = workbook.Worksheet("Products");
        var expected = new Dictionary<Guid, int>();
        for (var row = 2; row <= products.LastRowUsed()!.RowNumber(); row++)
        {
            expected[Guid.Parse(products.Cell(row, 1).GetString())] =
                products.Cell(row, 10).GetValue<int>();
        }

        var items = workbook.Worksheet("OrderItems");
        for (var row = 2; row <= items.LastRowUsed()!.RowNumber(); row++)
        {
            var productId = Guid.Parse(items.Cell(row, 2).GetString());
            expected[productId] -= items.Cell(row, 5).GetValue<int>();
        }

        return expected;
    }

    private static async Task AssertNoImportedRowsAsync(OnlineMarketDbContext context)
    {
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Categories.CountAsync());
        Assert.Equal(0, await context.Brands.CountAsync());
        Assert.Equal(0, await context.Products.CountAsync());
        Assert.Equal(0, await context.Customers.CountAsync());
        Assert.Equal(0, await context.Orders.CountAsync());
        Assert.Equal(0, await context.StockMovements.CountAsync());
        Assert.Equal(0, await context.OutboxMessages.CountAsync());
    }

    private static async Task<(int Users, int Customers, int Orders, int OrderItems, int Movements, int Outbox)>
        ReadImportCountsAsync(OnlineMarketTestDatabase database)
    {
        await using var context = database.CreateContext();
        return (
            await context.Users.CountAsync(),
            await context.Customers.CountAsync(),
            await context.Orders.CountAsync(),
            await context.OrderItems.CountAsync(),
            await context.StockMovements.CountAsync(),
            await context.OutboxMessages.CountAsync());
    }

    private static ServiceProvider CreateServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OnlineMarketDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<OnlineMarketDbContext>();
        services.AddScoped<DemoExcelImporter>();
        return services.BuildServiceProvider();
    }

    private static string GetCanonicalWorkbookPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "demo_erp_veritabani.xlsx");
        Assert.True(File.Exists(path));
        return path;
    }

    private static string CopyCanonicalWorkbook(Action<XLWorkbook> mutate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"demo-import-{Guid.NewGuid():N}.xlsx");
        File.Copy(GetCanonicalWorkbookPath(), path);
        using var workbook = new XLWorkbook(path);
        mutate(workbook);
        workbook.Save();
        return path;
    }

    private static string CreateMinimalValidWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"demo-import-{Guid.NewGuid():N}.xlsx");
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var placedAtUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        using var workbook = new XLWorkbook();
        AddSheet(
            workbook,
            "Categories",
            ["Id", "ParentCategoryId", "Name", "Slug"],
            [categoryId, null, "Category", "category"]);
        AddSheet(
            workbook,
            "Brands",
            ["Id", "Name", "Slug"],
            [brandId, "Brand", "brand"]);
        AddSheet(
            workbook,
            "Products",
            ["Id", "Sku", "Name", "CategoryId", "BrandId", "Price", "VatRate", "NetContent", "UnitType", "InitialStock"],
            [productId, "SKU-1", "Product", categoryId, brandId, 10m, 20m, 1m, 1, 10]);
        AddSheet(
            workbook,
            "Customers",
            ["Id", "FirstName", "LastName", "Email", "Persona"],
            [customerId, "Demo", "Customer", "demo@example.test", "Test"]);
        AddSheet(
            workbook,
            "CustomerAddresses",
            ["Id", "CustomerId", "RecipientName", "PhoneNumber", "AddressLine1", "District", "City", "PostalCode", "CountryCode", "IsDefault"],
            [addressId, customerId, "Demo Customer", "5550000000", "Address 1", "District", "City", "34000", "TR", 1]);
        AddSheet(
            workbook,
            "Orders",
            ["Id", "OrderNumber", "CustomerId", "PlacedAtUtc", "Subtotal", "VatTotal", "GrandTotal"],
            [orderId, "DEMO-ORDER-1", customerId, placedAtUtc, 20m, 4m, 24m]);
        AddSheet(
            workbook,
            "OrderItems",
            ["OrderId", "ProductId", "SkuSnapshot", "ProductNameSnapshot", "Quantity", "UnitPrice", "VatRate", "NetLineAmount", "VatAmount", "LineTotal"],
            [orderId, productId, "SKU-1", "Product", 2, 10m, 20m, 20m, 4m, 24m]);
        workbook.SaveAs(path);
        return path;
    }

    private static void AddSheet(
        XLWorkbook workbook,
        string name,
        IReadOnlyList<string> headers,
        IReadOnlyList<object?> values)
    {
        var worksheet = workbook.Worksheets.Add(name);
        for (var column = 1; column <= headers.Count; column++)
        {
            worksheet.Cell(1, column).Value = headers[column - 1];
            var value = values[column - 1];
            if (value is not null)
            {
                worksheet.Cell(2, column).Value = XLCellValue.FromObject(value);
            }
        }
    }
}
