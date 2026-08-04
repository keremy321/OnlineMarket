using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Recommendation.Api.Contracts;

public sealed record CartCompletionRecommendationRequest : IValidatableObject
{
    [JsonRequired]
    public IReadOnlyList<Guid>? ProductIds { get; init; }

    public int? Limit { get; init; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (ProductIds is null || ProductIds.Count == 0)
        {
            yield return new ValidationResult(
                "At least one product ID is required.",
                [nameof(ProductIds)]);
        }
        else if (ProductIds.Any(productId => productId == Guid.Empty))
        {
            yield return new ValidationResult(
                "Product IDs cannot contain an empty GUID.",
                [nameof(ProductIds)]);
        }

        if (Limit is <= 0)
        {
            yield return new ValidationResult(
                "The recommendation limit must be positive.",
                [nameof(Limit)]);
        }
    }
}
