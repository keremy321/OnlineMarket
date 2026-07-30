using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Tests;

internal static class IntegrationApiTestData
{
    public static OrderReadyForErpV1Request CreateValidRequest(
        Guid? eventId = null,
        Guid? orderId = null,
        Guid? customerId = null,
        Guid? correlationId = null)
    {
        return new OrderReadyForErpV1Request
        {
            EventId = eventId ?? Guid.NewGuid(),
            OccurredAtUtc = new DateTime(
                2026,
                7,
                30,
                10,
                0,
                0,
                DateTimeKind.Utc),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            OrderId = orderId ?? Guid.NewGuid(),
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            OrderPlacedAtUtc = new DateTime(
                2026,
                7,
                30,
                9,
                59,
                0,
                DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.CardSimulation,
            Customer = new ErpOrderCustomerV1Request
            {
                CustomerId = customerId ?? Guid.NewGuid(),
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@example.test"
            },
            Address = new ErpOrderAddressV1Request
            {
                RecipientName = "Ada Lovelace",
                PhoneNumber = "+905550000000",
                AddressLine1 = "Integration Street 1",
                District = "Kadikoy",
                City = "Istanbul",
                CountryCode = "TR"
            },
            Totals = new ErpOrderTotalsV1Request
            {
                Subtotal = 100.00m,
                VatTotal = 20.00m,
                GrandTotal = 120.00m,
                Currency = "TRY"
            },
            Items =
            [
                new ErpOrderItemV1Request
                {
                    ProductId = Guid.NewGuid(),
                    Sku = "SKU-001",
                    ProductName = "Integration product",
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    VatRate = 20.00m,
                    NetLineAmount = 100.00m,
                    VatAmount = 20.00m,
                    LineTotal = 120.00m
                }
            ]
        };
    }
}
