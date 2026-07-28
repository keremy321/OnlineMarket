using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class OnlineMarketWebDbContextTests
{
    private DbContextOptions<OnlineMarketDbContext> CreateLocalDbOptions(string dbName)
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";
        return new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public async Task Can_Apply_Migrations_And_Create_Tables_Successfully()
    {
        var dbName = $"Test_OnlineMarketDb_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using (var context = new OnlineMarketDbContext(options))
        {
            try
            {
                // Act: Apply migrations
                await context.Database.MigrateAsync();

                // Assert: Can query DbContext
                var canConnect = await context.Database.CanConnectAsync();
                Assert.True(canConnect);

                // Verify tables exist by adding a Brand
                var brand = new Brand
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Brand",
                    Slug = "test-brand",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                context.Brands.Add(brand);
                await context.SaveChangesAsync();

                var savedBrand = await context.Brands.FirstOrDefaultAsync(b => b.Id == brand.Id);
                Assert.NotNull(savedBrand);
                Assert.Equal("Test Brand", savedBrand.Name);
            }
            finally
            {
                await context.Database.EnsureDeletedAsync();
            }
        }
    }

    [Fact]
    public async Task CheckConstraint_Stocks_Quantity_NonNegative_Rejects_Negative_Values()
    {
        var dbName = $"Test_StockConstraint_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Cat1",
                Slug = "cat-1",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var brand = new Brand
            {
                Id = Guid.NewGuid(),
                Name = "Brand1",
                Slug = "brand-1",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = "SKU-TEST-01",
                Name = "Test Product",
                Slug = "test-product",
                CategoryId = category.Id,
                BrandId = brand.Id,
                Price = 10.00m,
                VatRate = 1.00m,
                NetContent = 1.000m,
                UnitType = UnitType.Piece,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var stock = new Stock
            {
                ProductId = product.Id,
                Quantity = -5, // Negative quantity violates CK_Stocks_Quantity_NonNegative
                UpdatedAtUtc = DateTime.UtcNow
            };

            context.Categories.Add(category);
            context.Brands.Add(brand);
            context.Products.Add(product);
            context.Stocks.Add(stock);

            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                await context.SaveChangesAsync();
            });
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task FilteredIndex_CustomerAddresses_Prevents_Multiple_Active_Default_Addresses()
    {
        var dbName = $"Test_AddressFilterIndex_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "user@test.com",
                NormalizedUserName = "USER@TEST.COM",
                Email = "user@test.com",
                NormalizedEmail = "USER@TEST.COM",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FirstName = "Ali",
                LastName = "Yilmaz",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var addr1 = new CustomerAddress
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                Title = "Ev",
                ContactName = "Ali Yilmaz",
                PhoneNumber = "5551112233",
                AddressLine1 = "Adres 1",
                District = "Kadikoy",
                City = "Istanbul",
                IsDefault = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var addr2 = new CustomerAddress
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                Title = "Is",
                ContactName = "Ali Yilmaz",
                PhoneNumber = "5551112233",
                AddressLine1 = "Adres 2",
                District = "Besiktas",
                City = "Istanbul",
                IsDefault = true, // Duplicate active default address violates UX_CustomerAddresses_Default
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            context.Users.Add(user);
            context.Customers.Add(customer);
            context.CustomerAddresses.AddRange(addr1, addr2);

            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                await context.SaveChangesAsync();
            });
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
