using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

public sealed class PersonalizedRecommendationHttpTests
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Public_facade_returns_recommendation_metadata_without_subject()
    {
        var customerId = Guid.Parse(
            "50000000-0000-0000-0000-000000000001");
        await using var factory = new RecommendationWebApplicationFactory(
            "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True",
            ApiKey,
            configureServices: services =>
            {
                services.RemoveAll<IPersonalizedRecommendationService>();
                services.AddSingleton<IPersonalizedRecommendationService>(
                    new StubService());
            });
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, ApiKey);

        using var response = await client.GetAsync(
            $"/api/v1/recommendations/customers/{customerId}?limit=8");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PythonImplicitAls", body, StringComparison.Ordinal);
        Assert.Contains("Personalized.ImplicitAls", body, StringComparison.Ordinal);
        Assert.DoesNotContain("subjectId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            customerId.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubService : IPersonalizedRecommendationService
    {
        public Task<IReadOnlyList<PersonalizedRecommendationItem>> GetAsync(
            Guid customerId,
            int? requestedLimit,
            bool excludePreviouslyPurchased,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PersonalizedRecommendationItem> result =
            [
                new PersonalizedRecommendationItem(
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    0.9m,
                    null,
                    PersonalizedRankingSource.PythonImplicitAls,
                    "model-set-v1")
            ];
            return Task.FromResult(result);
        }
    }
}
