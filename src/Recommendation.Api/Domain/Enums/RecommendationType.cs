namespace Recommendation.Api.Domain.Enums;

public enum RecommendationType : byte
{
    Popular = 1,
    FrequentlyBoughtTogether = 2,
    Similar = 3,
    Personalized = 4,
    CartCompletion = 5
}
