namespace Recommendation.Api.Domain.Enums;

public enum RecommendationRunType : byte
{
    Affinity = 1,
    Similarity = 2,
    Popularity = 3,
    CustomerPreference = 4,
    Full = 5
}
