using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Tests;

public sealed class OrderReadyForErpV1ValidatorTests
{
    private readonly OrderReadyForErpV1Validator validator = new();

    [Fact]
    public void Valid_payload_is_normalized_and_accepted()
    {
        var request = IntegrationApiTestData.CreateValidRequest();
        request = request with
        {
            OrderNumber = $"  {request.OrderNumber}  ",
            Customer = request.Customer! with
            {
                FirstName = "  Ada  "
            },
            Address = request.Address! with
            {
                AddressLine2 = "   ",
                CountryCode = " tr "
            },
            Totals = request.Totals! with
            {
                Currency = " try "
            }
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(request.OrderNumber.Trim(), result.NormalizedRequest.OrderNumber);
        Assert.Equal("Ada", result.NormalizedRequest.Customer!.FirstName);
        Assert.Null(result.NormalizedRequest.Address!.AddressLine2);
        Assert.Equal("TR", result.NormalizedRequest.Address.CountryCode);
        Assert.Equal("TRY", result.NormalizedRequest.Totals!.Currency);
    }

    [Fact]
    public void Payment_method_is_required()
    {
        var request = IntegrationApiTestData.CreateValidRequest() with
        {
            PaymentMethod = (PaymentMethod)0
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(nameof(request.PaymentMethod), result.Errors.Keys);
    }

    [Fact]
    public void Duplicate_product_ids_are_rejected_without_merging()
    {
        var request = IntegrationApiTestData.CreateValidRequest();
        var duplicate = request.Items![0]!;
        request = request with
        {
            Totals = request.Totals! with
            {
                Subtotal = 200m,
                VatTotal = 40m,
                GrandTotal = 240m
            },
            Items = [duplicate, duplicate]
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("Items[1].ProductId", result.Errors.Keys);
    }

    [Fact]
    public void Line_and_header_calculation_mismatches_are_rejected()
    {
        var request = IntegrationApiTestData.CreateValidRequest();
        request = request with
        {
            Items =
            [
                request.Items![0]! with
                {
                    NetLineAmount = 99m,
                    LineTotal = 119m
                }
            ]
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("Items[0].NetLineAmount", result.Errors.Keys);
        Assert.Contains("Totals.Subtotal", result.Errors.Keys);
        Assert.Contains("Totals.GrandTotal", result.Errors.Keys);
    }
}
