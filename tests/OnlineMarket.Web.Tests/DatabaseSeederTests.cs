using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class DatabaseSeederTests
{
    private DbContextOptions<OnlineMarketDbContext> CreateLocalDbOptions(string dbName)
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";
        return new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public async Task Seeder_Populates_AdminUser_Roles_Categories_Brands_Products_Stocks_And_StockMovements()
    {
        var dbName = $"Test_SeederDb_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var userStore = new UserStore<ApplicationUser, IdentityRole<Guid>, OnlineMarketDbContext, Guid>(context);
            var roleStore = new RoleStore<IdentityRole<Guid>, OnlineMarketDbContext, Guid>(context);

            var userManager = new UserManager<ApplicationUser>(
                userStore, null!, new PasswordHasher<ApplicationUser>(), null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole<Guid>>(
                roleStore, null!, null!, null!, null!);

            var catalogPath = Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/seed/catalog.v1.json");
            catalogPath = Path.GetFullPath(catalogPath);

            // Act
            await DatabaseSeeder.SeedAsync(context, userManager, roleManager, catalogPath);

            // Assert
            var adminUser = await userManager.FindByEmailAsync("admin@onlinemarket.com");
            Assert.NotNull(adminUser);

            var hasAdminRole = await userManager.IsInRoleAsync(adminUser, "Admin");
            Assert.True(hasAdminRole);

            var categoriesCount = await context.Categories.CountAsync();
            Assert.True(categoriesCount >= 4);

            var brandsCount = await context.Brands.CountAsync();
            Assert.True(brandsCount >= 3);

            var productsCount = await context.Products.CountAsync();
            Assert.True(productsCount >= 10);

            var stocksCount = await context.Stocks.CountAsync();
            Assert.Equal(productsCount, stocksCount);

            var movementsCount = await context.StockMovements.CountAsync();
            Assert.Equal(productsCount, movementsCount);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
