using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Persistence;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

[Collection(RecommendationSqlServerCollection.CollectionName)]
public sealed class RecommendationEventIngestionSqlServerTests(
    RecommendationSqlServerFixture fixture)
{
    private const string ApiKey = "test-only-recommendation-api-key";

    [Fact]
    public async Task Product_event_creates_snapshot_and_processed_event()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateProduct();

        using var response = await PostAsync(client, "products", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = await response.Content
            .ReadFromJsonAsync<RecommendationEventAcceptedResponse>();
        Assert.Equal(request.EventId, accepted!.EventId);
        Assert.Equal("Processed", accepted.Status);

        await using var context = database.CreateContext();
        var product = await context.ProductSnapshots.AsNoTracking().SingleAsync();
        var processed = await context.ProcessedEvents.AsNoTracking().SingleAsync();
        Assert.Equal(request.ProductId, product.ProductId);
        Assert.Equal(request.Sku, product.Sku);
        Assert.Equal(request.Name, product.Name);
        Assert.Equal(request.CategoryId, product.CategoryId);
        Assert.Equal(request.ParentCategoryId, product.ParentCategoryId);
        Assert.Equal(request.BrandId, product.BrandId);
        Assert.Equal(request.Price, product.Price);
        Assert.Equal(request.NetContent, product.NetContent);
        Assert.Equal(request.UnitType, product.UnitType);
        Assert.Equal(request.IsActive, product.IsActive);
        Assert.Equal(request.IsInStock, product.IsInStock);
        Assert.Equal(request.SourceUpdatedAtUtc, product.SourceUpdatedAtUtc);
        Assert.Equal(request.EventId, processed.EventId);
        Assert.Equal("ProductSnapshotChangedV1", processed.EventType);
        Assert.Matches("^[0-9a-f]{64}$", processed.PayloadHash);
    }

    [Fact]
    public async Task Product_replay_succeeds_and_different_hash_conflicts()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateProduct();

        using var first = await PostAsync(client, "products", request);
        using var replay = await PostAsync(
            client,
            "products",
            request with
            {
                Sku = $"  {request.Sku}  ",
                Name = $" {request.Name} "
            });
        using var conflict = await PostAsync(
            client,
            "products",
            request with { Name = "Different product payload" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content
            .ReadFromJsonAsync<RecommendationEventAcceptedResponse>();
        Assert.Equal("Replay", replayResult!.Status);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var error = await conflict.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Idempotency.PayloadConflict", error!.Code);
        Assert.False(error.Retryable);

        await using var context = database.CreateContext();
        Assert.Equal(1, await context.ProductSnapshots.CountAsync());
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Product_source_timestamp_controls_stale_and_newer_updates()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var productId = Guid.NewGuid();
        var current = RecommendationEventTestData.CreateProduct(
            productId: productId,
            name: "Current",
            sourceUpdatedAtUtc: RecommendationEventTestData.BaseUtc);
        var stale = current with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            Name = "Stale",
            SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc.AddMinutes(-1)
        };
        var newer = current with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            Name = "Newer",
            SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc.AddMinutes(1)
        };

        using var currentResponse = await PostAsync(client, "products", current);
        using var staleResponse = await PostAsync(client, "products", stale);

        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
        await using (var staleContext = database.CreateContext())
        {
            var snapshot = await staleContext.ProductSnapshots
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal("Current", snapshot.Name);
            Assert.Equal(current.SourceUpdatedAtUtc, snapshot.SourceUpdatedAtUtc);
            Assert.Equal(2, await staleContext.ProcessedEvents.CountAsync());
        }

        using var newerResponse = await PostAsync(client, "products", newer);

        Assert.Equal(HttpStatusCode.OK, newerResponse.StatusCode);
        await using var newerContext = database.CreateContext();
        var updated = await newerContext.ProductSnapshots
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("Newer", updated.Name);
        Assert.Equal(newer.SourceUpdatedAtUtc, updated.SourceUpdatedAtUtc);
        Assert.Equal(3, await newerContext.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_product_requests_create_one_logical_result()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateProduct();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => PostAsync(client, "products", request)));

        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var context = database.CreateContext();
        Assert.Equal(1, await context.ProductSnapshots.CountAsync());
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Order_event_creates_pseudonymous_snapshot_and_replays()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        await SeedProductsAsync(database, firstProductId, secondProductId);
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateOrder(
            (firstProductId, 2),
            (secondProductId, 3));

        using var first = await PostAsync(client, "orders", request);
        using var replay = await PostAsync(
            client,
            "orders",
            request with
            {
                OrderNumber = $" {request.OrderNumber} ",
                Items = request.Items!.Reverse().ToArray()
            });
        var changedItems = request.Items!.ToArray();
        changedItems[0] = changedItems[0]! with { Quantity = 4 };
        using var conflict = await PostAsync(
            client,
            "orders",
            request with { Items = changedItems });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content
            .ReadFromJsonAsync<RecommendationEventAcceptedResponse>();
        Assert.Equal("Replay", replayResult!.Status);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictError = await conflict.Content
            .ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Idempotency.PayloadConflict", conflictError!.Code);

        await using var context = database.CreateContext();
        var order = await context.OrderSnapshots
            .AsNoTracking()
            .SingleAsync();
        var items = await context.OrderSnapshotItems
            .AsNoTracking()
            .OrderBy(item => item.ProductId)
            .ToArrayAsync();
        Assert.Equal(request.OrderId, order.OrderId);
        Assert.Equal(request.OrderNumber, order.OrderNumber);
        Assert.Equal(request.CustomerId, order.CustomerId);
        Assert.Equal(CreateSubjectDeriver().Derive(request.CustomerId), order.SubjectId);
        Assert.Matches(
            @"^v1\.[A-Za-z0-9_-]{43}$",
            order.SubjectId);
        Assert.Equal(request.OccurredAtUtc, order.OccurredAtUtc);
        Assert.Equal(5, order.TotalQuantity);
        Assert.Equal(2, order.DistinctProductCount);
        Assert.Equal(2, items.Length);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
        AssertNoPersonalOrFinancialEntityProperties();
    }

    [Fact]
    public async Task Subject_derivation_failure_leaves_no_partial_order_rows()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var productId = Guid.NewGuid();
        await SeedProductsAsync(database, productId);
        await using var factory = new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey,
            configureServices: services =>
            {
                services.RemoveAll<IRecommendationSubjectIdDeriver>();
                services.AddSingleton<IRecommendationSubjectIdDeriver>(
                    new ThrowingSubjectIdDeriver());
            });
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateOrder((productId, 1));

        using var response = await PostAsync(client, "orders", request);

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.OrderSnapshots.CountAsync());
        Assert.Equal(0, await context.OrderSnapshotItems.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Missing_product_is_retryable_and_leaves_no_partial_rows()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateOrder((Guid.NewGuid(), 1));

        using var response = await PostAsync(client, "orders", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("Recommendation.ProductSnapshotMissing", error!.Code);
        Assert.True(error.Retryable);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.OrderSnapshots.CountAsync());
        Assert.Equal(0, await context.OrderSnapshotItems.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Duplicate_products_and_non_positive_quantities_are_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var productId = Guid.NewGuid();
        var duplicate = RecommendationEventTestData.CreateOrder(
            (productId, 1),
            (productId, 2));
        var nonPositive = RecommendationEventTestData.CreateOrder(
            (Guid.NewGuid(), 0));

        using var duplicateResponse = await PostAsync(
            client,
            "orders",
            duplicate);
        using var quantityResponse = await PostAsync(
            client,
            "orders",
            nonPositive);

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, quantityResponse.StatusCode);
        var duplicateError = await duplicateResponse.Content
            .ReadFromJsonAsync<ApiValidationErrorResponse>();
        var quantityError = await quantityResponse.Content
            .ReadFromJsonAsync<ApiValidationErrorResponse>();
        Assert.Equal("Validation.Failed", duplicateError!.Code);
        Assert.Contains(
            duplicateError.Errors.Keys,
            key => key.EndsWith(".ProductId", StringComparison.Ordinal));
        Assert.Equal("Validation.Failed", quantityError!.Code);
        Assert.Contains(
            quantityError.Errors.Keys,
            key => key.EndsWith(".Quantity", StringComparison.Ordinal));

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.OrderSnapshots.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_order_requests_create_one_snapshot()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var productId = Guid.NewGuid();
        await SeedProductsAsync(database, productId);
        await using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var request = RecommendationEventTestData.CreateOrder((productId, 2));

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => PostAsync(client, "orders", request)));

        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var context = database.CreateContext();
        Assert.Equal(1, await context.OrderSnapshots.CountAsync());
        Assert.Equal(1, await context.OrderSnapshotItems.CountAsync());
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Migrations_and_model_snapshot_remain_current()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static RecommendationWebApplicationFactory CreateFactory(
        RecommendationTestDatabase database)
    {
        return new RecommendationWebApplicationFactory(
            database.ConnectionString,
            ApiKey);
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

    private static async Task<HttpResponseMessage> PostAsync<T>(
        HttpClient client,
        string eventRoute,
        T request)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/events/{eventRoute}")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add(ApiKeyDefaults.HeaderName, ApiKey);
        return await client.SendAsync(message);
    }

    private static async Task SeedProductsAsync(
        RecommendationTestDatabase database,
        params Guid[] productIds)
    {
        await using var context = database.CreateContext();
        context.ProductSnapshots.AddRange(productIds.Select((productId, index) =>
            new ProductSnapshot
            {
                ProductId = productId,
                Sku = $"SEED-{index}-{productId:N}",
                Name = $"Seed product {index}",
                CategoryId = Guid.NewGuid(),
                BrandId = Guid.NewGuid(),
                Price = 1m,
                NetContent = 1m,
                UnitType = UnitType.Piece,
                IsActive = true,
                IsInStock = true,
                SourceUpdatedAtUtc = RecommendationEventTestData.BaseUtc,
                ReceivedAtUtc = RecommendationEventTestData.BaseUtc
            }));
        await context.SaveChangesAsync();
    }

    private static void AssertNoPersonalOrFinancialEntityProperties()
    {
        var forbiddenNames = new HashSet<string>(
            [
                "Email",
                "Phone",
                "PhoneNumber",
                "Address",
                "PaymentMethod",
                "Price",
                "VatRate",
                "Subtotal",
                "VatTotal",
                "GrandTotal"
            ],
            StringComparer.OrdinalIgnoreCase);
        var persistedPropertyNames = typeof(OrderSnapshot)
            .GetProperties()
            .Concat(typeof(OrderSnapshotItem).GetProperties())
            .Select(property => property.Name);

        Assert.DoesNotContain(
            persistedPropertyNames,
            property => forbiddenNames.Contains(property));
    }

    private static IRecommendationSubjectIdDeriver CreateSubjectDeriver()
    {
        return new HmacRecommendationSubjectIdDeriver(
            Options.Create(new RecommendationSubjectOptions
            {
                Key =
                    "test-only-recommendation-subject-key-32-bytes-minimum",
                Version = "v1"
            }));
    }

    private sealed class ThrowingSubjectIdDeriver
        : IRecommendationSubjectIdDeriver
    {
        public string Derive(Guid customerId)
        {
            throw new InvalidOperationException(
                "Injected subject derivation failure.");
        }
    }
}
