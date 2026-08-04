using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Recommendation.Api.Tests;

internal sealed class RecommendationWebApplicationFactory(
    string connectionString,
    string? apiKey)
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
                    ["ConnectionStrings:RecommendationDb"] =
                        connectionString,
                    ["Security:ApiKey"] = apiKey
                });
        });
    }
}
