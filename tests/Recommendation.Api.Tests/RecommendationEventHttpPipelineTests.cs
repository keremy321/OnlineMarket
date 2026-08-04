using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationEventHttpPipelineTests(
    RecommendationSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Unknown_product_properties_are_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        var node = ToObject(RecommendationEventTestData.CreateProduct());
        node["UnexpectedProperty"] = "must be rejected";

        using var response = await SendJsonAsync(
            client,
            "/api/v1/events/products",
            node,
            ApiKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content
            .ReadFromJsonAsync<ApiValidationErrorResponse>();
        Assert.Equal("Validation.Failed", error!.Code);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.ProductSnapshots.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Personal_and_financial_order_properties_are_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        var node = ToObject(RecommendationEventTestData.CreateOrder(
            (Guid.NewGuid(), 1)));
        node["Email"] = "not-accepted@example.test";
        node["Address"] = "not accepted";
        node["PaymentMethod"] = 1;
        node["GrandTotal"] = 100m;

        using var response = await SendJsonAsync(
            client,
            "/api/v1/events/orders",
            node,
            ApiKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content
            .ReadFromJsonAsync<ApiValidationErrorResponse>();
        Assert.Equal("Validation.Failed", error!.Code);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.OrderSnapshots.CountAsync());
        Assert.Equal(0, await context.OrderSnapshotItems.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Missing_invalid_and_unconfigured_api_keys_are_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var request = RecommendationEventTestData.CreateProduct();
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);

        using var missing = await client.PostAsJsonAsync(
            "/api/v1/events/products",
            request);
        using var invalid = await SendAsync(
            client,
            "/api/v1/events/products",
            request,
            "wrong-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        var missingError = await missing.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        var invalidError = await invalid.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Authentication.ApiKeyRequired", missingError!.Code);
        Assert.Equal("Authentication.ApiKeyInvalid", invalidError!.Code);

        await using var unconfiguredFactory =
            new RecommendationWebApplicationFactory(
                database.ConnectionString,
                null);
        using var unconfiguredClient = CreateClient(unconfiguredFactory);
        using var unconfigured = await SendAsync(
            unconfiguredClient,
            "/api/v1/events/products",
            request,
            ApiKey);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unconfigured.StatusCode);
        var configurationError = await unconfigured.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal(
            "Authentication.ConfigurationMissing",
            configurationError!.Code);
        Assert.True(configurationError.Retryable);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Product_and_processed_event_rollback_together_on_failure()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [dbo].[TR_ProcessedEvents_RejectIngestion]
                ON [dbo].[ProcessedEvents]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted)
                        THROW 51000, 'Injected event-ingestion failure.', 1;
                END;
                """);
        }

        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
        using var client = CreateClient(factory);
        using var response = await SendAsync(
            client,
            "/api/v1/events/products",
            RecommendationEventTestData.CreateProduct(),
            ApiKey);

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Recommendation.UnexpectedError", error!.Code);
        Assert.True(error.Retryable);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.ProductSnapshots.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    private static JsonObject ToObject<T>(T value)
    {
        return JsonSerializer.SerializeToNode(
                value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?.AsObject()
            ?? throw new InvalidOperationException(
                "Unable to create test JSON.");
    }

    private static HttpClient CreateClient(
        RecommendationWebApplicationFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client,
        string route,
        T request,
        string apiKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add(ApiKeyDefaults.HeaderName, apiKey);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        string route,
        JsonObject value,
        string apiKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(
                value.ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.Add(ApiKeyDefaults.HeaderName, apiKey);
        return await client.SendAsync(message);
    }
}
