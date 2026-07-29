using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace MockErp.Api.Tests;

[CollectionDefinition(CollectionName)]
public sealed class MockErpSqlServerCollection
    : ICollectionFixture<MockErpSqlServerFixture>
{
    public const string CollectionName = "Mock ERP SQL Server";
}

public sealed class MockErpSqlServerFixture : IAsyncLifetime
{
    private const string SqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU22-GDR1-ubuntu-22.04@sha256:bf438d7104f861f5e1e1ba14b063d60a5bd883964b3c9b3b3c0064536177baa5";

    private readonly MsSqlContainer container = new MsSqlBuilder()
        .WithImage(SqlServerImage)
        .WithPassword(CreateEphemeralPassword())
        .Build();

    public Task InitializeAsync()
    {
        return container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }

    public Task<MockErpTestDatabase> CreateDatabaseAsync(
        bool applyMigrations = true)
    {
        return MockErpTestDatabase.CreateAsync(
            container.GetConnectionString(),
            applyMigrations);
    }

    private static string CreateEphemeralPassword()
    {
        return $"MockErp!{Guid.NewGuid():N}aA9";
    }
}

public sealed class MockErpTestDatabase : IAsyncDisposable
{
    private readonly string masterConnectionString;

    private MockErpTestDatabase(
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

    public static async Task<MockErpTestDatabase> CreateAsync(
        string containerConnectionString,
        bool applyMigrations)
    {
        var databaseName = $"MockErpTests_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        var databaseBuilder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };

        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var database = new MockErpTestDatabase(
            databaseName,
            databaseBuilder.ConnectionString,
            masterBuilder.ConnectionString);

        if (applyMigrations)
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
        }

        return database;
    }

    public MockErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MockErpDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new MockErpDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new SqlConnection(masterConnectionString);
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
