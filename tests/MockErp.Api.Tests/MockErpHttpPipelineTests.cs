using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Security;

namespace MockErp.Api.Tests;

[Collection(MockErpSqlServerCollection.CollectionName)]
public sealed class MockErpHttpPipelineTests(
    MockErpSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-mock-erp-api-key";

    [Fact]
    public async Task Missing_api_key_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new MockErpWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/customers/ensure")
        {
            Content = JsonContent.Create(
                MockErpApiTestData.CreateEnsureCustomerRequest())
        };
        request.Headers.Add(
            IdempotencyKeyFilter.HeaderName,
            $"customer:{Guid.NewGuid()}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Authentication.ApiKeyRequired", error!.Code);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new MockErpWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/customers/ensure")
        {
            Content = JsonContent.Create(
                MockErpApiTestData.CreateEnsureCustomerRequest())
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Idempotency.KeyRequired", error!.Code);
    }

    [Fact]
    public async Task Unknown_json_properties_are_rejected_as_validation_failures()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new MockErpWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        var node = JsonSerializer.SerializeToNode(
                MockErpApiTestData.CreateEnsureCustomerRequest(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?.AsObject()
            ?? throw new InvalidOperationException("Unable to create test JSON.");
        node["unexpectedProperty"] = "must be rejected";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/customers/ensure")
        {
            Content = new StringContent(
                node.ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);
        request.Headers.Add(
            IdempotencyKeyFilter.HeaderName,
            $"customer:{Guid.NewGuid()}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content
            .ReadFromJsonAsync<ApiValidationErrorResponse>();
        Assert.Equal("Validation.Failed", error!.Code);
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }
}
