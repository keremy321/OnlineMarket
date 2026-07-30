using ErpIntegration.Api.Contracts;

namespace ErpIntegration.Api.Application.Services;

public static class OrderReadyForErpV1Normalizer
{
    public static OrderReadyForErpV1Request Normalize(
        OrderReadyForErpV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            OrderNumber = Trim(request.OrderNumber),
            Customer = request.Customer is null
                ? null
                : request.Customer with
                {
                    FirstName = Trim(request.Customer.FirstName),
                    LastName = Trim(request.Customer.LastName),
                    Email = Trim(request.Customer.Email)
                },
            Address = request.Address is null
                ? null
                : request.Address with
                {
                    RecipientName = Trim(request.Address.RecipientName),
                    PhoneNumber = Trim(request.Address.PhoneNumber),
                    AddressLine1 = Trim(request.Address.AddressLine1),
                    AddressLine2 = TrimToNull(request.Address.AddressLine2),
                    District = Trim(request.Address.District),
                    City = Trim(request.Address.City),
                    PostalCode = TrimToNull(request.Address.PostalCode),
                    CountryCode = Trim(request.Address.CountryCode)
                        ?.ToUpperInvariant()
                },
            Totals = request.Totals is null
                ? null
                : request.Totals with
                {
                    Currency = Trim(request.Totals.Currency)
                        ?.ToUpperInvariant()
                },
            Items = request.Items?
                .Select(item => item is null
                    ? null
                    : item with
                    {
                        Sku = Trim(item.Sku),
                        ProductName = Trim(item.ProductName)
                    })
                .ToArray()
        };
    }

    private static string? Trim(string? value)
    {
        return value?.Trim();
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
