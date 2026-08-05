using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Recommendation.Api.Tests;

public sealed class RecommendationOpenApiScalarTests
{
    private const string UnusedConnectionString =
        "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True";

    [Fact]
    public async Task
        OpenApiDocumentIsAvailableInDevelopmentWithTitleAndApiKeyScheme()
    {
        await using var factory = new DevelopmentRecommendationWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            "Online Market Recommendation API",
            root.GetProperty("info").GetProperty("title").GetString());

        var apiKeyScheme = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("ApiKey");
        Assert.Equal("apiKey", apiKeyScheme.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", apiKeyScheme.GetProperty("name").GetString());
        Assert.Equal("header", apiKeyScheme.GetProperty("in").GetString());

        var popularOperation = root.GetProperty("paths")
            .GetProperty("/api/v1/recommendations/popular")
            .GetProperty("get");
        Assert.True(
            popularOperation.TryGetProperty("security", out var security)
            && security.GetArrayLength() > 0,
            "A protected operation must carry an OpenAPI security requirement.");
    }

    [Fact]
    public async Task ScalarUiIsAvailableInDevelopment()
    {
        await using var factory = new DevelopmentRecommendationWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiAndScalarAreNotAvailableOutsideDevelopment()
    {
        // This service applies a global fallback authorization policy to every
        // request, including ones that match no endpoint at all. Outside
        // Development, /openapi and /scalar are not mapped, so routing finds
        // no endpoint and the fallback policy rejects the request with 401
        // before a 404 would otherwise be produced. Either way, the documents
        // are not served outside Development.
        await using var factory = new RecommendationWebApplicationFactory(
            UnusedConnectionString,
            "test-only-recommendation-api-key");
        using var client = CreateClient(factory);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/scalar/v1")).StatusCode);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private sealed class DevelopmentRecommendationWebApplicationFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:RecommendationDb"] =
                            UnusedConnectionString,
                        ["Security:ApiKey"] = "test-only-recommendation-api-key",
                        ["RecommendationSubject:Key"] =
                            "test-only-recommendation-subject-key-32-bytes-minimum",
                        ["RecommendationSubject:Version"] = "v1",
                        ["Services:RecommendationModelService:BaseAddress"] =
                            "https://recommendation-model-service.test",
                        ["Services:RecommendationModelService:ApiKey"] =
                            "test-only-model-service-key",
                        ["Services:RecommendationModelService:Timeout"] =
                            "00:00:01"
                    });
            });
        }
    }
}
