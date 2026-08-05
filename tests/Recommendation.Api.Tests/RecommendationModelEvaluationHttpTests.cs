using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelEvaluationHttpTests
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Evaluation_facade_returns_metrics_without_identifiers()
    {
        await using var factory = CreateFactory(
            RecommendationModelClientOutcome.Succeeded);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, ApiKey);

        using var response = await client.PostAsync(
            "/api/v1/recommendations/evaluate-models",
            null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporal-test-v1", body, StringComparison.Ordinal);
        Assert.Contains("precisionAtK", body, StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "subjectId",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "customerId",
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluation_failure_maps_to_retryable_service_unavailable()
    {
        await using var factory = CreateFactory(
            RecommendationModelClientOutcome.InvalidResponse);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, ApiKey);

        using var response = await client.PostAsync(
            "/api/v1/recommendations/evaluate-models",
            null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(
            "RecommendationModel.EvaluationUnavailable",
            body,
            StringComparison.Ordinal);
    }

    private static RecommendationWebApplicationFactory CreateFactory(
        RecommendationModelClientOutcome outcome)
    {
        return new RecommendationWebApplicationFactory(
            "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True",
            ApiKey,
            configureServices: services =>
            {
                services.RemoveAll<IRecommendationModelEvaluationService>();
                services.AddSingleton<IRecommendationModelEvaluationService>(
                    new StubService(outcome));
            });
    }

    private sealed class StubService(RecommendationModelClientOutcome outcome)
        : IRecommendationModelEvaluationService
    {
        public Task<RecommendationModelClientResult<
            RecommendationModelEvaluationClientResponse>> EvaluateAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RecommendationModelClientResult<
                RecommendationModelEvaluationClientResponse>(
                    outcome,
                    outcome == RecommendationModelClientOutcome.Succeeded
                        ? RecommendationModelEvaluationTestData.Response()
                        : null));
        }
    }
}
