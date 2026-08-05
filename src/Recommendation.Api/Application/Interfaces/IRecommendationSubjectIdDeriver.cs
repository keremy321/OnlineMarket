namespace Recommendation.Api.Application.Interfaces;

public interface IRecommendationSubjectIdDeriver
{
    string Derive(Guid customerId);
}
