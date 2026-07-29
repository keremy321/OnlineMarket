using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Infrastructure.Persistence;

public sealed class MockErpStockSeeder(MockErpDbContext context)
{
    private const int DefaultReorderLevel = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<int> SeedAsync(
        string catalogJsonPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(catalogJsonPath))
        {
            throw new ArgumentException(
                "The canonical catalogue path is required.",
                nameof(catalogJsonPath));
        }

        await using var stream = File.OpenRead(catalogJsonPath);
        var catalog = await JsonSerializer.DeserializeAsync<CatalogSeed>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (catalog is null || catalog.Products.Count == 0)
        {
            throw new InvalidDataException(
                "The canonical catalogue contains no products.");
        }

        var existingStocks = await context.ErpStocks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var stocksByProductId = existingStocks.ToDictionary(
            stock => stock.ExternalProductId);
        var stocksBySku = existingStocks.ToDictionary(
            stock => stock.Sku,
            StringComparer.OrdinalIgnoreCase);
        var seededAtUtc = DateTime.UtcNow;
        var insertedCount = 0;

        foreach (var product in catalog.Products)
        {
            Validate(product);

            if (stocksByProductId.TryGetValue(
                product.Id,
                out var existingStock))
            {
                EnsureCanonicalIdentity(existingStock, product);
                continue;
            }

            if (stocksBySku.TryGetValue(product.Sku, out var skuStock))
            {
                throw new InvalidDataException(
                    $"Catalogue SKU '{product.Sku}' is already assigned to " +
                    $"external product '{skuStock.ExternalProductId}'.");
            }

            var stock = new ErpStock
            {
                Id = Guid.NewGuid(),
                ExternalProductId = product.Id,
                Sku = product.Sku,
                ProductName = product.Name,
                UnitType = product.UnitType,
                NetContent = product.NetContent,
                Quantity = product.InitialStock,
                ReorderLevel = DefaultReorderLevel,
                UpdatedAtUtc = seededAtUtc
            };

            context.ErpStocks.Add(stock);
            stocksByProductId.Add(stock.ExternalProductId, stock);
            stocksBySku.Add(stock.Sku, stock);
            insertedCount++;
        }

        if (insertedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return insertedCount;
    }

    private static void Validate(CatalogProduct product)
    {
        if (product.Id == Guid.Empty)
        {
            throw new InvalidDataException(
                "A catalogue product has an empty ID.");
        }

        if (string.IsNullOrWhiteSpace(product.Sku) ||
            product.Sku.Length > 64)
        {
            throw new InvalidDataException(
                $"Catalogue product '{product.Id}' has an invalid SKU.");
        }

        if (string.IsNullOrWhiteSpace(product.Name) ||
            product.Name.Length > 200)
        {
            throw new InvalidDataException(
                $"Catalogue product '{product.Id}' has an invalid name.");
        }

        if (!Enum.IsDefined(product.UnitType))
        {
            throw new InvalidDataException(
                $"Catalogue product '{product.Id}' has an invalid unit type.");
        }

        if (product.NetContent <= 0)
        {
            throw new InvalidDataException(
                $"Catalogue product '{product.Id}' has invalid net content.");
        }

        if (product.InitialStock < 0)
        {
            throw new InvalidDataException(
                $"Catalogue product '{product.Id}' has negative stock.");
        }
    }

    private static void EnsureCanonicalIdentity(
        ErpStock existingStock,
        CatalogProduct product)
    {
        if (!string.Equals(
                existingStock.Sku,
                product.Sku,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                existingStock.ProductName,
                product.Name,
                StringComparison.Ordinal) ||
            existingStock.UnitType != product.UnitType ||
            existingStock.NetContent != product.NetContent)
        {
            throw new InvalidDataException(
                $"Existing ERP stock for external product '{product.Id}' " +
                "does not match the canonical catalogue.");
        }
    }

    private sealed class CatalogSeed
    {
        public List<CatalogProduct> Products { get; init; } = [];
    }

    private sealed class CatalogProduct
    {
        public Guid Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public decimal NetContent { get; init; }

        public UnitType UnitType { get; init; }

        public int InitialStock { get; init; }
    }
}
