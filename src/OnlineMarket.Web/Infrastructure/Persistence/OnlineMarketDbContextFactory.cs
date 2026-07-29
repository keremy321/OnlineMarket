using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public sealed class OnlineMarketDbContextFactory
    : IDesignTimeDbContextFactory<OnlineMarketDbContext>
{
    private const string ConnectionVariable = "ConnectionStrings__OnlineMarketDb";

    public OnlineMarketDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        var optionsBuilder = new DbContextOptionsBuilder<OnlineMarketDbContext>();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseSqlServer();
        }
        else
        {
            optionsBuilder.UseSqlServer(connectionString);
        }

        return new OnlineMarketDbContext(optionsBuilder.Options);
    }
}
