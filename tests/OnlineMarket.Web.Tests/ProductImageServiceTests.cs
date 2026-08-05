using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class ProductImageServiceTests
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class FakeFormFile : IFormFile
    {
        public FakeFormFile(string fileName, long length = 100)
        {
            FileName = fileName;
            Length = length;
        }

        public string ContentType => "image/jpeg";
        public string ContentDisposition => $"form-data; name=\"ImageFile\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length { get; }
        public string Name => "ImageFile";
        public string FileName { get; }

        public void CopyTo(Stream target) { }
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Stream OpenReadStream() => new MemoryStream();
    }

    private static OnlineMarketDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OnlineMarketDbContext(options);
    }

    [Theory]
    [InlineData("Spagetti 500g", "spagetti-500g")]
    [InlineData("Çorbalık Makarna & Sos", "corbalik-makarna-ve-sos")]
    [InlineData("Şarküteri Zeytinyağı 1L!", "sarkuteri-zeytinyagi-1l")]
    public void Slugify_Produces_Clean_Normalized_Slugs(string input, string expected)
    {
        var result = ProductImageService.Slugify(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SaveProductImageAsync_Throws_On_Invalid_Extension()
    {
        var env = new FakeWebHostEnvironment();
        var dbContext = CreateDbContext();
        var stockMutationMock = new SqlServerStockMutationService(dbContext);
        var catalogService = new CatalogService(dbContext, stockMutationMock);
        var service = new ProductImageService(env, dbContext, catalogService, NullLogger<ProductImageService>.Instance);

        var file = new FakeFormFile("malicious.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveProductImageAsync(file, "Test Product", "PRD-001"));
    }

    [Fact]
    public async Task BulkMatchImagesFromFolderAsync_Matches_Products_By_Name_Slug()
    {
        var tempWebRoot = Path.Combine(Path.GetTempPath(), "OnlineMarketTest_" + Guid.NewGuid().ToString("N"));
        var uploadsDir = Path.Combine(tempWebRoot, "uploads", "products");
        Directory.CreateDirectory(uploadsDir);

        try
        {
            // Create dummy image file
            var testImagePath = Path.Combine(uploadsDir, "spagetti-500g.jpg");
            await File.WriteAllTextAsync(testImagePath, "dummy image content");

            var env = new FakeWebHostEnvironment { WebRootPath = tempWebRoot };

            var dbContext = CreateDbContext();
            var categoryId = Guid.NewGuid();
            var brandId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var category = new Category { Id = categoryId, Name = "Makarna", Slug = "makarna", IsActive = true };
            var brand = new Brand { Id = brandId, Name = "Anadolu", Slug = "anadolu", IsActive = true };
            var product = new Product
            {
                Id = productId,
                Sku = "MKR-002",
                Name = "Spagetti 500g",
                Slug = "spagetti-500g",
                CategoryId = categoryId,
                BrandId = brandId,
                Price = 25m,
                VatRate = 10m,
                NetContent = 500m,
                UnitType = UnitType.Gram,
                ImageUrl = null,
                IsActive = true
            };

            dbContext.Categories.Add(category);
            dbContext.Brands.Add(brand);
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var stockMutationMock = new SqlServerStockMutationService(dbContext);
            var catalogService = new CatalogService(dbContext, stockMutationMock);
            var service = new ProductImageService(env, dbContext, catalogService, NullLogger<ProductImageService>.Instance);

            var count = await service.BulkMatchImagesFromFolderAsync();

            Assert.Equal(1, count);
            var updatedProduct = await dbContext.Products.FindAsync(productId);
            Assert.NotNull(updatedProduct);
            Assert.Equal("/uploads/products/spagetti-500g.jpg", updatedProduct.ImageUrl);
        }
        finally
        {
            if (Directory.Exists(tempWebRoot))
            {
                Directory.Delete(tempWebRoot, true);
            }
        }
    }
}
