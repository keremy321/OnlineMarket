using Recommendation.Api.Contracts;

namespace Recommendation.Api.Application.Services;

public sealed record RecommendationEventValidationResult<T>(
    T NormalizedRequest,
    IReadOnlyDictionary<string, string[]> Errors,
    int? TotalQuantity = null)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class RecommendationEventValidator
{
    private const decimal MaximumMoney = 9999999999999999.99m;
    private const decimal MaximumNetContent = 999999999.999m;

    public RecommendationEventValidationResult<ProductSnapshotChangedV1Request>
        ValidateProduct(ProductSnapshotChangedV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = request with
        {
            Sku = request.Sku?.Trim(),
            Name = request.Name?.Trim(),
            Description = NormalizeOptionalString(request.Description)
        };
        var errors = CreateErrors();

        RequiredGuid(errors, nameof(request.EventId), normalized.EventId);
        UtcTimestamp(
            errors,
            nameof(request.OccurredAtUtc),
            normalized.OccurredAtUtc);
        RequiredGuid(
            errors,
            nameof(request.CorrelationId),
            normalized.CorrelationId);
        RequiredGuid(errors, nameof(request.ProductId), normalized.ProductId);
        RequiredString(errors, nameof(request.Sku), normalized.Sku, 64);
        RequiredString(errors, nameof(request.Name), normalized.Name, 200);
        OptionalString(
            errors,
            nameof(request.Description),
            normalized.Description,
            2000);
        RequiredGuid(errors, nameof(request.CategoryId), normalized.CategoryId);
        RequiredGuid(errors, nameof(request.BrandId), normalized.BrandId);

        if (normalized.ParentCategoryId == Guid.Empty)
        {
            AddError(
                errors,
                nameof(request.ParentCategoryId),
                "ParentCategoryId must be a non-empty GUID when supplied.");
        }
        else if (normalized.ParentCategoryId == normalized.CategoryId)
        {
            AddError(
                errors,
                nameof(request.ParentCategoryId),
                "ParentCategoryId must differ from CategoryId.");
        }

        DecimalValue(
            errors,
            nameof(request.Price),
            normalized.Price,
            0,
            MaximumMoney,
            2,
            false);
        DecimalValue(
            errors,
            nameof(request.NetContent),
            normalized.NetContent,
            0,
            MaximumNetContent,
            3,
            true);

        if (!Enum.IsDefined(normalized.UnitType))
        {
            AddError(
                errors,
                nameof(request.UnitType),
                "UnitType must be a supported numeric value.");
        }

        UtcTimestamp(
            errors,
            nameof(request.SourceUpdatedAtUtc),
            normalized.SourceUpdatedAtUtc);

        return Result(normalized, errors);
    }

    public RecommendationEventValidationResult<
        OrderConfirmedForRecommendationV1Request> ValidateOrder(
            OrderConfirmedForRecommendationV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = request with
        {
            OrderNumber = request.OrderNumber?.Trim(),
            Items = request.Items?
                .OrderBy(item => item?.ProductId ?? Guid.Empty)
                .ToArray()
        };
        var errors = CreateErrors();

        RequiredGuid(errors, nameof(request.EventId), normalized.EventId);
        UtcTimestamp(
            errors,
            nameof(request.OccurredAtUtc),
            normalized.OccurredAtUtc);
        RequiredGuid(
            errors,
            nameof(request.CorrelationId),
            normalized.CorrelationId);
        RequiredGuid(errors, nameof(request.OrderId), normalized.OrderId);
        RequiredString(
            errors,
            nameof(request.OrderNumber),
            normalized.OrderNumber,
            32);
        RequiredGuid(errors, nameof(request.CustomerId), normalized.CustomerId);

        var totalQuantity = ValidateItems(errors, normalized.Items);
        return Result(normalized, errors, totalQuantity);
    }

    private static int? ValidateItems(
        Dictionary<string, List<string>> errors,
        IReadOnlyList<RecommendationOrderItemV1Request?>? items)
    {
        if (items is null || items.Count == 0)
        {
            AddError(errors, "Items", "At least one item is required.");
            return null;
        }

        var productIds = new HashSet<Guid>();
        var totalQuantity = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var prefix = $"Items[{index}]";
            if (item is null)
            {
                AddError(errors, prefix, $"{prefix} must not be null.");
                continue;
            }

            RequiredGuid(errors, $"{prefix}.ProductId", item.ProductId);
            if (item.ProductId != Guid.Empty && !productIds.Add(item.ProductId))
            {
                AddError(
                    errors,
                    $"{prefix}.ProductId",
                    "Each ProductId may appear at most once.");
            }

            if (item.Quantity <= 0)
            {
                AddError(
                    errors,
                    $"{prefix}.Quantity",
                    "Quantity must be greater than zero.");
                continue;
            }

            try
            {
                totalQuantity = checked(totalQuantity + item.Quantity);
            }
            catch (OverflowException)
            {
                AddError(
                    errors,
                    "Items",
                    "The total item quantity is outside the supported range.");
                return null;
            }
        }

        return totalQuantity;
    }

    private static Dictionary<string, List<string>> CreateErrors()
    {
        return new Dictionary<string, List<string>>(StringComparer.Ordinal);
    }

    private static RecommendationEventValidationResult<T> Result<T>(
        T normalized,
        Dictionary<string, List<string>> errors,
        int? totalQuantity = null)
    {
        return new RecommendationEventValidationResult<T>(
            normalized,
            errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal),
            totalQuantity);
    }

    private static void RequiredGuid(
        Dictionary<string, List<string>> errors,
        string path,
        Guid value)
    {
        if (value == Guid.Empty)
        {
            AddError(errors, path, $"{path} is required.");
        }
    }

    private static void UtcTimestamp(
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
        else if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            AddError(
                errors,
                path,
                $"{path} must use millisecond precision.");
        }
    }

    private static void RequiredString(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, path, $"{path} is required.");
        }
        else if (value.Length > maximumLength)
        {
            AddError(
                errors,
                path,
                $"{path} must not exceed {maximumLength} characters.");
        }
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static void OptionalString(
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

    private static void DecimalValue(
        Dictionary<string, List<string>> errors,
        string path,
        decimal value,
        decimal minimum,
        decimal maximum,
        int scale,
        bool exclusiveMinimum)
    {
        if (exclusiveMinimum ? value <= minimum : value < minimum)
        {
            AddError(
                errors,
                path,
                exclusiveMinimum
                    ? $"{path} must be greater than {minimum}."
                    : $"{path} must not be less than {minimum}.");
        }

        if (value > maximum)
        {
            AddError(errors, path, $"{path} is outside the supported range.");
        }

        if (decimal.Round(value, scale) != value)
        {
            AddError(
                errors,
                path,
                $"{path} must have at most {scale} decimal places.");
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
