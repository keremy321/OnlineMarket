using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class CatalogService : ICatalogService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IOutboxService _outboxService;

    public CatalogService(OnlineMarketDbContext dbContext, IOutboxService outboxService)
    {
        _dbContext = dbContext;
        _outboxService = outboxService;
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        var categories = await _dbContext.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        return categories.Select(c => new CategoryDto(
            c.Id, c.Name, c.Slug, c.DisplayOrder, c.ParentCategoryId
        )).ToList();
    }

    public async Task<List<BrandDto>> GetBrandsAsync()
    {
        var brands = await _dbContext.Brands
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync();

        return brands.Select(b => new BrandDto(b.Id, b.Name, b.Slug)).ToList();
    }

    public async Task<List<ProductDto>> GetProductsAsync(ProductFilterDto filter)
    {
        var query = _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Stock)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        }

        if (filter.BrandId.HasValue)
        {
            query = query.Where(p => p.BrandId == filter.BrandId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var search = filter.SearchQuery.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(search) || p.Sku.ToLower().Contains(search));
        }

        if (filter.MinPrice.HasValue)
        {
            query = query.Where(p => p.Price >= filter.MinPrice.Value);
        }

        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= filter.MaxPrice.Value);
        }

        if (filter.InStockOnly == true)
        {
            query = query.Where(p => p.Stock != null && p.Stock.Quantity > 0);
        }

        query = filter.SortBy switch
        {
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            "name" => query.OrderBy(p => p.Name),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        var products = await query.ToListAsync();
        return products.Select(MapToDto).ToList();
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id)
    {
        var product = await _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == id);

        return product == null ? null : MapToDto(product);
    }

    public async Task<Dictionary<Guid, ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        if (!idList.Any()) return new Dictionary<Guid, ProductDto>();

        var products = await _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Stock)
            .Where(p => idList.Contains(p.Id))
            .ToListAsync();

        return products.ToDictionary(p => p.Id, MapToDto);
    }

    public async Task<ProductDto?> GetProductBySlugAsync(string slug)
    {
        var product = await _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Slug == slug);

        return product == null ? null : MapToDto(product);
    }

    public async Task<ProductDto> CreateProductAsync(ProductDto dto, int initialStock)
    {
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Sku = dto.Sku,
            Name = dto.Name,
            Slug = string.IsNullOrWhiteSpace(dto.Slug) ? dto.Name.ToLower().Replace(" ", "-") : dto.Slug,
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            BrandId = dto.BrandId,
            Price = dto.Price,
            VatRate = dto.VatRate,
            NetContent = dto.NetContent,
            UnitType = dto.UnitType,
            ImageUrl = dto.ImageUrl,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var stock = new Stock
        {
            ProductId = productId,
            Quantity = initialStock,
            ReorderLevel = 10,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var stockMovement = new StockMovement
        {
            ProductId = productId,
            MovementType = StockMovementType.Initial,
            QuantityChange = initialStock,
            PreviousQuantity = 0,
            NewQuantity = initialStock,
            ReferenceType = StockReferenceType.AdminOperation,
            Description = "Initial Stock",
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Products.Add(product);
        _dbContext.Stocks.Add(stock);
        _dbContext.StockMovements.Add(stockMovement);

        await _dbContext.SaveChangesAsync();

        await RaiseProductSnapshotChangedEventAsync(product, stock.Quantity);

        return (await GetProductByIdAsync(productId))!;
    }

    public async Task<ProductDto?> UpdateProductAsync(Guid id, ProductDto dto)
    {
        var product = await _dbContext.Products
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return null;

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
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        var stockQty = product.Stock?.Quantity ?? 0;
        await RaiseProductSnapshotChangedEventAsync(product, stockQty);

        return await GetProductByIdAsync(id);
    }

    public async Task<bool> AdjustStockAsync(Guid productId, int quantityChange, string reason, Guid userId)
    {
        var product = await _dbContext.Products
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null || product.Stock == null) return false;

        var prevQty = product.Stock.Quantity;
        var newQty = prevQty + quantityChange;
        if (newQty < 0) return false;

        product.Stock.Quantity = newQty;
        product.Stock.UpdatedAtUtc = DateTime.UtcNow;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = quantityChange >= 0 ? StockMovementType.AdminIncrease : StockMovementType.AdminDecrease,
            QuantityChange = quantityChange,
            PreviousQuantity = prevQty,
            NewQuantity = newQty,
            ReferenceType = StockReferenceType.AdminOperation,
            Description = reason,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.StockMovements.Add(movement);
        await _dbContext.SaveChangesAsync();

        await RaiseProductSnapshotChangedEventAsync(product, newQty);

        return true;
    }

    private async Task RaiseProductSnapshotChangedEventAsync(Product product, int stockQuantity)
    {
        var category = await _dbContext.Categories.FindAsync(product.CategoryId);
        var correlationId = Guid.NewGuid();

        var eventPayload = new
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            CategoryId = product.CategoryId,
            ParentCategoryId = category?.ParentCategoryId,
            BrandId = product.BrandId,
            Price = product.Price,
            NetContent = product.NetContent,
            UnitType = (byte)product.UnitType,
            IsActive = product.IsActive,
            IsInStock = stockQuantity > 0,
            SourceUpdatedAtUtc = product.UpdatedAtUtc
        };

        await _outboxService.CreateOutboxMessageAsync(
            eventType: "ProductSnapshotChangedV1",
            destination: "Recommendation.Api",
            aggregateType: "Product",
            aggregateId: product.Id,
            eventPayload: eventPayload,
            correlationId: correlationId
        );
    }

    private static ProductDto MapToDto(Product p)
    {
        var stockQty = p.Stock?.Quantity ?? 0;
        return new ProductDto(
            p.Id,
            p.Sku,
            p.Name,
            p.Slug,
            p.Description,
            p.CategoryId,
            p.Category?.Name ?? string.Empty,
            p.BrandId,
            p.Brand?.Name ?? string.Empty,
            p.Price,
            p.VatRate,
            p.NetContent,
            p.UnitType,
            p.ImageUrl,
            p.IsActive,
            stockQty,
            stockQty > 0
        );
    }
}
