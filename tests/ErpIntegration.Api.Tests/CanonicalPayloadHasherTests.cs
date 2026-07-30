using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Tests;

public sealed class CanonicalPayloadHasherTests
{
    [Fact]
    public void Canonical_hash_is_lowercase_sha256_and_stable_after_normalization()
    {
        var request = IntegrationApiTestData.CreateValidRequest();
        var paddedRequest = request with
        {
            OrderNumber = $" {request.OrderNumber} ",
            Customer = request.Customer! with
            {
                FirstName = $" {request.Customer.FirstName} "
            },
            Address = request.Address! with
            {
                CountryCode = "tr"
            },
            Totals = request.Totals! with
            {
                Currency = "try"
            }
        };

        var first = CanonicalPayloadHasher.Compute(
            OrderReadyForErpV1Normalizer.Normalize(request));
        var second = CanonicalPayloadHasher.Compute(
            OrderReadyForErpV1Normalizer.Normalize(paddedRequest));

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Required_payment_method_participates_in_the_hash()
    {
        var request = IntegrationApiTestData.CreateValidRequest();
        var changed = request with
        {
            PaymentMethod = PaymentMethod.TransferSimulation
        };

        var first = CanonicalPayloadHasher.Compute(request);
        var second = CanonicalPayloadHasher.Compute(changed);

        Assert.NotEqual(first, second);
    }
}
