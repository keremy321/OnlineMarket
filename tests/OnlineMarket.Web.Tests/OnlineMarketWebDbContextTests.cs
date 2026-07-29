using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class OnlineMarketWebDbContextTests
{
    private const string InitialMigration =
        "20260728145429_202607281800_InitialOnlineMarketSchema";

    private readonly OnlineMarketSqlServerFixture fixture;

    public OnlineMarketWebDbContextTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task AllMigrationsApplyToEmptyDatabaseAndCreateExpectedSchema()
    {
        await using var database = await fixture.CreateDatabaseAsync(applyMigrations: false);
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        var tables = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT [name]
            FROM sys.tables
            ORDER BY [name];
            """);

        string[] expectedTables =
        [
            "AspNetRoleClaims",
            "AspNetRoles",
            "AspNetUserClaims",
            "AspNetUserLogins",
            "AspNetUserRoles",
            "AspNetUsers",
            "AspNetUserTokens",
            "Brands",
            "CartItems",
            "Carts",
            "Categories",
            "CustomerAddresses",
            "Customers",
            "OrderAddresses",
            "OrderItems",
            "Orders",
            "OutboxMessages",
            "Payments",
            "Products",
            "StockMovements",
            "Stocks",
            "__EFMigrationsHistory"
        ];

        Assert.All(expectedTables, table => Assert.Contains(table, tables));

        var coveringIndexCount = await QueryScalarAsync<int>(
            database.ConnectionString,
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[Products]')
              AND [name] = N'IX_Products_CategoryId_IsActive_Name_Covering';
            """);
        var duplicateProductKeyCount = await QueryScalarAsync<int>(
            database.ConnectionString,
            """
            SELECT COUNT(DISTINCT i.[index_id])
            FROM sys.indexes i
            JOIN sys.index_columns ic1
              ON ic1.[object_id] = i.[object_id]
             AND ic1.[index_id] = i.[index_id]
             AND ic1.[key_ordinal] = 1
            JOIN sys.columns c1
              ON c1.[object_id] = ic1.[object_id]
             AND c1.[column_id] = ic1.[column_id]
            JOIN sys.index_columns ic2
              ON ic2.[object_id] = i.[object_id]
             AND ic2.[index_id] = i.[index_id]
             AND ic2.[key_ordinal] = 2
            JOIN sys.columns c2
              ON c2.[object_id] = ic2.[object_id]
             AND c2.[column_id] = ic2.[column_id]
            JOIN sys.index_columns ic3
              ON ic3.[object_id] = i.[object_id]
             AND ic3.[index_id] = i.[index_id]
             AND ic3.[key_ordinal] = 3
            JOIN sys.columns c3
              ON c3.[object_id] = ic3.[object_id]
             AND c3.[column_id] = ic3.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'[Products]')
              AND c1.[name] = N'CategoryId'
              AND c2.[name] = N'IsActive'
              AND c3.[name] = N'Name';
            """);
        var includedColumns = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT c.[name]
            FROM sys.indexes i
            JOIN sys.index_columns ic
              ON ic.[object_id] = i.[object_id]
             AND ic.[index_id] = i.[index_id]
            JOIN sys.columns c
              ON c.[object_id] = ic.[object_id]
             AND c.[column_id] = ic.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'[Products]')
              AND i.[name] = N'IX_Products_CategoryId_IsActive_Name_Covering'
              AND ic.[is_included_column] = 1
            ORDER BY c.[name];
            """);

        Assert.Equal(1, coveringIndexCount);
        Assert.Equal(1, duplicateProductKeyCount);
        Assert.Equal(["BrandId", "ImageUrl", "Price"], includedColumns);

        var requiredChecks = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT [name]
            FROM sys.check_constraints
            WHERE [name] IN
            (
                N'CK_Stocks_Quantity_NonNegative',
                N'CK_StockMovements_Balance',
                N'CK_Orders_GrandTotal',
                N'CK_OrderItems_LineTotal',
                N'CK_OutboxMessages_Payload_IsJson'
            )
            ORDER BY [name];
            """);
        var requiredForeignKeys = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT [name]
            FROM sys.foreign_keys
            WHERE [name] IN
            (
                N'FK_Customers_AspNetUsers_UserId',
                N'FK_Orders_Carts_SourceCartId',
                N'FK_Orders_Customers_CustomerId',
                N'FK_Stocks_Products_ProductId'
            )
            ORDER BY [name];
            """);

        Assert.Equal(5, requiredChecks.Count);
        Assert.Equal(4, requiredForeignKeys.Count);
        Assert.Equal(
            1,
            await QueryScalarAsync<int>(
                database.ConnectionString,
                "SELECT COUNT(*) FROM sys.sequences WHERE [name] = N'OnlineMarketOrderNumberSequence';"));
    }

    [Fact]
    public async Task ExistingInitialDatabaseUpgradesWithoutLosingBusinessData()
    {
        await using var database = await fixture.CreateDatabaseAsync(applyMigrations: false);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(InitialMigration);

        var brandId = Guid.NewGuid();
        context.Brands.Add(new Brand
        {
            Id = brandId,
            Name = "Upgrade survivor",
            Slug = $"upgrade-{brandId:N}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        Assert.Equal(
            0,
            await QueryScalarAsync<int>(
                database.ConnectionString,
                "SELECT COUNT(*) FROM sys.sequences WHERE [name] = N'OnlineMarketOrderNumberSequence';"));

        await migrator.MigrateAsync();

        Assert.True(await context.Brands.AnyAsync(brand => brand.Id == brandId));
        Assert.Equal(
            1,
            await QueryScalarAsync<int>(
                database.ConnectionString,
                "SELECT COUNT(*) FROM sys.sequences WHERE [name] = N'OnlineMarketOrderNumberSequence';"));
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task MigratedDatabaseHasNoPendingMigrations()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    private static async Task<List<string>> QueryStringsAsync(
        string connectionString,
        string sql)
    {
        var values = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<T> QueryScalarAsync<T>(
        string connectionString,
        string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(
            await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("The scalar query returned null."),
            typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
