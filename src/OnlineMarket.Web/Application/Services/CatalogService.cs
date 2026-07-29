using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IStockMutationService _stockMutationService;

    public CatalogService(
        OnlineMarketDbContext dbContext,
        IStockMutationService stockMutationService)
    {
        _dbContext = dbContext;
        _stockMutationService = stockMutationService;
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        var categories = await _dbContext.Categories
            .Where(category => category.IsActive)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ToListAsync();

        return categories.Select(category => new CategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.DisplayOrder,
            category.ParentCategoryId)).ToList();
    }

    public async Task<List<BrandDto>> GetBrandsAsync()
    {
        var brands = await _dbContext.Brands
            .Where(brand => brand.IsActive)
            .OrderBy(brand => brand.Name)
            .ToListAsync();

        return brands
            .Select(brand => new BrandDto(brand.Id, brand.Name, brand.Slug))
            .ToList();
    }

    public async Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter)
    {
        var query = _dbContext.Products
            .Include(product => product.Category)
            .Include(product => product.Brand)
            .Include(product => product.Stock)
            .Where(product => product.IsActive)
            .AsQueryable();

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == filter.CategoryId.Value);
        }

        if (filter.BrandId.HasValue)
        {
            query = query.Where(product => product.BrandId == filter.BrandId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var search = filter.SearchQuery.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(search)
                || product.Sku.ToLower().Contains(search));
        }

        if (filter.MinPrice.HasValue)
        {
            query = query.Where(product => product.Price >= filter.MinPrice.Value);
        }

        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(product => product.Price <= filter.MaxPrice.Value);
        }

        if (filter.InStockOnly == true)
        {
            query = query.Where(product =>
                product.Stock != null
                && product.Stock.Quantity > 0);
        }

        query = filter.SortBy switch
        {
            "price_asc" => query.OrderBy(product => product.Price),
            "price_desc" => query.OrderByDescending(product => product.Price),
            "name" => query.OrderBy(product => product.Name),
            _ => query.OrderByDescending(product => product.CreatedAtUtc)
        };

        return (await query.ToListAsync()).Select(MapToDto).ToList();
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id)
    {
        var product = await ProductQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == id);

        return product is null ? null : MapToDto(product);
    }

    public async Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var products = await ProductQuery()
            .Where(product => idList.Contains(product.Id))
            .ToListAsync();

        return products.ToDictionary(product => product.Id, MapToDto);
    }

    public async Task<ProductDto?> GetProductBySlugAsync(string slug)
    {
        var product = await ProductQuery()
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug);

        return product is null ? null : MapToDto(product);
    }

    public async Task<ProductDto> CreateProductAsync(ProductDto dto, int initialStock)
    {
        if (initialStock < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialStock));
        }

        var now = UtcNowAtDatabasePrecision();
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Sku = dto.Sku,
            Name = dto.Name,
            Slug = string.IsNullOrWhiteSpace(dto.Slug)
                ? CreateSlug(dto.Name)
                : dto.Slug,
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            BrandId = dto.BrandId,
            Price = dto.Price,
            VatRate = dto.VatRate,
            NetContent = dto.NetContent,
            UnitType = dto.UnitType,
            ImageUrl = dto.ImageUrl,
            IsActive = dto.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var stock = new Stock
        {
            ProductId = productId,
            Quantity = initialStock,
            ReorderLevel = 10,
            UpdatedAtUtc = now
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var parentCategoryId = await GetParentCategoryIdAsync(product.CategoryId);

        _dbContext.Products.Add(product);
        _dbContext.Stocks.Add(stock);

        if (initialStock > 0)
        {
            _dbContext.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.Initial,
                QuantityChange = initialStock,
                PreviousQuantity = 0,
                NewQuantity = initialStock,
                ReferenceType = StockReferenceType.AdminOperation,
                Description = "Initial Stock",
                CreatedAtUtc = now
            });
        }

        _dbContext.OutboxMessages.Add(CreateProductSnapshotMessage(
            product,
            parentCategoryId,
            initialStock,
            now));

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return (await GetProductByIdAsync(productId))!;
    }

    public async Task<ProductDto?> UpdateProductAsync(Guid id, ProductDto dto)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var product = await _dbContext.Products
            .Include(candidate => candidate.Stock)
            .FirstOrDefaultAsync(candidate => candidate.Id == id);

        if (product is null)
        {
            await transaction.RollbackAsync();
            return null;
        }

        var now = UtcNowAtDatabasePrecision();
        product.Name = dto.Name;
        product.Sku = dto.Sku;
        product.Description = dto.Description;
        product.CategoryId = dto.CategoryId;
        product.BrandId = dto.BrandId;
        product.Price = dto.Price;
        product.VatRate = dto.VatRate;
        product.NetContent = dto.NetContent;
        product.UnitType = dto.UnitType;
        product.ImageUrl = dto.ImageUrl;
        product.IsActive = dto.IsActive;
        product.UpdatedAtUtc = now;

        var parentCategoryId = await GetParentCategoryIdAsync(product.CategoryId);
        _dbContext.OutboxMessages.Add(CreateProductSnapshotMessage(
            product,
            parentCategoryId,
            product.Stock?.Quantity ?? 0,
            now));

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetProductByIdAsync(id);
    }

    public async Task<bool> AdjustStockAsync(
        Guid productId,
        int quantityChange,
        string reason,
        Guid userId)
    {
        if (quantityChange == 0)
        {
            return false;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == productId);

        if (product is null)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var now = UtcNowAtDatabasePrecision();
        var stockResult = await _stockMutationService.TryAdjustAsync(
            productId,
            quantityChange,
            now);

        if (stockResult is null)
        {
            await transaction.RollbackAsync();
            return false;
        }

        _dbContext.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            MovementType = quantityChange > 0
                ? StockMovementType.AdminIncrease
                : StockMovementType.AdminDecrease,
            QuantityChange = quantityChange,
            PreviousQuantity = stockResult.Value.PreviousQuantity,
            NewQuantity = stockResult.Value.NewQuantity,
            ReferenceType = StockReferenceType.AdminOperation,
            Description = reason,
            CreatedByUserId = userId == Guid.Empty ? null : userId,
            CreatedAtUtc = now
        });

        var availabilityChanged =
            (stockResult.Value.PreviousQuantity == 0)
            != (stockResult.Value.NewQuantity == 0);

        if (availabilityChanged)
        {
            product.UpdatedAtUtc = now;
            var parentCategoryId = await GetParentCategoryIdAsync(product.CategoryId);
            _dbContext.OutboxMessages.Add(CreateProductSnapshotMessage(
                product,
                parentCategoryId,
                stockResult.Value.NewQuantity,
                now));
        }

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return true;
    }

    private IQueryable<Product> ProductQuery()
    {
        return _dbContext.Products
            .Include(product => product.Category)
            .Include(product => product.Brand)
            .Include(product => product.Stock);
    }

    private async Task<Guid?> GetParentCategoryIdAsync(Guid categoryId)
    {
        return await _dbContext.Categories
            .Where(category => category.Id == categoryId)
            .Select(category => category.ParentCategoryId)
            .SingleAsync();
    }

    private static OutboxMessage CreateProductSnapshotMessage(
        Product product,
        Guid? parentCategoryId,
        int stockQuantity,
        DateTime occurredAtUtc)
    {
        var integrationEvent = new ProductSnapshotChangedV1(
            Guid.NewGuid(),
            occurredAtUtc,
            Guid.NewGuid(),
            product.Id,
            product.Sku,
            product.Name,
            product.CategoryId,
            parentCategoryId,
            product.BrandId,
            product.Price,
            product.NetContent,
            product.UnitType,
            product.IsActive,
            stockQuantity > 0,
            product.UpdatedAtUtc);

        return OutboxMessageFactory.Create(
            integrationEvent,
            OutboxMessageFactory.RecommendationDestination,
            "Product",
            product.Id);
    }

    private static ProductDto MapToDto(Product product)
    {
        var stockQuantity = product.Stock?.Quantity ?? 0;
        return new ProductDto(
            product.Id,
            product.Sku,
            product.Name,
            product.Slug,
            product.Description,
            product.CategoryId,
            product.Category?.Name ?? string.Empty,
            product.BrandId,
            product.Brand?.Name ?? string.Empty,
            product.Price,
            product.VatRate,
            product.NetContent,
            product.UnitType,
            product.ImageUrl,
            product.IsActive,
            stockQuantity,
            stockQuantity > 0);
    }

    private static string CreateSlug(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "-");
    }

    private static DateTime UtcNowAtDatabasePrecision()
    {
        var now = DateTime.UtcNow;
        return new DateTime(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
