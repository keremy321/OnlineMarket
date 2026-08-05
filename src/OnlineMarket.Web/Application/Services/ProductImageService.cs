using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public sealed class ProductImageService : IProductImageService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private readonly IWebHostEnvironment _environment;
    private readonly OnlineMarketDbContext _dbContext;
    private readonly ICatalogService _catalogService;
    private readonly ILogger<ProductImageService> _logger;

    public ProductImageService(
        IWebHostEnvironment environment,
        OnlineMarketDbContext dbContext,
        ICatalogService catalogService,
        ILogger<ProductImageService> logger)
    {
        _environment = environment;
        _dbContext = dbContext;
        _catalogService = catalogService;
        _logger = logger;
    }

    public async Task<string?> SaveProductImageAsync(
        IFormFile? imageFile,
        string productName,
        string sku,
        CancellationToken cancellationToken = default)
    {
        if (imageFile is null || imageFile.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                $"Desteklenmeyen görsel formatı ({extension}). İzin verilen formatlar: .jpg, .jpeg, .png, .webp, .gif");
        }

        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "products");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var slug = Slugify(productName);
        var cleanSku = Slugify(sku);
        var fileName = $"{slug}-{cleanSku}{extension}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await imageFile.CopyToAsync(stream, cancellationToken);

        _logger.LogInformation("Product image saved for {ProductName} ({Sku}) at {FilePath}", productName, sku, filePath);

        return $"/uploads/products/{fileName}";
    }

    public async Task<int> BulkMatchImagesFromFolderAsync(CancellationToken cancellationToken = default)
    {
        var matchedCount = 0;
        var searchDirectories = new[]
        {
            Path.Combine(_environment.WebRootPath, "uploads", "products")
        };

        var availableImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in searchDirectories)
        {
            if (!Directory.Exists(dir)) continue;

            var files = Directory.GetFiles(dir)
                .Where(file => AllowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));

            foreach (var filePath in files)
            {
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                var normalizedKey = Slugify(fileNameWithoutExt);
                
                var relativePath = "/" + Path.GetRelativePath(_environment.WebRootPath, filePath).Replace('\\', '/');

                availableImages[normalizedKey] = relativePath;
                
                // Also index by raw name without extension for exact matches
                var rawKey = fileNameWithoutExt.Trim().ToLowerInvariant();
                if (!availableImages.ContainsKey(rawKey))
                {
                    availableImages[rawKey] = relativePath;
                }
            }
        }

        if (availableImages.Count == 0)
        {
            _logger.LogInformation("BulkMatchImagesFromFolderAsync: No images found in search directories.");
            return 0;
        }

        var products = await _dbContext.Products
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);

        for (int i = 0; i < products.Count; i++)
        {
            var product = products[i];
            var indexedKey = $"p_{(i + 1):D3}";
            var productSlug = Slugify(product.Name);
            var productSku = Slugify(product.Sku);
            var productNameRaw = product.Name.Trim().ToLowerInvariant();

            string? matchedUrl = null;

            if (availableImages.TryGetValue(indexedKey, out var urlByIndex))
            {
                matchedUrl = urlByIndex;
            }
            else if (availableImages.TryGetValue(productSlug, out var urlBySlug))
            {
                matchedUrl = urlBySlug;
            }
            else if (availableImages.TryGetValue(productSku, out var urlBySku))
            {
                matchedUrl = urlBySku;
            }
            else if (availableImages.TryGetValue(productNameRaw, out var urlByRawName))
            {
                matchedUrl = urlByRawName;
            }

            if (!string.IsNullOrEmpty(matchedUrl))
            {
                var dto = (await _catalogService.GetProductByIdAsync(product.Id));
                if (dto != null)
                {
                    var updatedDto = dto with { ImageUrl = matchedUrl };
                    await _catalogService.UpdateProductAsync(product.Id, updatedDto);
                    matchedCount++;
                    _logger.LogInformation("Matched image {MatchedUrl} to product {ProductName} ({ProductId})", matchedUrl, product.Name, product.Id);
                }
            }
        }

        return matchedCount;
    }

    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
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
        return spacesToDash;
    }
}
