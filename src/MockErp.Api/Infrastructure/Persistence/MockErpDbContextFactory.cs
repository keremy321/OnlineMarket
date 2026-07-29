using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MockErp.Api.Infrastructure.Persistence;

public sealed class MockErpDbContextFactory
    : IDesignTimeDbContextFactory<MockErpDbContext>
{
    private const string ConnectionVariable =
        "ConnectionStrings__MockErpDb";

    public MockErpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=127.0.0.1,1433;Database=MockErpDb;" +
                "Integrated Security=True;Encrypt=True;" +
                "TrustServerCertificate=True;";
        }

        var options = new DbContextOptionsBuilder<MockErpDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new MockErpDbContext(options);
    }
}
