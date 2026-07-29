using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Domain.Entities;

public sealed class RecommendationRun
{
    public Guid Id { get; set; }

    public RecommendationRunType RunType { get; set; }

    public RecommendationRunStatus Status { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int InputRecordCount { get; set; }

    public int OutputRecordCount { get; set; }

    public string ParametersJson { get; set; } = "{}";

    public string? ErrorMessage { get; set; }

    public Guid? TriggeredByUserId { get; set; }

    public Guid CorrelationId { get; set; }

    public ICollection<ProductAffinity> ProductAffinities { get; } = [];

    public ICollection<ProductSimilarity> ProductSimilarities { get; } = [];

    public ICollection<ProductPopularity> ProductPopularity { get; } = [];
}
