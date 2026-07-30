extern alias MockErpApi;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MockErpDbContext =
    MockErpApi::MockErp.Api.Infrastructure.Persistence.MockErpDbContext;
using MockErpStock =
    MockErpApi::MockErp.Api.Domain.Entities.ErpStock;
using MockErpUnitType =
    MockErpApi::MockErp.Api.Domain.Enums.UnitType;

namespace ErpIntegration.Api.Tests;

internal sealed class MockErpWorkerTestDatabase : IAsyncDisposable
{
    private readonly string masterConnectionString;

    private MockErpWorkerTestDatabase(
        string databaseName,
        string connectionString,
        string masterConnectionString)
    {
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        this.masterConnectionString = masterConnectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static async Task<MockErpWorkerTestDatabase> CreateAsync(
        string existingDatabaseConnectionString)
    {
        var databaseName = $"WorkerMockErpTests_{Guid.NewGuid():N}";
        var source = new SqlConnectionStringBuilder(
            existingDatabaseConnectionString);
        var masterBuilder = new SqlConnectionStringBuilder(source.ConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        var databaseBuilder = new SqlConnectionStringBuilder(source.ConnectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };

        await using (var connection = new SqlConnection(
            masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var database = new MockErpWorkerTestDatabase(
            databaseName,
            databaseBuilder.ConnectionString,
            masterBuilder.ConnectionString);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public MockErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MockErpDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new MockErpDbContext(options);
    }

    public async Task SeedStockAsync(
        Guid productId,
        string sku,
        string productName,
        int quantity)
    {
        await using var context = CreateContext();
        context.ErpStocks.Add(new MockErpStock
        {
            Id = Guid.NewGuid(),
            ExternalProductId = productId,
            Sku = sku,
            ProductName = productName,
            UnitType = MockErpUnitType.Piece,
            NetContent = 1m,
            Quantity = quantity,
            ReorderLevel = 1,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new SqlConnection(
            masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }
}
