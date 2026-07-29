using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Domain.Entities;

public sealed class CustomerPreferenceScore
{
    public Guid CustomerId { get; set; }

    public PreferenceType PreferenceType { get; set; }

    public Guid ReferenceId { get; set; }

    public int PurchaseCount { get; set; }

    public int TotalQuantity { get; set; }

    public DateTime LastPurchasedAtUtc { get; set; }

    public decimal FrequencyScore { get; set; }

    public decimal RecencyScore { get; set; }

    public decimal Score { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
