namespace Recommendation.Api.Tests;

public sealed class RecommendationSubjectConfigurationTests
{
    [Fact]
    public void Normal_startup_requires_a_strong_subject_derivation_key()
    {
        const string invalidKey = "do-not-log-this-short-key";
        using var factory = new RecommendationWebApplicationFactory(
            "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True",
            "test-only-recommendation-api-key",
            new Dictionary<string, string?>
            {
                ["RecommendationSubject:Key"] = invalidKey,
                ["RecommendationSubject:Version"] = "v1"
            });

        var exception = Assert.ThrowsAny<Exception>(
            () => factory.CreateClient());

        Assert.Contains(
            "RecommendationSubject",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            invalidKey,
            exception.ToString(),
            StringComparison.Ordinal);
    }
}
