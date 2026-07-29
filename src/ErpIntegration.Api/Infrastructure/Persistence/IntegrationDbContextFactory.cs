using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ErpIntegration.Api.Infrastructure.Persistence;

public sealed class IntegrationDbContextFactory
    : IDesignTimeDbContextFactory<IntegrationDbContext>
{
    private const string ConnectionVariable =
        "ConnectionStrings__IntegrationDb";

    public IntegrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=127.0.0.1,1433;Database=IntegrationDb;" +
                "Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        }

        var options = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new IntegrationDbContext(options);
    }
}
