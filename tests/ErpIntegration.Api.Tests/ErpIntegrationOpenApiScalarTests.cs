using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ErpIntegration.Api.Tests;

public sealed class ErpIntegrationOpenApiScalarTests
{
    private const string UnusedConnectionString =
        "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;TrustServerCertificate=True";

    [Fact]
    public async Task OpenApiDocumentIsAvailableInDevelopmentWithTitleAndNoApiKeyScheme()
    {
        await using var factory = new DevelopmentErpIntegrationWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            "Online Market ERP Integration API",
            root.GetProperty("info").GetProperty("title").GetString());

        // This service does not authenticate requests with X-Api-Key, so no
        // security scheme or per-operation security requirement should exist.
        var hasSecuritySchemes = root.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out var schemes)
            && schemes.EnumerateObject().Any();
        Assert.False(hasSecuritySchemes);

        var acceptOperation = root.GetProperty("paths")
            .GetProperty("/api/v1/integration/orders")
            .GetProperty("post");
        var acceptHasSecurity =
            acceptOperation.TryGetProperty("security", out var security)
            && security.GetArrayLength() > 0;
        Assert.False(acceptHasSecurity);
    }

    [Fact]
    public async Task ScalarUiIsAvailableInDevelopment()
    {
        await using var factory = new DevelopmentErpIntegrationWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiAndScalarAreNotAvailableOutsideDevelopment()
    {
        await using var factory = new TestingErpIntegrationWebApplicationFactory();
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

    private static Dictionary<string, string?> BaseConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:IntegrationDb"] = UnusedConnectionString,
            ["MockErp:BaseAddress"] = "http://localhost/",
            ["MockErp:ApiKey"] = "openapi-test-key",
            ["IntegrationWorker:Enabled"] = "false"
        };
    }

    private sealed class DevelopmentErpIntegrationWebApplicationFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(BaseConfiguration()));
        }
    }

    private sealed class TestingErpIntegrationWebApplicationFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(BaseConfiguration()));
        }
    }
}
