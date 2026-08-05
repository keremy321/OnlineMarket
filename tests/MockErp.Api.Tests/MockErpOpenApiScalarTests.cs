using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MockErp.Api.Tests;

public sealed class MockErpOpenApiScalarTests
{
    private const string UnusedConnectionString =
        "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True";

    [Fact]
    public async Task OpenApiDocumentIsAvailableInDevelopmentWithTitleAndApiKeyScheme()
    {
        await using var factory = new DevelopmentMockErpWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            "Online Market Mock ERP API",
            root.GetProperty("info").GetProperty("title").GetString());

        var apiKeyScheme = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("ApiKey");
        Assert.Equal("apiKey", apiKeyScheme.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", apiKeyScheme.GetProperty("name").GetString());
        Assert.Equal("header", apiKeyScheme.GetProperty("in").GetString());

        var ensureOperation = root.GetProperty("paths")
            .GetProperty("/api/v1/customers/ensure")
            .GetProperty("post");
        Assert.True(
            ensureOperation.TryGetProperty("security", out var security)
            && security.GetArrayLength() > 0,
            "A protected operation must carry an OpenAPI security requirement.");
    }

    [Fact]
    public async Task ScalarUiIsAvailableInDevelopment()
    {
        await using var factory = new DevelopmentMockErpWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiAndScalarAreNotAvailableOutsideDevelopment()
    {
        await using var factory = new MockErpWebApplicationFactory(
            UnusedConnectionString,
            "test-only-mockerp-api-key");
        using var client = CreateClient(factory);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
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

    private sealed class DevelopmentMockErpWebApplicationFactory
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
                        ["ConnectionStrings:MockErpDb"] = UnusedConnectionString,
                        ["Security:ApiKey"] = "test-only-mockerp-api-key"
                    });
            });
        }
    }
}
