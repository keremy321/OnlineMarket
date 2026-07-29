using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class CheckoutServiceTests
{
    private DbContextOptions<OnlineMarketDbContext> CreateLocalDbOptions(string dbName)
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";
        return new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public async Task ExecuteCheckoutAsync_Saves_Order_Snapshots_StockMovements_And_OutboxEvents_In_One_Transaction()
    {
        var dbName = $"Test_Checkout_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var userId = Guid.NewGuid();
            var user = new ApplicationUser { Id = userId, UserName = "test@onlinemarket.com", Email = "test@onlinemarket.com" };

            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                Id = customerId,
                UserId = userId,
                FirstName = "Test",
                LastName = "Müşteri",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var address = new CustomerAddress
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Title = "Ev Adresi",
                ContactName = "Test Müşteri",
                PhoneNumber = "05551112233",
                AddressLine1 = "Test Cad. No:1",
                District = "Kayıthaneler",
                City = "İstanbul",
                CountryCode = "TR",
                IsDefault = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Test Kategori",
                Slug = "test-kategori",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var brand = new Brand
            {
                Id = Guid.NewGuid(),
                Name = "Test Marka",
                Slug = "test-marka",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var productId = Guid.NewGuid();
            var product = new Product
            {
                Id = productId,
                Sku = "TEST-PRD-001",
                Name = "Test Ürünü",
                Slug = "test-urunu",
                CategoryId = category.Id,
                BrandId = brand.Id,
                Price = 50.00m,
                VatRate = 20.00m,
                NetContent = 1.000m,
                UnitType = UnitType.Piece,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var stock = new Stock
            {
                ProductId = productId,
                Quantity = 100,
                ReorderLevel = 10,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var cart = new Cart
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Status = CartStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var cartItem = new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = productId,
                Quantity = 2,
                LastKnownUnitPrice = 50.00m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            cart.Items.Add(cartItem);

            context.Users.Add(user);
            context.Customers.Add(customer);
            context.CustomerAddresses.Add(address);
            context.Categories.Add(category);
            context.Brands.Add(brand);
            context.Products.Add(product);
            context.Stocks.Add(stock);
            context.Carts.Add(cart);
            await context.SaveChangesAsync();

            // 2. Act
            var checkoutService = new CheckoutService(context);
            var request = new CheckoutRequestDto(address.Id, "Test Müşteri", "**** 1234", true);

            var result = await checkoutService.ExecuteCheckoutAsync(customerId, request);

            // 3. Assert
            Assert.True(result.Success);
            Assert.NotNull(result.OrderId);
            Assert.NotNull(result.OrderNumber);

            // Verify Order saved
            var order = await context.Orders
                .Include(o => o.Items)
                .Include(o => o.AddressSnapshot)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.Id == result.OrderId);

            Assert.NotNull(order);
            Assert.Equal(customerId, order.CustomerId);
            Assert.Equal(OrderStatus.Confirmed, order.Status);
            Assert.Equal(100.00m, order.Subtotal);
            Assert.Equal(20.00m, order.VatTotal);
            Assert.Equal(120.00m, order.GrandTotal);

            // Verify Snapshots
            Assert.NotNull(order.AddressSnapshot);
            Assert.Equal("Test Müşteri", order.AddressSnapshot.RecipientName);
            Assert.Single(order.Items);
            var item = order.Items.First();
            Assert.Equal("TEST-PRD-001", item.SkuSnapshot);
            Assert.Equal(50.00m, item.UnitPrice);

            // Verify Payment simulation
            Assert.NotNull(order.Payment);
            Assert.Equal(PaymentStatus.Succeeded, order.Payment.Status);

            // Verify Atomic Stock Decrease
            var updatedStock = await context.Stocks.FirstAsync(s => s.ProductId == productId);
            Assert.Equal(98, updatedStock.Quantity);

            // Verify Stock Movement
            var movement = await context.StockMovements.FirstOrDefaultAsync(m => m.ProductId == productId && m.ReferenceType == StockReferenceType.Order);
            Assert.NotNull(movement);
            Assert.Equal(-2, movement.QuantityChange);
            Assert.Equal(98, movement.NewQuantity);

            // Verify Cart Converted
            var updatedCart = await context.Carts.FirstAsync(c => c.Id == cart.Id);
            Assert.Equal(CartStatus.Converted, updatedCart.Status);

            // Verify 2 Outbox Events Created
            var outboxMessages = await context.OutboxMessages.Where(m => m.AggregateId == order.Id).ToListAsync();
            Assert.Equal(2, outboxMessages.Count);
            Assert.Contains(outboxMessages, m => m.EventType == "OrderConfirmedForRecommendationV1");
            Assert.Contains(outboxMessages, m => m.EventType == "OrderReadyForErpV1");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ExecuteCheckoutAsync_RollsBack_When_Stock_Is_Insufficient()
    {
        var dbName = $"Test_CheckoutFail_{Guid.NewGuid():N}";
        var options = CreateLocalDbOptions(dbName);

        using var context = new OnlineMarketDbContext(options);
        try
        {
            await context.Database.MigrateAsync();

            var userId = Guid.NewGuid();
            var user = new ApplicationUser { Id = userId, UserName = "fail@onlinemarket.com", Email = "fail@onlinemarket.com" };

            var customerId = Guid.NewGuid();
            var customer = new Customer { Id = customerId, UserId = userId, FirstName = "Fail", LastName = "User" };
            var address = new CustomerAddress { Id = Guid.NewGuid(), CustomerId = customerId, Title = "Home", AddressLine1 = "Addr", City = "City", District = "Dist" };

            var category = new Category { Id = Guid.NewGuid(), Name = "Category", Slug = "cat", IsActive = true };
            var brand = new Brand { Id = Guid.NewGuid(), Name = "Brand", Slug = "brand", IsActive = true };

            var productId = Guid.NewGuid();
            var product = new Product
            {
                Id = productId,
                Sku = "OUT-001",
                Name = "Low Stock Product",
                Price = 10,
                VatRate = 20,
                NetContent = 1.0m,
                UnitType = UnitType.Piece,
                CategoryId = category.Id,
                BrandId = brand.Id,
                IsActive = true
            };
            var stock = new Stock { ProductId = productId, Quantity = 1 };

            var cart = new Cart { Id = Guid.NewGuid(), CustomerId = customerId, Status = CartStatus.Active };
            cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = productId, Quantity = 5, LastKnownUnitPrice = 10 });

            context.Users.Add(user);
            context.Customers.Add(customer);
            context.CustomerAddresses.Add(address);
            context.Categories.Add(category);
            context.Brands.Add(brand);
            context.Products.Add(product);
            context.Stocks.Add(stock);
            context.Carts.Add(cart);
            await context.SaveChangesAsync();

            var checkoutService = new CheckoutService(context);
            var result = await checkoutService.ExecuteCheckoutAsync(customerId, new CheckoutRequestDto(address.Id, "Fail User", "**** 1234", true));

            Assert.False(result.Success);
            Assert.Contains("yetersiz stok", result.ErrorMessage);

            // Verify Stock remains untouched
            var updatedStock = await context.Stocks.FirstAsync(s => s.ProductId == productId);
            Assert.Equal(1, updatedStock.Quantity);

            // Verify No Order created
            Assert.Equal(0, await context.Orders.CountAsync());
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
