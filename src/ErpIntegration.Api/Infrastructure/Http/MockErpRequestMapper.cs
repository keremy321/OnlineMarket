using System.Text.Json;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Infrastructure.Http;

internal sealed record MockErpMappedRequest(
    string Operation,
    string Path,
    object Payload,
    string MaskedPayload);

internal sealed class MockErpRequestMappingException(
    string code,
    string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

internal static class MockErpRequestMapper
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static MockErpMappedRequest Map(ClaimedIntegrationStep step)
    {
        return step.StepType switch
        {
            IntegrationStepType.EnsureCustomer => EnsureCustomer(step),
            IntegrationStepType.CreateOrder => CreateOrder(step),
            IntegrationStepType.CreateStockMovement =>
                CreateStockMovements(step),
            IntegrationStepType.CreateAccountingEntry =>
                CreateAccountingEntry(step),
            _ => throw new MockErpRequestMappingException(
                "Integration.StepTypeUnsupported",
                "The integration step type is not supported.")
        };
    }

    private static MockErpMappedRequest EnsureCustomer(
        ClaimedIntegrationStep step)
    {
        var snapshot = step.Snapshot;
        var payload = new MockErpEnsureCustomerRequest(
            step.CustomerId,
            snapshot.FirstName,
            snapshot.LastName,
            snapshot.Email,
            snapshot.PhoneNumber,
            snapshot.AddressLine1,
            snapshot.AddressLine2,
            snapshot.District,
            snapshot.City,
            snapshot.PostalCode,
            snapshot.CountryCode);
        return new MockErpMappedRequest(
            "EnsureCustomer",
            "api/v1/customers/ensure",
            payload,
            Mask(new
            {
                operation = "EnsureCustomer",
                externalCustomerId = step.CustomerId
            }));
    }

    private static MockErpMappedRequest CreateOrder(
        ClaimedIntegrationStep step)
    {
        if (string.IsNullOrWhiteSpace(step.ErpCustomerCode))
        {
            throw new MockErpRequestMappingException(
                "Integration.ErpCustomerLinkMissing",
                "The ERP customer link is missing.");
        }

        var snapshot = step.Snapshot;
        var payload = new MockErpCreateOrderRequest(
            step.MarketOrderId,
            step.OrderNumber,
            step.ErpCustomerCode,
            AsUtc(snapshot.OrderPlacedAtUtc),
            snapshot.PaymentMethod,
            snapshot.Subtotal,
            snapshot.VatTotal,
            snapshot.GrandTotal,
            snapshot.Currency,
            new MockErpCreateOrderAddressRequest(
                snapshot.RecipientName,
                snapshot.PhoneNumber,
                snapshot.AddressLine1,
                snapshot.AddressLine2,
                snapshot.District,
                snapshot.City,
                snapshot.PostalCode,
                snapshot.CountryCode),
            step.Lines
                .OrderBy(line => line.ProductId)
                .Select(line => new MockErpCreateOrderLineRequest(
                    line.ProductId,
                    line.Sku,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.VatRate,
                    line.NetLineAmount,
                    line.VatAmount,
                    line.LineTotal))
                .ToArray());
        return new MockErpMappedRequest(
            "CreateOrder",
            "api/v1/orders",
            payload,
            Mask(new
            {
                operation = "CreateOrder",
                externalOrderId = step.MarketOrderId,
                step.OrderNumber,
                step.CustomerId,
                lineCount = step.Lines.Count,
                snapshot.Subtotal,
                snapshot.VatTotal,
                snapshot.GrandTotal,
                snapshot.Currency
            }));
    }

    private static MockErpMappedRequest CreateStockMovements(
        ClaimedIntegrationStep step)
    {
        var payload = new MockErpCreateStockMovementsRequest(
            step.MarketOrderId,
            step.Lines
                .OrderBy(line => line.ProductId)
                .Select(line => new MockErpCreateStockMovementLineRequest(
                    line.ProductId,
                    checked(-line.Quantity)))
                .ToArray());
        return new MockErpMappedRequest(
            "CreateStockMovement",
            "api/v1/stock-movements",
            payload,
            Mask(new
            {
                operation = "CreateStockMovement",
                externalOrderId = step.MarketOrderId,
                lines = step.Lines
                    .OrderBy(line => line.ProductId)
                    .Select(line => new
                    {
                        externalProductId = line.ProductId,
                        quantityChange = checked(-line.Quantity)
                    })
            }));
    }

    private static MockErpMappedRequest CreateAccountingEntry(
        ClaimedIntegrationStep step)
    {
        var entryDateUtc = AsUtc(step.Snapshot.OrderPlacedAtUtc);
        var payload = new MockErpCreateAccountingEntryRequest(
            step.MarketOrderId,
            entryDateUtc,
            $"Sales voucher for order {step.OrderNumber}");
        return new MockErpMappedRequest(
            "CreateAccountingEntry",
            "api/v1/accounting-entries",
            payload,
            Mask(new
            {
                operation = "CreateAccountingEntry",
                externalOrderId = step.MarketOrderId,
                entryDateUtc
            }));
    }

    private static DateTime AsUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static string Mask<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
