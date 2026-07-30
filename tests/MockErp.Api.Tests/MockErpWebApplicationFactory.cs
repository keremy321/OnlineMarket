using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MockErp.Api.Tests;

internal sealed class MockErpWebApplicationFactory(
    string connectionString,
    string apiKey)
    : WebApplicationFactory<Program>
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
