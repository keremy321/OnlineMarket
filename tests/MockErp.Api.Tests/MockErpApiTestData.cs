using System.Text.Json;
using MockErp.Api.Application.Models;
using MockErp.Api.Application.Services;
using MockErp.Api.Contracts;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;
using MockErp.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace MockErp.Api.Tests;

internal static class MockErpApiTestData
{
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static EnsureCustomerRequest CreateEnsureCustomerRequest(
        Guid? externalCustomerId = null)
    {
        return new EnsureCustomerRequest
        {
            ExternalCustomerId = externalCustomerId ?? Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.test",
            PhoneNumber = "+905550000000",
            AddressLine1 = "Customer Street 1",
            District = "Kadikoy",
            City = "Istanbul",
            CountryCode = "TR"
        };
    }

    public static CreateOrderRequest CreateOrderRequest(
        string erpCustomerCode,
        Guid? externalOrderId = null,
        Guid? externalProductId = null,
        DateTime? placedAtUtc = null)
    {
        return new CreateOrderRequest
        {
            ExternalOrderId = externalOrderId ?? Guid.NewGuid(),
            MarketOrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            ErpCustomerCode = erpCustomerCode,
            OrderPlacedAtUtc = placedAtUtc ?? new DateTime(
                2026,
                7,
                30,
                12,
                0,
                0,
                DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.CardSimulation,
            Subtotal = 100m,
            VatTotal = 20m,
            GrandTotal = 120m,
            Currency = "TRY",
            Address = new CreateOrderAddressRequest
            {
                RecipientName = "Ada Lovelace",
                PhoneNumber = "+905550000000",
                AddressLine1 = "Delivery Street 1",
                District = "Kadikoy",
                City = "Istanbul",
                CountryCode = "TR"
            },
            Lines =
            [
                new CreateOrderLineRequest
                {
                    ExternalProductId =
                        externalProductId ?? Guid.NewGuid(),
                    Sku = $"SKU-{Guid.NewGuid():N}",
                    ProductName = "Mock ERP test product",
                    Quantity = 2,
                    UnitPrice = 50m,
                    VatRate = 20m,
                    NetLineAmount = 100m,
                    VatAmount = 20m,
                    LineTotal = 120m
                }
            ]
        };
    }

    public static ErpStock CreateStock(
        Guid externalProductId,
        int quantity)
    {
        return new ErpStock
        {
            Id = Guid.NewGuid(),
            ExternalProductId = externalProductId,
            Sku = $"SKU-{Guid.NewGuid():N}",
            ProductName = "Stock test product",
            UnitType = UnitType.Piece,
            NetContent = 1m,
            Quantity = quantity,
            ReorderLevel = 1,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public static MockErpService CreateService(MockErpDbContext context)
    {
        var store = new SqlServerMockErpStore(
            context,
            NullLogger<SqlServerMockErpStore>.Instance);
        return new MockErpService(
            store,
            new MockErpRequestValidator(),
            TimeProvider.System,
            NullLogger<MockErpService>.Instance);
    }

    public static T Read<T>(OperationResult result)
    {
        Assert.True(result.Succeeded);
        return JsonSerializer.Deserialize<T>(
                result.Response!.Body,
                WebJsonOptions)
            ?? throw new InvalidOperationException(
                "The stored response could not be deserialized.");
    }
}
