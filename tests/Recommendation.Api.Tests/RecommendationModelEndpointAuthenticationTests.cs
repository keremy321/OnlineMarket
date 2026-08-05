using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelEndpointAuthenticationTests
{
    [Theory]
    [InlineData("/api/v1/recommendations/recalculate-models", "POST")]
    [InlineData("/api/v1/recommendations/evaluate-models", "POST")]
    [InlineData(
        "/api/v1/recommendations/similar/00000000-0000-0000-0000-000000000001",
        "GET")]
    [InlineData("/api/v1/recommendations/subjects/backfill", "POST")]
    [InlineData(
        "/api/v1/recommendations/customers/00000000-0000-0000-0000-000000000001",
        "GET")]
    public async Task Model_facade_endpoints_require_authentication(
        string path,
        string method)
    {
        await using var factory = new RecommendationWebApplicationFactory(
            "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True",
            "test-only-recommendation-api-key");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
