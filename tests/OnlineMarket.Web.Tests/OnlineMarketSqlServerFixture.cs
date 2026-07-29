using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OnlineMarket.Web.Tests;

[CollectionDefinition(CollectionName)]
public sealed class OnlineMarketSqlServerCollection
    : ICollectionFixture<OnlineMarketSqlServerFixture>
{
    public const string CollectionName = "OnlineMarket SQL Server";
}

public sealed class OnlineMarketSqlServerFixture : IAsyncLifetime
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

    public Task<OnlineMarketTestDatabase> CreateDatabaseAsync(
        bool applyMigrations = true)
    {
        return OnlineMarketTestDatabase.CreateAsync(
            container.GetConnectionString(),
            applyMigrations);
    }

    private static string CreateEphemeralPassword()
    {
        return $"OnlineMarketTests!{Guid.NewGuid():N}aA9";
    }
}

public sealed class OnlineMarketTestDatabase : IAsyncDisposable
{
    private readonly string masterConnectionString;

    private OnlineMarketTestDatabase(
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

    public static async Task<OnlineMarketTestDatabase> CreateAsync(
        string containerConnectionString,
        bool applyMigrations)
    {
        var databaseName = $"OnlineMarketTests_{Guid.NewGuid():N}";
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

        var database = new OnlineMarketTestDatabase(
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

    public OnlineMarketDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new OnlineMarketDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

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
