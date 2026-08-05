using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class BulkDatasetSeeder
{
    private static readonly Dictionary<string, string[]> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Makarna & Sos"] = ["PASTA", "TOMATO_PASTE"],
        ["Kahvaltılık"] = ["JAM", "HONEY", "MILK"],
        ["Bakliyat"] = ["BEANS", "RICE", "FLOUR", "CORN"],
        ["Baharat & Harç"] = ["SPICES", "TOMATO_PASTE"],
        ["Konserve"] = ["FISH", "CORN", "BEANS"],
        ["Kahve & Çay"] = ["COFFEE", "TEA"],
        ["Gazlı İçecek"] = ["SODA", "WATER"],
        ["Süt Ürünleri"] = ["MILK"],
        ["Meyve Suyu"] = ["JUICE"],
        ["Cips & Kraker"] = ["CHIPS"],
        ["Çikolata & Şekerleme"] = ["CHOCOLATE", "CANDY"],
        ["Kuruyemiş"] = ["NUTS"],
        ["Deterjan & Yumuşatıcı"] = ["PACKAGES"],
        ["Kağıt Ürünleri"] = ["PACKAGES"],
        ["Bebek Bakım"] = ["MILK", "PACKAGES"],
        ["Bebek Maması"] = ["CEREAL", "MILK"],
        ["Ekmek & Unlu Mamül"] = ["CAKE", "CEREAL"],
        ["Pasta & Tatlı"] = ["CAKE", "CHOCOLATE"],
        ["Peynir & Zeytin"] = ["MILK", "OIL"],
        ["Şarküteri Et"] = ["FISH", "PACKAGES"],
        ["Saç Bakımı"] = ["PACKAGES"],
        ["Ağız Bakımı"] = ["PACKAGES"],
        ["Dondurulmuş Hazır Yemek"] = ["FISH", "PASTA", "PACKAGES"]
    };

    [Fact]
    public async Task PopulateAllProductsWithExtractedImages()
    {
        var freiburgDir = @"C:\Users\Hyperionias\Desktop\görseller\freiburg_extracted\images";
        var targetUploadsDir = @"c:\Users\Hyperionias\Desktop\Uyumsoft\OnlineMarket\src\OnlineMarket.Web\wwwroot\uploads\products";

        if (!Directory.Exists(targetUploadsDir))
        {
            Directory.CreateDirectory(targetUploadsDir);
        }

        var freiburgImages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(freiburgDir))
        {
            foreach (var subDir in Directory.GetDirectories(freiburgDir))
            {
                var folderName = Path.GetFileName(subDir);
                var images = Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                freiburgImages[folderName] = images;
            }
        }

        var catalogSeedPath = @"C:\Users\Hyperionias\Desktop\Uyumsoft\OnlineMarket\scripts\seed\catalog.v1.json";
        if (!File.Exists(catalogSeedPath))
        {
            catalogSeedPath = Path.Combine(AppContext.BaseDirectory, "Seed", "catalog.v1.json");
        }

        var catalogJson = await File.ReadAllTextAsync(catalogSeedPath);
        var doc = System.Text.Json.JsonDocument.Parse(catalogJson);

        var productIndexMap = new Dictionary<string, int>();

        var options = new DbContextOptionsBuilder<OnlineMarketDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var dbContext = new OnlineMarketDbContext(options);

        // Populate all 205 product images
        var products = doc.RootElement.GetProperty("Products").EnumerateArray().ToList();
        var categories = doc.RootElement.GetProperty("Categories").EnumerateArray()
            .ToDictionary(c => c.GetProperty("Id").GetString()!, c => c.GetProperty("Name").GetString()!);

        int copiedCount = 0;
        foreach (var p in products)
        {
            var sku = p.GetProperty("Sku").GetString()!;
            var name = p.GetProperty("Name").GetString()!;
            var categoryId = p.GetProperty("CategoryId").GetString()!;
            categories.TryGetValue(categoryId, out var catName);

            catName ??= "Gıda";
            var slug = ProductImageService.Slugify(name);

            string? selectedSourceImage = null;

            // Check if Freiburg category matches
            if (CategoryMap.TryGetValue(catName, out var freiburgFolders))
            {
                foreach (var folder in freiburgFolders)
                {
                    if (freiburgImages.TryGetValue(folder, out var folderImgs) && folderImgs.Count > 0)
                    {
                        productIndexMap.TryGetValue(folder, out var idx);
                        selectedSourceImage = folderImgs[idx % folderImgs.Count];
                        productIndexMap[folder] = idx + 1;
                        break;
                    }
                }
            }

            // Fallback to general pasta/cereal/milk if not found
            if (selectedSourceImage == null && freiburgImages.TryGetValue("PASTA", out var fallbackImgs) && fallbackImgs.Count > 0)
            {
                productIndexMap.TryGetValue("PASTA", out var idx);
                selectedSourceImage = fallbackImgs[idx % fallbackImgs.Count];
                productIndexMap["PASTA"] = idx + 1;
            }

            if (selectedSourceImage != null && File.Exists(selectedSourceImage))
            {
                var ext = Path.GetExtension(selectedSourceImage);
                var destFileName = $"{slug}{ext}";
                var destPath = Path.Combine(targetUploadsDir, destFileName);

                File.Copy(selectedSourceImage, destPath, overwrite: true);
                copiedCount++;
            }
        }

        Assert.True(copiedCount > 0);
    }
}
