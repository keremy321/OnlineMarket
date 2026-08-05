using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OnlineMarket.Web.Tests;

public class SequentialImageSeeder
{
    [Fact]
    public async Task SyncImagesBySequentialIndex()
    {
        var desktopDir = @"C:\Users\Hyperionias\Desktop\görsel çekme\hedef_urun_gorselleri_secili";
        var wwwrootUploadsDir = @"c:\Users\Hyperionias\Desktop\Uyumsoft\OnlineMarket\src\OnlineMarket.Web\wwwroot\uploads\products";
        var catalogJsonPath = @"c:\Users\Hyperionias\Desktop\Uyumsoft\OnlineMarket\scripts\seed\catalog.v1.json";

        if (!Directory.Exists(wwwrootUploadsDir))
        {
            Directory.CreateDirectory(wwwrootUploadsDir);
        }

        // Clean uploads folder completely
        foreach (var oldFile in Directory.GetFiles(wwwrootUploadsDir))
        {
            try { File.Delete(oldFile); } catch { }
        }

        // Get 205 files ordered by numeric prefix (001, 002, ..., 205)
        var sourceFiles = Directory.GetFiles(desktopDir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f =>
            {
                var name = Path.GetFileName(f);
                var numStr = name.Split('_')[0];
                return int.TryParse(numStr, out var num) ? num : 999;
            })
            .ToList();

        var jsonContent = await File.ReadAllTextAsync(catalogJsonPath);
        var jsonNode = JsonNode.Parse(jsonContent)!;
        var productsArray = jsonNode["Products"]!.AsArray();

        int matchedCount = 0;
        for (int i = 0; i < productsArray.Count && i < sourceFiles.Count; i++)
        {
            var sourceFile = sourceFiles[i];
            var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            var targetFileName = $"p_{(i + 1):D3}{ext}";
            var targetPath = Path.Combine(wwwrootUploadsDir, targetFileName);

            File.Copy(sourceFile, targetPath, overwrite: true);

            var relativeUrl = $"/uploads/products/{targetFileName}";
            productsArray[i]!["ImageUrl"] = relativeUrl;

            matchedCount++;
        }

        // Save updated catalog.v1.json
        var updatedJson = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.ReadAllTextAsync(catalogJsonPath); // verify readable
        await File.WriteAllTextAsync(catalogJsonPath, updatedJson);

        Assert.Equal(productsArray.Count, matchedCount);
    }
}
