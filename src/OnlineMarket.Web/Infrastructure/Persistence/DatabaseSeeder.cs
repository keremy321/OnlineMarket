using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public sealed record SeedAdminCredentials(string? Email, string? Password);

public sealed class DatabaseSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OnlineMarketDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        OnlineMarketDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync(
        string catalogJsonPath,
        SeedAdminCredentials adminCredentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogJsonPath);
        ArgumentNullException.ThrowIfNull(adminCredentials);

        await EnsureRoleAsync("Admin");
        await EnsureRoleAsync("Customer");
        var adminUser = await EnsureAdminAsync(adminCredentials);

        var catalog = await LoadCatalogAsync(catalogJsonPath, cancellationToken);
        await SeedCatalogAsync(catalog, adminUser?.Id, cancellationToken);
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        ThrowIfFailed(result, $"Could not create the {roleName} role.");
    }

    private async Task<ApplicationUser?> EnsureAdminAsync(
        SeedAdminCredentials credentials)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(credentials.Email);
        var hasPassword = !string.IsNullOrWhiteSpace(credentials.Password);

        if (!hasEmail && !hasPassword)
        {
            _logger.LogInformation(
                "Development admin seeding was skipped because no configured admin credentials were supplied.");
            return null;
        }

        if (!hasEmail || !hasPassword)
        {
            throw new InvalidOperationException(
                "SeedAdmin:Email and SeedAdmin:Password must either both be configured or both be omitted.");
        }

        var adminUser = await _userManager.FindByEmailAsync(credentials.Email!);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = credentials.Email,
                Email = credentials.Email,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(
                adminUser,
                credentials.Password!);
            ThrowIfFailed(createResult, "Could not create the configured development admin.");
        }

        if (!await _userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            var roleResult = await _userManager.AddToRoleAsync(adminUser, "Admin");
            ThrowIfFailed(roleResult, "Could not assign the configured development admin role.");
        }

        return adminUser;
    }

    private async Task<CatalogSeedDocument> LoadCatalogAsync(
        string catalogJsonPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(catalogJsonPath))
        {
            throw new FileNotFoundException(
                "The canonical catalogue seed file was not found.",
                catalogJsonPath);
        }

        await using var stream = File.OpenRead(catalogJsonPath);
        return await JsonSerializer.DeserializeAsync<CatalogSeedDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("The canonical catalogue seed file is empty.");
    }

    private async Task SeedCatalogAsync(
        CatalogSeedDocument catalog,
        Guid? adminUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var isRelational = _dbContext.Database.IsRelational();
        var transaction = isRelational ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

        var existingCategories = await _dbContext.Categories
            .ToDictionaryAsync(category => category.Id, cancellationToken);
        var categorySlugs = existingCategories.Values
            .ToDictionary(category => category.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var seed in catalog.Categories)
        {
            if (existingCategories.ContainsKey(seed.Id))
            {
                continue;
            }

            var effectiveSlug = string.IsNullOrWhiteSpace(seed.Slug) ? GenerateSlug(seed.Name) : seed.Slug;

            if (categorySlugs.TryGetValue(effectiveSlug, out var conflictingCategory) && conflictingCategory.Id != seed.Id)
            {
                effectiveSlug = $"{effectiveSlug}-{seed.Id.ToString("N")[..6]}";
            }

            var category = new Category
            {
                Id = seed.Id,
                ParentCategoryId = seed.ParentCategoryId,
                Name = seed.Name,
                Slug = effectiveSlug,
                DisplayOrder = seed.DisplayOrder,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _dbContext.Categories.Add(category);
            existingCategories.Add(category.Id, category);
            categorySlugs[category.Slug] = category;
        }

        var existingBrands = await _dbContext.Brands
            .ToDictionaryAsync(brand => brand.Id, cancellationToken);
        var brandSlugs = existingBrands.Values
            .ToDictionary(brand => brand.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var seed in catalog.Brands)
        {
            if (existingBrands.ContainsKey(seed.Id))
            {
                continue;
            }

            var effectiveSlug = string.IsNullOrWhiteSpace(seed.Slug) ? GenerateSlug(seed.Name) : seed.Slug;

            if (brandSlugs.TryGetValue(effectiveSlug, out var conflictingBrand) && conflictingBrand.Id != seed.Id)
            {
                effectiveSlug = $"{effectiveSlug}-{seed.Id.ToString("N")[..6]}";
            }

            var brand = new Brand
            {
                Id = seed.Id,
                Name = seed.Name,
                Slug = effectiveSlug,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _dbContext.Brands.Add(brand);
            existingBrands.Add(brand.Id, brand);
            brandSlugs[brand.Slug] = brand;
        }

        var existingProducts = await _dbContext.Products
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        var productsBySku = existingProducts.Values
            .ToDictionary(product => product.Sku, StringComparer.OrdinalIgnoreCase);
        var productsBySlug = existingProducts.Values
            .ToDictionary(product => product.Slug, StringComparer.OrdinalIgnoreCase);
        var existingStockProductIds = await _dbContext.Stocks
            .Select(stock => stock.ProductId)
            .ToHashSetAsync(cancellationToken);

        foreach (var seed in catalog.Products)
        {
            if (!existingCategories.ContainsKey(seed.CategoryId)
                || !existingBrands.ContainsKey(seed.BrandId))
            {
                throw new InvalidDataException(
                    $"Canonical product '{seed.Sku}' references an unknown category or brand.");
            }

            var effectiveSlug = string.IsNullOrWhiteSpace(seed.Slug) ? GenerateSlug(seed.Name) : seed.Slug;

            if (!existingProducts.TryGetValue(seed.Id, out var product))
            {
                if (productsBySku.TryGetValue(seed.Sku, out var skuConflict) && skuConflict.Id != seed.Id)
                {
                    throw new InvalidOperationException(
                        $"Canonical SKU '{seed.Sku}' conflicts with product {skuConflict.Id}.");
                }

                if (productsBySlug.TryGetValue(effectiveSlug, out var slugConflict) && slugConflict.Id != seed.Id)
                {
                    effectiveSlug = $"{effectiveSlug}-{seed.Id.ToString("N")[..6]}";
                }

                product = new Product
                {
                    Id = seed.Id,
                    Sku = seed.Sku,
                    Name = seed.Name,
                    Slug = effectiveSlug,
                    Description = seed.Description,
                    CategoryId = seed.CategoryId,
                    BrandId = seed.BrandId,
                    Price = seed.Price,
                    VatRate = seed.VatRate,
                    NetContent = seed.NetContent,
                    UnitType = seed.UnitType,
                    ImageUrl = seed.ImageUrl,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.Products.Add(product);
                existingProducts.Add(product.Id, product);
                productsBySku[product.Sku] = product;
                productsBySlug[product.Slug] = product;
            }

            if (existingStockProductIds.Contains(seed.Id))
            {
                continue;
            }

            _dbContext.Stocks.Add(new Stock
            {
                ProductId = seed.Id,
                Quantity = seed.InitialStock,
                ReorderLevel = seed.ReorderLevel,
                UpdatedAtUtc = now
            });
            existingStockProductIds.Add(seed.Id);

            if (seed.InitialStock > 0)
            {
                _dbContext.StockMovements.Add(new StockMovement
                {
                    ProductId = seed.Id,
                    MovementType = StockMovementType.Initial,
                    QuantityChange = seed.InitialStock,
                    PreviousQuantity = 0,
                    NewQuantity = seed.InitialStock,
                    ReferenceType = StockReferenceType.Seed,
                    Description = "Initial Seed Balance",
                    CreatedByUserId = adminUserId,
                    CreatedAtUtc = now
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }
    }

    private static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Guid.NewGuid().ToString("N");
        var normalized = text.ToLowerInvariant()
            .Replace("ç", "c")
            .Replace("ğ", "g")
            .Replace("ı", "i")
            .Replace("ö", "o")
            .Replace("ş", "s")
            .Replace("ü", "u")
            .Replace("I", "i")
            .Replace("İ", "i")
            .Replace("&", "ve");
        var invalidCharsRemoved = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9\s-]", "");
        var spacesToDash = System.Text.RegularExpressions.Regex.Replace(invalidCharsRemoved, @"\s+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(spacesToDash) ? Guid.NewGuid().ToString("N") : spacesToDash;
    }

    private static void ThrowIfFailed(
        IdentityResult result,
        string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var codes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"{message} Identity codes: {codes}");
    }

    private sealed record CatalogSeedDocument(
        List<CategorySeed> Categories,
        List<BrandSeed> Brands,
        List<ProductSeed> Products);

    private sealed record CategorySeed(
        Guid Id,
        Guid? ParentCategoryId,
        string Name,
        string? Slug,
        int DisplayOrder);

    private sealed record BrandSeed(
        Guid Id,
        string Name,
        string? Slug);

    private sealed record ProductSeed(
        Guid Id,
        string Sku,
        string Name,
        string? Slug,
        string? Description,
        Guid CategoryId,
        Guid BrandId,
        decimal Price,
        decimal VatRate,
        decimal NetContent,
        UnitType UnitType,
        string? ImageUrl,
        int InitialStock,
        int ReorderLevel = 10);
}
