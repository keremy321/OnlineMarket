using System.ComponentModel.DataAnnotations;
using MockErp.Api.Contracts;

namespace MockErp.Api.Application.Services;

public sealed record RequestValidationResult<T>(
    T NormalizedRequest,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class MockErpRequestValidator
{
    private const decimal MaximumMoney = 9999999999999999.99m;
    private static readonly EmailAddressAttribute EmailValidator = new();

    public RequestValidationResult<EnsureCustomerRequest> Validate(
        EnsureCustomerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request with
        {
            FirstName = Trim(request.FirstName),
            LastName = Trim(request.LastName),
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            AddressLine1 = Trim(request.AddressLine1),
            AddressLine2 = TrimToNull(request.AddressLine2),
            District = Trim(request.District),
            City = Trim(request.City),
            PostalCode = TrimToNull(request.PostalCode),
            CountryCode = Trim(request.CountryCode)?.ToUpperInvariant()
        };
        var errors = NewErrors();
        RequiredGuid(errors, nameof(request.ExternalCustomerId), value.ExternalCustomerId);
        Required(errors, nameof(request.FirstName), value.FirstName, 100);
        Required(errors, nameof(request.LastName), value.LastName, 100);
        Required(errors, nameof(request.Email), value.Email, 256);
        if (!string.IsNullOrEmpty(value.Email)
            && !EmailValidator.IsValid(value.Email))
        {
            Add(errors, nameof(request.Email), "Email must be valid.");
        }

        Required(errors, nameof(request.PhoneNumber), value.PhoneNumber, 30);
        Required(errors, nameof(request.AddressLine1), value.AddressLine1, 250);
        Optional(errors, nameof(request.AddressLine2), value.AddressLine2, 250);
        Required(errors, nameof(request.District), value.District, 100);
        Required(errors, nameof(request.City), value.City, 100);
        Optional(errors, nameof(request.PostalCode), value.PostalCode, 20);
        Country(errors, nameof(request.CountryCode), value.CountryCode);
        return Result(value, errors);
    }

    public RequestValidationResult<CreateOrderRequest> Validate(
        CreateOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request with
        {
            MarketOrderNumber = Trim(request.MarketOrderNumber),
            ErpCustomerCode = Trim(request.ErpCustomerCode)?.ToUpperInvariant(),
            Currency = Trim(request.Currency)?.ToUpperInvariant(),
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
            Lines = request.Lines?
                .Select(line => line is null
                    ? null
                    : line with
                    {
                        Sku = Trim(line.Sku),
                        ProductName = Trim(line.ProductName)
                    })
                .ToArray()
        };
        var errors = NewErrors();
        RequiredGuid(errors, nameof(request.ExternalOrderId), value.ExternalOrderId);
        Required(errors, nameof(request.MarketOrderNumber), value.MarketOrderNumber, 32);
        Required(errors, nameof(request.ErpCustomerCode), value.ErpCustomerCode, 50);
        Utc(errors, nameof(request.OrderPlacedAtUtc), value.OrderPlacedAtUtc);
        if (!Enum.IsDefined(value.PaymentMethod))
        {
            Add(
                errors,
                nameof(request.PaymentMethod),
                "PaymentMethod is required and must be supported.");
        }

        Money(errors, nameof(request.Subtotal), value.Subtotal);
        Money(errors, nameof(request.VatTotal), value.VatTotal);
        Money(errors, nameof(request.GrandTotal), value.GrandTotal);
        if (value.GrandTotal != value.Subtotal + value.VatTotal)
        {
            Add(
                errors,
                nameof(request.GrandTotal),
                "GrandTotal must equal Subtotal plus VatTotal.");
        }

        Required(errors, nameof(request.Currency), value.Currency, 3);
        if (!string.IsNullOrEmpty(value.Currency)
            && value.Currency != "TRY")
        {
            Add(errors, nameof(request.Currency), "Currency must be TRY.");
        }

        ValidateAddress(errors, value.Address);
        ValidateOrderLines(errors, value);
        return Result(value, errors);
    }

    public RequestValidationResult<CreateStockMovementsRequest> Validate(
        CreateStockMovementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = NewErrors();
        RequiredGuid(errors, nameof(request.ExternalOrderId), request.ExternalOrderId);
        if (request.Lines is null || request.Lines.Count == 0)
        {
            Add(errors, nameof(request.Lines), "At least one stock movement is required.");
            return Result(request, errors);
        }

        var productIds = new HashSet<Guid>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var prefix = $"Lines[{index}]";
            if (line is null)
            {
                Add(errors, prefix, $"{prefix} must not be null.");
                continue;
            }

            RequiredGuid(errors, $"{prefix}.ExternalProductId", line.ExternalProductId);
            if (line.ExternalProductId != Guid.Empty
                && !productIds.Add(line.ExternalProductId))
            {
                Add(
                    errors,
                    $"{prefix}.ExternalProductId",
                    "Each ExternalProductId may appear at most once.");
            }

            if (line.QuantityChange >= 0)
            {
                Add(
                    errors,
                    $"{prefix}.QuantityChange",
                    "Sales stock movements require a negative QuantityChange.");
            }
        }

        return Result(request, errors);
    }

    public RequestValidationResult<CreateAccountingEntryRequest> Validate(
        CreateAccountingEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request with
        {
            Description = Trim(request.Description)
        };
        var errors = NewErrors();
        RequiredGuid(errors, nameof(request.ExternalOrderId), value.ExternalOrderId);
        Utc(errors, nameof(request.EntryDateUtc), value.EntryDateUtc);
        Required(errors, nameof(request.Description), value.Description, 500);
        return Result(value, errors);
    }

    private static void ValidateAddress(
        Dictionary<string, List<string>> errors,
        CreateOrderAddressRequest? address)
    {
        if (address is null)
        {
            Add(errors, "Address", "Address is required.");
            return;
        }

        Required(errors, "Address.RecipientName", address.RecipientName, 200);
        Required(errors, "Address.PhoneNumber", address.PhoneNumber, 30);
        Required(errors, "Address.AddressLine1", address.AddressLine1, 250);
        Optional(errors, "Address.AddressLine2", address.AddressLine2, 250);
        Required(errors, "Address.District", address.District, 100);
        Required(errors, "Address.City", address.City, 100);
        Optional(errors, "Address.PostalCode", address.PostalCode, 20);
        Country(errors, "Address.CountryCode", address.CountryCode);
    }

    private static void ValidateOrderLines(
        Dictionary<string, List<string>> errors,
        CreateOrderRequest request)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            Add(errors, nameof(request.Lines), "At least one order line is required.");
            return;
        }

        var productIds = new HashSet<Guid>();
        decimal subtotal = 0;
        decimal vatTotal = 0;
        decimal grandTotal = 0;
        var overflowed = false;

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var prefix = $"Lines[{index}]";
            if (line is null)
            {
                Add(errors, prefix, $"{prefix} must not be null.");
                continue;
            }

            RequiredGuid(errors, $"{prefix}.ExternalProductId", line.ExternalProductId);
            if (line.ExternalProductId != Guid.Empty
                && !productIds.Add(line.ExternalProductId))
            {
                Add(
                    errors,
                    $"{prefix}.ExternalProductId",
                    "Each ExternalProductId may appear at most once.");
            }

            Required(errors, $"{prefix}.Sku", line.Sku, 64);
            Required(errors, $"{prefix}.ProductName", line.ProductName, 200);
            if (line.Quantity <= 0)
            {
                Add(errors, $"{prefix}.Quantity", "Quantity must be positive.");
            }

            Money(errors, $"{prefix}.UnitPrice", line.UnitPrice);
            Vat(errors, $"{prefix}.VatRate", line.VatRate);
            Money(errors, $"{prefix}.NetLineAmount", line.NetLineAmount);
            Money(errors, $"{prefix}.VatAmount", line.VatAmount);
            Money(errors, $"{prefix}.LineTotal", line.LineTotal);
            if (line.LineTotal != line.NetLineAmount + line.VatAmount)
            {
                Add(
                    errors,
                    $"{prefix}.LineTotal",
                    "LineTotal must equal NetLineAmount plus VatAmount.");
            }

            if (line.Quantity > 0
                && line.UnitPrice >= 0
                && line.VatRate is >= 0 and <= 100)
            {
                try
                {
                    var expectedNet = decimal.Round(
                        checked(line.UnitPrice * line.Quantity),
                        2,
                        MidpointRounding.AwayFromZero);
                    var expectedVat = decimal.Round(
                        checked(expectedNet * line.VatRate / 100m),
                        2,
                        MidpointRounding.AwayFromZero);
                    if (line.NetLineAmount != expectedNet)
                    {
                        Add(
                            errors,
                            $"{prefix}.NetLineAmount",
                            "NetLineAmount does not match price and quantity.");
                    }

                    if (line.VatAmount != expectedVat)
                    {
                        Add(
                            errors,
                            $"{prefix}.VatAmount",
                            "VatAmount does not match the rounded VAT calculation.");
                    }
                }
                catch (OverflowException)
                {
                    Add(errors, prefix, "Calculated line amounts are outside the supported range.");
                }
            }

            if (!overflowed)
            {
                try
                {
                    subtotal = checked(subtotal + line.NetLineAmount);
                    vatTotal = checked(vatTotal + line.VatAmount);
                    grandTotal = checked(grandTotal + line.LineTotal);
                }
                catch (OverflowException)
                {
                    overflowed = true;
                    Add(errors, nameof(request.Lines), "Line totals are outside the supported range.");
                }
            }
        }

        if (!overflowed)
        {
            if (request.Subtotal != subtotal)
            {
                Add(errors, nameof(request.Subtotal), "Subtotal must equal the sum of line net amounts.");
            }

            if (request.VatTotal != vatTotal)
            {
                Add(errors, nameof(request.VatTotal), "VatTotal must equal the sum of line VAT amounts.");
            }

            if (request.GrandTotal != grandTotal)
            {
                Add(errors, nameof(request.GrandTotal), "GrandTotal must equal the sum of line totals.");
            }
        }
    }

    private static Dictionary<string, List<string>> NewErrors()
    {
        return new Dictionary<string, List<string>>(StringComparer.Ordinal);
    }

    private static RequestValidationResult<T> Result<T>(
        T value,
        Dictionary<string, List<string>> errors)
    {
        return new RequestValidationResult<T>(
            value,
            errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));
    }

    private static void RequiredGuid(
        Dictionary<string, List<string>> errors,
        string path,
        Guid value)
    {
        if (value == Guid.Empty)
        {
            Add(errors, path, $"{path} is required.");
        }
    }

    private static void Required(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, path, $"{path} is required.");
        }
        else if (value.Length > maximumLength)
        {
            Add(errors, path, $"{path} must not exceed {maximumLength} characters.");
        }
    }

    private static void Optional(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        int maximumLength)
    {
        if (value?.Length > maximumLength)
        {
            Add(errors, path, $"{path} must not exceed {maximumLength} characters.");
        }
    }

    private static void Country(
        Dictionary<string, List<string>> errors,
        string path,
        string? value)
    {
        Required(errors, path, value, 2);
        if (!string.IsNullOrEmpty(value) && value != "TR")
        {
            Add(errors, path, $"{path} must be TR.");
        }
    }

    private static void Utc(
        Dictionary<string, List<string>> errors,
        string path,
        DateTime value)
    {
        if (value == default)
        {
            Add(errors, path, $"{path} is required.");
        }
        else if (value.Kind != DateTimeKind.Utc)
        {
            Add(errors, path, $"{path} must be UTC.");
        }
    }

    private static void Money(
        Dictionary<string, List<string>> errors,
        string path,
        decimal value)
    {
        if (value < 0 || value > MaximumMoney || decimal.Round(value, 2) != value)
        {
            Add(errors, path, $"{path} must fit non-negative decimal(18,2).");
        }
    }

    private static void Vat(
        Dictionary<string, List<string>> errors,
        string path,
        decimal value)
    {
        if (value is < 0 or > 100 || decimal.Round(value, 2) != value)
        {
            Add(errors, path, $"{path} must fit decimal(5,2) between 0 and 100.");
        }
    }

    private static void Add(
        Dictionary<string, List<string>> errors,
        string path,
        string message)
    {
        if (!errors.TryGetValue(path, out var messages))
        {
            messages = [];
            errors[path] = messages;
        }

        messages.Add(message);
    }

    private static string? Trim(string? value)
    {
        return value?.Trim();
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = Trim(value);
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
