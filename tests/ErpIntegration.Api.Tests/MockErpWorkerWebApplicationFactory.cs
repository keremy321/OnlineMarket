extern alias MockErpApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MockErpProgram = MockErpApi::Program;

namespace ErpIntegration.Api.Tests;

internal sealed class MockErpWorkerWebApplicationFactory(
    string connectionString,
    string apiKey)
    : WebApplicationFactory<MockErpProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MockErpDb"] = connectionString,
                    ["Security:ApiKey"] = apiKey
                });
        });
    }
}
