using System.ComponentModel.DataAnnotations;
using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Services;

public sealed record OrderReadyForErpValidationResult(
    OrderReadyForErpV1Request NormalizedRequest,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class OrderReadyForErpV1Validator
{
    private const decimal MaximumMoney = 9999999999999999.99m;
    private static readonly EmailAddressAttribute EmailValidator = new();

    public OrderReadyForErpValidationResult Validate(
        OrderReadyForErpV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = OrderReadyForErpV1Normalizer.Normalize(request);
        var errors = new Dictionary<string, List<string>>(
            StringComparer.Ordinal);

        ValidateRequiredGuid(errors, nameof(request.EventId), normalized.EventId);
        ValidateUtc(
            errors,
            nameof(request.OccurredAtUtc),
            normalized.OccurredAtUtc);
        ValidateRequiredGuid(
            errors,
            nameof(request.CorrelationId),
            normalized.CorrelationId);
        ValidateRequiredGuid(errors, nameof(request.OrderId), normalized.OrderId);
        ValidateRequiredString(
            errors,
            nameof(request.OrderNumber),
            normalized.OrderNumber,
            32);
        ValidateUtc(
            errors,
            nameof(request.OrderPlacedAtUtc),
            normalized.OrderPlacedAtUtc);

        if (!Enum.IsDefined(normalized.PaymentMethod))
        {
            AddError(
                errors,
                nameof(request.PaymentMethod),
                "PaymentMethod is required and must be a supported numeric value.");
        }

        ValidateCustomer(errors, normalized.Customer);
        ValidateAddress(errors, normalized.Address);
        ValidateTotals(errors, normalized.Totals);
        ValidateItems(errors, normalized.Items, normalized.Totals);

        return new OrderReadyForErpValidationResult(
            normalized,
            errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));
    }

    private static void ValidateCustomer(
        Dictionary<string, List<string>> errors,
        ErpOrderCustomerV1Request? customer)
    {
        if (customer is null)
        {
            AddError(errors, "Customer", "Customer is required.");
            return;
        }

        ValidateRequiredGuid(
            errors,
            "Customer.CustomerId",
            customer.CustomerId);
        ValidateRequiredString(
            errors,
            "Customer.FirstName",
            customer.FirstName,
            100);
        ValidateRequiredString(
            errors,
            "Customer.LastName",
            customer.LastName,
            100);
        ValidateRequiredString(
            errors,
            "Customer.Email",
            customer.Email,
            256);

        if (!string.IsNullOrEmpty(customer.Email)
            && !EmailValidator.IsValid(customer.Email))
        {
            AddError(
                errors,
                "Customer.Email",
                "Customer.Email must be a valid email address.");
        }
    }

    private static void ValidateAddress(
        Dictionary<string, List<string>> errors,
        ErpOrderAddressV1Request? address)
    {
        if (address is null)
        {
            AddError(errors, "Address", "Address is required.");
            return;
        }

        ValidateRequiredString(
            errors,
            "Address.RecipientName",
            address.RecipientName,
            200);
        ValidateRequiredString(
            errors,
            "Address.PhoneNumber",
            address.PhoneNumber,
            30);
        ValidateRequiredString(
            errors,
            "Address.AddressLine1",
            address.AddressLine1,
            250);
        ValidateOptionalString(
            errors,
            "Address.AddressLine2",
            address.AddressLine2,
            250);
        ValidateRequiredString(
            errors,
            "Address.District",
            address.District,
            100);
        ValidateRequiredString(
            errors,
            "Address.City",
            address.City,
            100);
        ValidateOptionalString(
            errors,
            "Address.PostalCode",
            address.PostalCode,
            20);
        ValidateRequiredString(
            errors,
            "Address.CountryCode",
            address.CountryCode,
            2);

        if (!string.IsNullOrEmpty(address.CountryCode)
            && !address.CountryCode.Equals("TR", StringComparison.Ordinal))
        {
            AddError(
                errors,
                "Address.CountryCode",
                "Address.CountryCode must be TR for the v1 contract.");
        }
    }

    private static void ValidateTotals(
        Dictionary<string, List<string>> errors,
        ErpOrderTotalsV1Request? totals)
    {
        if (totals is null)
        {
            AddError(errors, "Totals", "Totals is required.");
            return;
        }

        ValidateMoney(errors, "Totals.Subtotal", totals.Subtotal);
        ValidateMoney(errors, "Totals.VatTotal", totals.VatTotal);
        ValidateMoney(errors, "Totals.GrandTotal", totals.GrandTotal);
        ValidateRequiredString(
            errors,
            "Totals.Currency",
            totals.Currency,
            3);

        if (!string.IsNullOrEmpty(totals.Currency)
            && !totals.Currency.Equals("TRY", StringComparison.Ordinal))
        {
            AddError(
                errors,
                "Totals.Currency",
                "Totals.Currency must be TRY for the v1 contract.");
        }

        if (totals.GrandTotal != totals.Subtotal + totals.VatTotal)
        {
            AddError(
                errors,
                "Totals.GrandTotal",
                "Totals.GrandTotal must equal Subtotal plus VatTotal.");
        }
    }

    private static void ValidateItems(
        Dictionary<string, List<string>> errors,
        IReadOnlyList<ErpOrderItemV1Request?>? items,
        ErpOrderTotalsV1Request? totals)
    {
        if (items is null || items.Count == 0)
        {
            AddError(errors, "Items", "At least one order item is required.");
            return;
        }

        var productIds = new HashSet<Guid>();
        decimal subtotal = 0;
        decimal vatTotal = 0;
        decimal grandTotal = 0;
        var totalsOverflowed = false;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var prefix = $"Items[{index}]";
            if (item is null)
            {
                AddError(errors, prefix, $"{prefix} must not be null.");
                continue;
            }

            ValidateRequiredGuid(errors, $"{prefix}.ProductId", item.ProductId);
            if (item.ProductId != Guid.Empty && !productIds.Add(item.ProductId))
            {
                AddError(
                    errors,
                    $"{prefix}.ProductId",
                    "Each ProductId may appear at most once.");
            }

            ValidateRequiredString(errors, $"{prefix}.Sku", item.Sku, 64);
            ValidateRequiredString(
                errors,
                $"{prefix}.ProductName",
                item.ProductName,
                200);

            if (item.Quantity <= 0)
            {
                AddError(
                    errors,
                    $"{prefix}.Quantity",
                    "Quantity must be greater than zero.");
            }

            ValidateMoney(errors, $"{prefix}.UnitPrice", item.UnitPrice);
            ValidateVatRate(errors, $"{prefix}.VatRate", item.VatRate);
            ValidateMoney(
                errors,
                $"{prefix}.NetLineAmount",
                item.NetLineAmount);
            ValidateMoney(errors, $"{prefix}.VatAmount", item.VatAmount);
            ValidateMoney(errors, $"{prefix}.LineTotal", item.LineTotal);

            if (item.LineTotal != item.NetLineAmount + item.VatAmount)
            {
                AddError(
                    errors,
                    $"{prefix}.LineTotal",
                    "LineTotal must equal NetLineAmount plus VatAmount.");
            }

            ValidateCalculatedLine(errors, item, prefix);

            if (!totalsOverflowed)
            {
                try
                {
                    subtotal = checked(subtotal + item.NetLineAmount);
                    vatTotal = checked(vatTotal + item.VatAmount);
                    grandTotal = checked(grandTotal + item.LineTotal);
                }
                catch (OverflowException)
                {
                    totalsOverflowed = true;
                    AddError(
                        errors,
                        "Items",
                        "The sum of item amounts is outside the supported range.");
                }
            }
        }

        if (totals is null || totalsOverflowed)
        {
            return;
        }

        if (totals.Subtotal != subtotal)
        {
            AddError(
                errors,
                "Totals.Subtotal",
                "Totals.Subtotal must equal the sum of item NetLineAmount values.");
        }

        if (totals.VatTotal != vatTotal)
        {
            AddError(
                errors,
                "Totals.VatTotal",
                "Totals.VatTotal must equal the sum of item VatAmount values.");
        }

        if (totals.GrandTotal != grandTotal)
        {
            AddError(
                errors,
                "Totals.GrandTotal",
                "Totals.GrandTotal must equal the sum of item LineTotal values.");
        }
    }

    private static void ValidateCalculatedLine(
        Dictionary<string, List<string>> errors,
        ErpOrderItemV1Request item,
        string prefix)
    {
        if (item.Quantity <= 0
            || item.UnitPrice < 0
            || item.VatRate is < 0 or > 100)
        {
            return;
        }

        try
        {
            var expectedNet = decimal.Round(
                checked(item.UnitPrice * item.Quantity),
                2,
                MidpointRounding.AwayFromZero);
            var expectedVat = decimal.Round(
                checked(expectedNet * item.VatRate / 100m),
                2,
                MidpointRounding.AwayFromZero);

            if (item.NetLineAmount != expectedNet)
            {
                AddError(
                    errors,
                    $"{prefix}.NetLineAmount",
                    "NetLineAmount does not match UnitPrice multiplied by Quantity.");
            }

            if (item.VatAmount != expectedVat)
            {
                AddError(
                    errors,
                    $"{prefix}.VatAmount",
                    "VatAmount does not match the rounded VAT calculation.");
            }
        }
        catch (OverflowException)
        {
            AddError(
                errors,
                prefix,
                "The calculated line amount is outside the supported range.");
        }
    }

    private static void ValidateRequiredGuid(
        Dictionary<string, List<string>> errors,
        string path,
        Guid value)
    {
        if (value == Guid.Empty)
        {
            AddError(errors, path, $"{path} is required.");
        }
    }

    private static void ValidateUtc(
        Dictionary<string, List<string>> errors,
        string path,
        DateTime value)
    {
        if (value == default)
        {
            AddError(errors, path, $"{path} is required.");
        }
        else if (value.Kind != DateTimeKind.Utc)
        {
            AddError(errors, path, $"{path} must be expressed in UTC.");
        }
    }

    private static void ValidateRequiredString(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, path, $"{path} is required.");
            return;
        }

        if (value.Length > maximumLength)
        {
            AddError(
                errors,
                path,
                $"{path} must not exceed {maximumLength} characters.");
        }
    }

    private static void ValidateOptionalString(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        int maximumLength)
    {
        if (value is not null && value.Length > maximumLength)
        {
            AddError(
                errors,
                path,
                $"{path} must not exceed {maximumLength} characters.");
        }
    }

    private static void ValidateMoney(
        Dictionary<string, List<string>> errors,
        string path,
        decimal value)
    {
        if (value < 0)
        {
            AddError(errors, path, $"{path} must not be negative.");
        }

        if (value > MaximumMoney)
        {
            AddError(errors, path, $"{path} is outside decimal(18,2).");
        }

        if (decimal.Round(value, 2) != value)
        {
            AddError(errors, path, $"{path} must have at most two decimal places.");
        }
    }

    private static void ValidateVatRate(
        Dictionary<string, List<string>> errors,
        string path,
        decimal value)
    {
        if (value is < 0 or > 100)
        {
            AddError(errors, path, $"{path} must be between 0 and 100.");
        }

        if (decimal.Round(value, 2) != value)
        {
            AddError(errors, path, $"{path} must have at most two decimal places.");
        }
    }

    private static void AddError(
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
}
