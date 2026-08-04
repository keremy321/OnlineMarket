using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Infrastructure.Importing;

public sealed record DemoExcelImportResult(
    bool AlreadyImported,
    int Categories,
    int Brands,
    int Products,
    int Customers,
    int CustomerAddresses,
    int Orders,
    int OrderItems,
    int ProductOutboxMessages,
    int RecommendationOutboxMessages,
    int ErpOutboxMessages);

public sealed class DemoExcelImportException : Exception
{
    public DemoExcelImportException(string message)
        : base(message)
    {
    }
}

internal sealed record DemoImportDocument(
    IReadOnlyList<DemoCategoryRow> Categories,
    IReadOnlyList<DemoBrandRow> Brands,
    IReadOnlyList<DemoProductRow> Products,
    IReadOnlyList<DemoCustomerRow> Customers,
    IReadOnlyList<DemoCustomerAddressRow> CustomerAddresses,
    IReadOnlyList<DemoOrderRow> Orders,
    IReadOnlyList<DemoOrderItemRow> OrderItems,
    IReadOnlyDictionary<Guid, int> FinalStockByProductId,
    DateTime CatalogueOccurredAtUtc);

internal sealed record DemoCategoryRow(
    Guid Id,
    Guid? ParentCategoryId,
    string Name,
    string Slug);

internal sealed record DemoBrandRow(
    Guid Id,
    string Name,
    string Slug);

internal sealed record DemoProductRow(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    Guid CategoryId,
    Guid BrandId,
    decimal Price,
    decimal VatRate,
    decimal NetContent,
    UnitType UnitType,
    int InitialStock);

internal sealed record DemoCustomerRow(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Persona);

internal sealed record DemoCustomerAddressRow(
    Guid Id,
    Guid CustomerId,
    string RecipientName,
    string PhoneNumber,
    string AddressLine1,
    string District,
    string City,
    string? PostalCode,
    string CountryCode,
    bool IsDefault);

internal sealed record DemoOrderRow(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    DateTime PlacedAtUtc,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal);

internal sealed record DemoOrderItemRow(
    Guid OrderId,
    Guid ProductId,
    string SkuSnapshot,
    string ProductNameSnapshot,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetLineAmount,
    decimal VatAmount,
    decimal LineTotal);
