using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class CartServiceTests
{
    private DbContextOptions<OnlineMarketDbContext> CreateLocalDbOptions(string dbName)
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";
        return new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public async Task CartService_Add_Update_Remove_Calculates_Subtotal_And_Vat_Accurately()
    {
        var dbName = $"Test_CartService_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var userId = Guid.NewGuid();
            var user = new ApplicationUser { Id = userId, UserName = "cartuser@onlinemarket.com", Email = "cartuser@onlinemarket.com" };

            var customerId = Guid.NewGuid();
            var customer = new Customer { Id = customerId, UserId = userId, FirstName = "Cart", LastName = "User" };

            var category = new Category { Id = Guid.NewGuid(), Name = "Category", Slug = "cat", IsActive = true };
            var brand = new Brand { Id = Guid.NewGuid(), Name = "Brand", Slug = "brand", IsActive = true };

            var productId = Guid.NewGuid();
            var product = new Product
            {
                Id = productId,
                Sku = "CART-01",
                Name = "Cart Test Product",
                Price = 100.00m,
                VatRate = 20.00m,
                NetContent = 1.0m,
                UnitType = UnitType.Piece,
                CategoryId = category.Id,
                BrandId = brand.Id,
                IsActive = true
            };
            var stock = new Stock { ProductId = productId, Quantity = 50 };

            context.Users.Add(user);
            context.Customers.Add(customer);
            context.Categories.Add(category);
            context.Brands.Add(brand);
            context.Products.Add(product);
            context.Stocks.Add(stock);
            await context.SaveChangesAsync();

            var cartService = new CartService(context);

            // 1. Add item
            var cartDto = await cartService.AddItemToCartAsync(customerId, productId, 2);
            Assert.Single(cartDto.Items);
            Assert.Equal(200.00m, cartDto.Subtotal);
            Assert.Equal(40.00m, cartDto.VatTotal);
            Assert.Equal(240.00m, cartDto.GrandTotal);

            // 2. Update quantity to 3
            var itemId = cartDto.Items.First().Id;
            cartDto = await cartService.UpdateItemQuantityAsync(customerId, itemId, 3);
            Assert.Equal(300.00m, cartDto.Subtotal);
            Assert.Equal(60.00m, cartDto.VatTotal);
            Assert.Equal(360.00m, cartDto.GrandTotal);

            // 3. Clear cart
            cartDto = await cartService.ClearCartAsync(customerId);
            Assert.Empty(cartDto.Items);
            Assert.Equal(0m, cartDto.GrandTotal);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
