using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Recommendation.Api.Tests;

internal sealed class RecommendationWebApplicationFactory(
    string connectionString,
    string? apiKey,
    IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:RecommendationDb"] = connectionString,
                ["Security:ApiKey"] = apiKey
            };
            if (additionalConfiguration is not null)
            {
                foreach (var item in additionalConfiguration)
                {
                    values[item.Key] = item.Value;
                }
            }

            configuration.AddInMemoryCollection(values);
        });
    }
}
