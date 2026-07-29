using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class RecommendationDbContextFactory
    : IDesignTimeDbContextFactory<RecommendationDbContext>
{
    private const string ConnectionVariable =
        "ConnectionStrings__RecommendationDb";

    public RecommendationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=127.0.0.1,1433;Database=RecommendationDb;" +
                "Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        }

        var options = new DbContextOptionsBuilder<RecommendationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new RecommendationDbContext(options);
    }
}
