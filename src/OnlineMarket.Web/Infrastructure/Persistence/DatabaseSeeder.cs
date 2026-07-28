using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        OnlineMarketDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        string catalogJsonPath)
    {
        // 1. Ensure Roles
        string[] roles = ["Admin", "Customer"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        // 2. Ensure Admin User
        var adminEmail = "admin@onlinemarket.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(adminUser, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // 3. Purge dummy data if detected (e.g. "Kategori 1" or "kategori-1")
        var hasDummyData = await context.Categories.AnyAsync(c => c.Name.StartsWith("Kategori ") || c.Slug.StartsWith("kategori-"));
        if (hasDummyData)
        {
            context.OutboxMessages.RemoveRange(context.OutboxMessages);
            context.Payments.RemoveRange(context.Payments);
            context.OrderItems.RemoveRange(context.OrderItems);
            context.OrderAddresses.RemoveRange(context.OrderAddresses);
            context.Orders.RemoveRange(context.Orders);
            context.CartItems.RemoveRange(context.CartItems);
            context.Carts.RemoveRange(context.Carts);
            context.StockMovements.RemoveRange(context.StockMovements);
            context.Stocks.RemoveRange(context.Stocks);
            context.Products.RemoveRange(context.Products);
            context.Brands.RemoveRange(context.Brands);
            context.Categories.RemoveRange(context.Categories);
            await context.SaveChangesAsync();
        }

        // 4. Seed Catalog from JSON if not already seeded
        if (!await context.Products.AnyAsync() && File.Exists(catalogJsonPath))
        {
            var jsonText = await File.ReadAllTextAsync(catalogJsonPath);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            // Seed Categories
            var categoriesList = new List<Category>();
            if (root.TryGetProperty("categories", out var categoriesElement))
            {
                foreach (var c in categoriesElement.EnumerateArray())
                {
                    categoriesList.Add(new Category
                    {
                        Id = c.GetProperty("id").GetGuid(),
                        Name = c.GetProperty("name").GetString()!,
                        Slug = c.GetProperty("slug").GetString()!,
                        DisplayOrder = c.GetProperty("displayOrder").GetInt32(),
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
                context.Categories.AddRange(categoriesList);
            }

            // Seed Brands
            var brandsList = new List<Brand>();
            if (root.TryGetProperty("brands", out var brandsElement))
            {
                foreach (var b in brandsElement.EnumerateArray())
                {
                    brandsList.Add(new Brand
                    {
                        Id = b.GetProperty("id").GetGuid(),
                        Name = b.GetProperty("name").GetString()!,
                        Slug = b.GetProperty("slug").GetString()!,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
                context.Brands.AddRange(brandsList);
            }

            await context.SaveChangesAsync();

            // Seed Products, Stocks, and Stock Movements
            if (root.TryGetProperty("products", out var productsElement))
            {
                foreach (var p in productsElement.EnumerateArray())
                {
                    var productId = p.GetProperty("id").GetGuid();
                    var initialStockQty = p.GetProperty("initialStock").GetInt32();

                    var product = new Product
                    {
                        Id = productId,
                        Sku = p.GetProperty("sku").GetString()!,
                        Name = p.GetProperty("name").GetString()!,
                        Slug = p.GetProperty("slug").GetString()!,
                        CategoryId = p.GetProperty("categoryId").GetGuid(),
                        BrandId = p.GetProperty("brandId").GetGuid(),
                        Price = p.GetProperty("price").GetDecimal(),
                        VatRate = p.GetProperty("vatRate").GetDecimal(),
                        NetContent = p.GetProperty("netContent").GetDecimal(),
                        UnitType = (UnitType)p.GetProperty("unitType").GetByte(),
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    var stock = new Stock
                    {
                        ProductId = productId,
                        Quantity = initialStockQty,
                        ReorderLevel = 10,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    var stockMovement = new StockMovement
                    {
                        ProductId = productId,
                        MovementType = StockMovementType.Initial,
                        QuantityChange = initialStockQty,
                        PreviousQuantity = 0,
                        NewQuantity = initialStockQty,
                        ReferenceType = StockReferenceType.Seed,
                        Description = "Initial Seed Balance",
                        CreatedByUserId = adminUser.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    context.Products.Add(product);
                    context.Stocks.Add(stock);
                    context.StockMovements.Add(stockMovement);
                }

                await context.SaveChangesAsync();
            }
        }
    }
}
