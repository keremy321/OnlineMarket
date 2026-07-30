using MockErp.Api.Application.Services;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Tests;

public sealed class MockErpRequestValidatorTests
{
    private readonly MockErpRequestValidator validator = new();

    [Fact]
    public void Ensure_customer_normalizes_contract_fields()
    {
        var request = MockErpApiTestData.CreateEnsureCustomerRequest() with
        {
            FirstName = " Ada ",
            AddressLine2 = " ",
            CountryCode = " tr "
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal("Ada", result.NormalizedRequest.FirstName);
        Assert.Null(result.NormalizedRequest.AddressLine2);
        Assert.Equal("TR", result.NormalizedRequest.CountryCode);
    }

    [Fact]
    public void Order_requires_payment_method_and_unique_products()
    {
        var request = MockErpApiTestData.CreateOrderRequest("CARI-TEST");
        request = request with
        {
            PaymentMethod = (PaymentMethod)0,
            Subtotal = 200m,
            VatTotal = 40m,
            GrandTotal = 240m,
            Lines = [request.Lines![0], request.Lines[0]]
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(nameof(request.PaymentMethod), result.Errors.Keys);
        Assert.Contains("Lines[1].ExternalProductId", result.Errors.Keys);
    }

    [Fact]
    public void Stock_sales_require_negative_unique_product_changes()
    {
        var productId = Guid.NewGuid();
        var request = new Contracts.CreateStockMovementsRequest
        {
            ExternalOrderId = Guid.NewGuid(),
            Lines =
            [
                new Contracts.CreateStockMovementLineRequest
                {
                    ExternalProductId = productId,
                    QuantityChange = 1
                },
                new Contracts.CreateStockMovementLineRequest
                {
                    ExternalProductId = productId,
                    QuantityChange = -1
                }
            ]
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("Lines[0].QuantityChange", result.Errors.Keys);
        Assert.Contains("Lines[1].ExternalProductId", result.Errors.Keys);
    }

    [Fact]
    public void Canonical_hash_is_stable_after_normalization_and_changes_with_payment()
    {
        var request = MockErpApiTestData.CreateOrderRequest("CARI-TEST");
        var padded = request with
        {
            ErpCustomerCode = " cari-test ",
            Currency = "try"
        };
        var first = validator.Validate(request).NormalizedRequest;
        var second = validator.Validate(padded).NormalizedRequest;

        Assert.Equal(
            CanonicalRequestHasher.Compute(first),
            CanonicalRequestHasher.Compute(second));
        Assert.NotEqual(
            CanonicalRequestHasher.Compute(first),
            CanonicalRequestHasher.Compute(first with
            {
                PaymentMethod = PaymentMethod.TransferSimulation
            }));
    }
}
