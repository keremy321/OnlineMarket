using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ErpIntegration.Api.Tests;

[Collection(IntegrationSqlServerCollection.CollectionName)]
public sealed class ErpIntegrationHealthTests(
    IntegrationSqlServerFixture fixture)
{
    [Fact]
    public async Task Health_endpoint_validates_configuration_and_database()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        using var factory = new ErpIntegrationWebApplicationFactory(
            database.ConnectionString);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class ErpIntegrationWebApplicationFactory(
        string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:IntegrationDb"] =
                            connectionString,
                        ["MockErp:BaseAddress"] =
                            "http://localhost/",
                        ["MockErp:ApiKey"] = "health-test-key",
                        ["IntegrationWorker:Enabled"] = "false"
                    });
            });
        }
    }
}
