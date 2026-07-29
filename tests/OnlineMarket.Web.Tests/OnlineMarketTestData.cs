using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

internal sealed record CheckoutScenario(
    Guid UserId,
    Guid CustomerId,
    Guid AddressId,
    Guid CartId,
    Guid CartItemId,
    Guid CategoryId,
    Guid BrandId,
    Guid ProductId);

internal static class OnlineMarketTestData
{
    public static async Task<CheckoutScenario> SeedCheckoutScenarioAsync(
        OnlineMarketDbContext context,
        int stockQuantity = 10,
        int cartQuantity = 1,
        string? label = null)
    {
        label ??= Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var user = CreateUser(label);
        var customer = CreateCustomer(user.Id, label, now);
        var address = CreateAddress(customer.Id, label, now);
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Category {label}",
            Slug = $"category-{label}",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = $"Brand {label}",
            Slug = $"brand-{label}",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-{label}",
            Name = $"Product {label}",
            Slug = $"product-{label}",
            CategoryId = category.Id,
            BrandId = brand.Id,
            Price = 50m,
            VatRate = 20m,
            NetContent = 1m,
            UnitType = UnitType.Piece,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var stock = new Stock
        {
            ProductId = product.Id,
            Quantity = stockQuantity,
            ReorderLevel = 2,
            UpdatedAtUtc = now
        };
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Status = CartStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = product.Id,
            Quantity = cartQuantity,
            LastKnownUnitPrice = 1m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
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

        return new CheckoutScenario(
            user.Id,
            customer.Id,
            address.Id,
            cart.Id,
            cartItem.Id,
            category.Id,
            brand.Id,
            product.Id);
    }

    public static async Task<CheckoutScenario> AddCustomerCartForProductAsync(
        OnlineMarketDbContext context,
        Guid categoryId,
        Guid brandId,
        Guid productId,
        int cartQuantity,
        string? label = null)
    {
        label ??= Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var user = CreateUser(label);
        var customer = CreateCustomer(user.Id, label, now);
        var address = CreateAddress(customer.Id, label, now);
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Status = CartStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = productId,
            Quantity = cartQuantity,
            LastKnownUnitPrice = 1m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        cart.Items.Add(cartItem);

        context.Users.Add(user);
        context.Customers.Add(customer);
        context.CustomerAddresses.Add(address);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        return new CheckoutScenario(
            user.Id,
            customer.Id,
            address.Id,
            cart.Id,
            cartItem.Id,
            categoryId,
            brandId,
            productId);
    }

    public static CheckoutService CreateCheckoutService(OnlineMarketDbContext context)
    {
        return new CheckoutService(
            context,
            new SqlServerStockMutationService(context),
            new SqlServerOrderNumberGenerator(context),
            NullLogger<CheckoutService>.Instance);
    }

    private static ApplicationUser CreateUser(string label)
    {
        var email = $"{label}@example.test";
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

    private static Customer CreateCustomer(
        Guid userId,
        string label,
        DateTime now)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = $"First{label[..8]}",
            LastName = $"Last{label[..8]}",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static CustomerAddress CreateAddress(
        Guid customerId,
        string label,
        DateTime now)
    {
        return new CustomerAddress
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Title = "Home",
            ContactName = $"Recipient {label[..8]}",
            PhoneNumber = "5550000000",
            AddressLine1 = "Test street 1",
            District = "Test district",
            City = "Test city",
            PostalCode = "34000",
            CountryCode = "TR",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
