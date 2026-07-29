using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class DatabaseSeederTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public DatabaseSeederTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task CanonicalSeedIsRepeatableAndDoesNotResetLegitimateStock()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var provider = CreateServiceProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var adminEmail = $"{Guid.NewGuid():N}@example.test";
        var adminPassword = $"Seed!{Guid.NewGuid():N}aA9";
        var credentials = new SeedAdminCredentials(adminEmail, adminPassword);
        var canonicalPath = GetCanonicalCatalogPath();

        await seeder.SeedAsync(canonicalPath, credentials);

        Assert.True(await roleManager.RoleExistsAsync("Admin"));
        Assert.True(await roleManager.RoleExistsAsync("Customer"));
        var admin = await userManager.FindByEmailAsync(adminEmail);
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, "Admin"));

        var firstCounts = await GetCountsAsync(context);
        Assert.Equal((4, 3, 10, 10, 10), firstCounts);

        var stock = await context.Stocks.OrderBy(candidate => candidate.ProductId).FirstAsync();
        var changedQuantity = stock.Quantity - 7;
        stock.Quantity = changedQuantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await seeder.SeedAsync(canonicalPath, credentials);
        context.ChangeTracker.Clear();

        Assert.Equal(firstCounts, await GetCountsAsync(context));
        Assert.Equal(
            changedQuantity,
            await context.Stocks
                .Where(candidate => candidate.ProductId == stock.ProductId)
                .Select(candidate => candidate.Quantity)
                .SingleAsync());
    }

    [Fact]
    public async Task OmittedAdminConfigurationCreatesNoUsableCredential()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var provider = CreateServiceProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        await seeder.SeedAsync(
            GetCanonicalCatalogPath(),
            new SeedAdminCredentials(null, null));
        await seeder.SeedAsync(
            GetCanonicalCatalogPath(),
            new SeedAdminCredentials(null, null));

        Assert.Equal(0, await context.Users.CountAsync());
        Assert.Equal(10, await context.Products.CountAsync());
        Assert.Equal(10, await context.Stocks.CountAsync());
        Assert.Equal(10, await context.StockMovements.CountAsync());
    }

    private static ServiceProvider CreateServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OnlineMarketDbContext>(
            options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<OnlineMarketDbContext>();
        services.AddScoped<DatabaseSeeder>();
        return services.BuildServiceProvider();
    }

    private static string GetCanonicalCatalogPath()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Seed",
            "catalog.v1.json");
        Assert.True(File.Exists(path));
        Assert.Equal("catalog.v1.json", Path.GetFileName(path));
        return path;
    }

    private static async Task<(int Categories, int Brands, int Products, int Stocks, int Movements)>
        GetCountsAsync(OnlineMarketDbContext context)
    {
        return (
            await context.Categories.CountAsync(),
            await context.Brands.CountAsync(),
            await context.Products.CountAsync(),
            await context.Stocks.CountAsync(),
            await context.StockMovements.CountAsync());
    }
}
