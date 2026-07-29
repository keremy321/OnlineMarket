using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class CheckoutServiceTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public CheckoutServiceTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task SuccessfulCheckoutCommitsEntireAggregateAndApprovedEvents()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var arrangeContext = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                arrangeContext,
                stockQuantity: 100,
                cartQuantity: 2);
            var service = OnlineMarketTestData.CreateCheckoutService(arrangeContext);

            var result = await service.ExecuteCheckoutAsync(
                scenario.CustomerId,
                new CheckoutRequestDto(
                    scenario.AddressId,
                    "Ignored holder",
                    "**** 1234",
                    true));

            Assert.True(result.Success);
            Assert.NotNull(result.OrderId);
            Assert.Matches("^ORD-[0-9]{20}$", result.OrderNumber);
        }

        await using var context = database.CreateContext();
        var order = await context.Orders
            .Include(candidate => candidate.AddressSnapshot)
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.Payment)
            .SingleAsync(candidate => candidate.CustomerId == scenario.CustomerId);

        Assert.Equal(100m, order.Subtotal);
        Assert.Equal(20m, order.VatTotal);
        Assert.Equal(120m, order.GrandTotal);
        Assert.NotNull(order.AddressSnapshot);
        Assert.Equal("34000", order.AddressSnapshot.PostalCode);
        Assert.Single(order.Items);
        Assert.Equal(50m, order.Items.Single().UnitPrice);
        Assert.NotNull(order.Payment);
        Assert.Equal(PaymentMethod.CardSimulation, order.Payment.Method);
        Assert.Equal(PaymentStatus.Succeeded, order.Payment.Status);
        Assert.Equal(98, await context.Stocks
            .Where(stock => stock.ProductId == scenario.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
        Assert.Equal(
            CartStatus.Converted,
            await context.Carts
                .Where(cart => cart.Id == scenario.CartId)
                .Select(cart => cart.Status)
                .SingleAsync());

        var movement = await context.StockMovements.SingleAsync();
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal(-2, movement.QuantityChange);
        Assert.Equal(100, movement.PreviousQuantity);
        Assert.Equal(98, movement.NewQuantity);
        Assert.Equal(StockReferenceType.Order, movement.ReferenceType);
        Assert.Equal(order.Id, movement.ReferenceId);

        var messages = await context.OutboxMessages
            .Where(message => message.AggregateId == order.Id)
            .OrderBy(message => message.EventType)
            .ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages.Select(message => message.EventId).Distinct().Count());
        Assert.All(messages, message => Assert.Equal(order.CorrelationId, message.CorrelationId));

        AssertRecommendationContract(
            messages.Single(message =>
                message.EventType == nameof(OrderConfirmedForRecommendationV1)).Payload);
        AssertErpContract(
            messages.Single(message =>
                message.EventType == nameof(OrderReadyForErpV1)).Payload);
    }

    [Fact]
    public async Task FailureAfterFirstStockMutationRollsBackEveryCheckoutWrite()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        Guid secondProductId;

        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 5,
                cartQuantity: 1);
            secondProductId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            context.Products.Add(new Product
            {
                Id = secondProductId,
                Sku = $"ROLLBACK-{Guid.NewGuid():N}",
                Name = "Rollback product",
                Slug = $"rollback-{Guid.NewGuid():N}",
                CategoryId = scenario.CategoryId,
                BrandId = scenario.BrandId,
                Price = 10m,
                VatRate = 20m,
                NetContent = 1m,
                UnitType = UnitType.Piece,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            context.Stocks.Add(new Stock
            {
                ProductId = secondProductId,
                Quantity = 5,
                UpdatedAtUtc = now
            });
            context.CartItems.Add(new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = scenario.CartId,
                ProductId = secondProductId,
                Quantity = 1,
                LastKnownUnitPrice = 10m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await context.SaveChangesAsync();

            var stockService = new FailAfterFirstMutationService(
                new SqlServerStockMutationService(context));
            var service = new CheckoutService(
                context,
                stockService,
                new SqlServerOrderNumberGenerator(context),
                NullLogger<CheckoutService>.Instance);

            var result = await service.ExecuteCheckoutAsync(
                scenario.CustomerId,
                new CheckoutRequestDto(scenario.AddressId, "", "", true));

            Assert.False(result.Success);
        }

        await using var verification = database.CreateContext();
        Assert.Equal(
            [5, 5],
            await verification.Stocks
                .OrderBy(stock => stock.ProductId)
                .Select(stock => stock.Quantity)
                .ToListAsync());
        Assert.Equal(0, await verification.Orders.CountAsync());
        Assert.Equal(0, await verification.Payments.CountAsync());
        Assert.Equal(0, await verification.StockMovements.CountAsync());
        Assert.Equal(0, await verification.OutboxMessages.CountAsync());
        Assert.Equal(
            CartStatus.Active,
            await verification.Carts
                .Where(cart => cart.Id == scenario.CartId)
                .Select(cart => cart.Status)
                .SingleAsync());
    }

    [Fact]
    public async Task InsufficientStockCreatesNoPartialOrder()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 0,
                cartQuantity: 1);
            var result = await OnlineMarketTestData.CreateCheckoutService(context)
                .ExecuteCheckoutAsync(
                    scenario.CustomerId,
                    new CheckoutRequestDto(scenario.AddressId, "", "", true));

            Assert.False(result.Success);
            Assert.Contains("yetersiz stok", result.ErrorMessage);
        }

        await using var verification = database.CreateContext();
        Assert.Equal(0, await verification.Orders.CountAsync());
        Assert.Equal(0, await verification.StockMovements.CountAsync());
        Assert.Equal(0, await verification.OutboxMessages.CountAsync());
        Assert.Equal(0, await verification.Stocks
            .Where(stock => stock.ProductId == scenario.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
    }

    [Fact]
    public async Task StockOneWithTwoConcurrentCheckoutsAllowsOneSuccess()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario first;
        CheckoutScenario second;
        await using (var context = database.CreateContext())
        {
            first = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 1,
                cartQuantity: 1);
            second = await OnlineMarketTestData.AddCustomerCartForProductAsync(
                context,
                first.CategoryId,
                first.BrandId,
                first.ProductId,
                1);
        }

        var firstTask = ExecuteCheckoutAsync(database, first);
        var secondTask = ExecuteCheckoutAsync(database, second);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.Success);
        await using var verification = database.CreateContext();
        Assert.Equal(0, await verification.Stocks
            .Where(stock => stock.ProductId == first.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
        Assert.Equal(1, await verification.Orders.CountAsync());
        Assert.Equal(1, await verification.StockMovements.CountAsync());
        Assert.Equal(2, await verification.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ConcurrentSuccessfulCheckoutsUseUniqueSequenceNumbers()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario first;
        CheckoutScenario second;
        await using (var context = database.CreateContext())
        {
            first = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 2,
                cartQuantity: 1);
            second = await OnlineMarketTestData.AddCustomerCartForProductAsync(
                context,
                first.CategoryId,
                first.BrandId,
                first.ProductId,
                1);
        }

        var results = await Task.WhenAll(
            ExecuteCheckoutAsync(database, first),
            ExecuteCheckoutAsync(database, second));

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(2, results.Select(result => result.OrderNumber).Distinct().Count());
        Assert.All(results, result => Assert.Matches("^ORD-[0-9]{20}$", result.OrderNumber));
    }

    [Fact]
    public async Task ConcurrentAttemptsForOneSourceCartCreateOnlyOneOrder()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 2,
                cartQuantity: 1);
        }

        var results = await Task.WhenAll(
            ExecuteCheckoutAsync(database, scenario),
            ExecuteCheckoutAsync(database, scenario));

        Assert.Single(results, result => result.Success);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.Orders.CountAsync());
        Assert.Equal(1, await verification.StockMovements.CountAsync());
        Assert.Equal(1, await verification.Stocks
            .Where(stock => stock.ProductId == scenario.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
    }

    [Fact]
    public async Task FailedPaymentSimulationLeavesStockAndOrderStateUntouched()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario scenario;
        await using (var context = database.CreateContext())
        {
            scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 3,
                cartQuantity: 1);
            var result = await OnlineMarketTestData.CreateCheckoutService(context)
                .ExecuteCheckoutAsync(
                    scenario.CustomerId,
                    new CheckoutRequestDto(scenario.AddressId, "", "", false));

            Assert.False(result.Success);
        }

        await using var verification = database.CreateContext();
        Assert.Equal(3, await verification.Stocks
            .Where(stock => stock.ProductId == scenario.ProductId)
            .Select(stock => stock.Quantity)
            .SingleAsync());
        Assert.Equal(0, await verification.Orders.CountAsync());
        Assert.Equal(0, await verification.OutboxMessages.CountAsync());
    }

    [Fact]
    public void CanonicalEventSerializationIsStableAndRejectsDuplicateErpProducts()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        var item = new ErpOrderItemV1(
            productId,
            "SKU",
            "Product",
            1,
            10m,
            20m,
            10m,
            2m,
            12m);
        var integrationEvent = new OrderReadyForErpV1(
            eventId,
            occurredAt,
            correlationId,
            orderId,
            "ORD-00000000000000000001",
            occurredAt,
            PaymentMethod.CardSimulation,
            new ErpOrderCustomerV1(Guid.NewGuid(), "First", "Last", "user@example.test"),
            new ErpOrderAddressV1("Recipient", "555", "Line", null, "District", "City", null, "TR"),
            new ErpOrderTotalsV1(10m, 2m, 12m, "TRY"),
            [item]);

        Assert.Equal(
            EventJsonSerializer.Serialize(integrationEvent),
            EventJsonSerializer.Serialize(integrationEvent));
        Assert.Throws<ArgumentException>(() => new OrderReadyForErpV1(
            eventId,
            occurredAt,
            correlationId,
            orderId,
            "ORD-00000000000000000001",
            occurredAt,
            PaymentMethod.CardSimulation,
            integrationEvent.Customer,
            integrationEvent.Address,
            integrationEvent.Totals,
            [item, item]));
    }

    private static async Task<CheckoutResultDto> ExecuteCheckoutAsync(
        OnlineMarketTestDatabase database,
        CheckoutScenario scenario)
    {
        await using var context = database.CreateContext();
        return await OnlineMarketTestData.CreateCheckoutService(context)
            .ExecuteCheckoutAsync(
                scenario.CustomerId,
                new CheckoutRequestDto(scenario.AddressId, "", "", true));
    }

    private static void AssertRecommendationContract(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(
            ["CorrelationId", "CustomerId", "EventId", "Items", "OccurredAtUtc", "OrderId", "OrderNumber"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var item = Assert.Single(root.GetProperty("Items").EnumerateArray());
        Assert.Equal(
            ["ProductId", "Quantity"],
            item.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("Email", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Address", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Price", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Total", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payment", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertErpContract(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(
            [
                "Address",
                "CorrelationId",
                "Customer",
                "EventId",
                "Items",
                "OccurredAtUtc",
                "OrderId",
                "OrderNumber",
                "OrderPlacedAtUtc",
                "PaymentMethod",
                "Totals"
            ],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["CustomerId", "Email", "FirstName", "LastName"],
            root.GetProperty("Customer").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            [
                "AddressLine1",
                "AddressLine2",
                "City",
                "CountryCode",
                "District",
                "PhoneNumber",
                "PostalCode",
                "RecipientName"
            ],
            root.GetProperty("Address").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["Currency", "GrandTotal", "Subtotal", "VatTotal"],
            root.GetProperty("Totals").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        var item = Assert.Single(root.GetProperty("Items").EnumerateArray());
        Assert.Equal(
            [
                "LineTotal",
                "NetLineAmount",
                "ProductId",
                "ProductName",
                "Quantity",
                "Sku",
                "UnitPrice",
                "VatAmount",
                "VatRate"
            ],
            item.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal((byte)PaymentMethod.CardSimulation, root.GetProperty("PaymentMethod").GetByte());

        string[] forbiddenTerms =
        [
            "CardNumber",
            "Cvv",
            "Expiry",
            "PaymentToken",
            "ProviderSecret"
        ];
        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, payload, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FailAfterFirstMutationService : IStockMutationService
    {
        private readonly IStockMutationService inner;
        private int decreaseCount;

        public FailAfterFirstMutationService(IStockMutationService inner)
        {
            this.inner = inner;
        }

        public async Task<StockMutationResultDto?> TryDecreaseAsync(
            Guid productId,
            int requestedQuantity,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            decreaseCount++;
            return decreaseCount == 1
                ? await inner.TryDecreaseAsync(
                    productId,
                    requestedQuantity,
                    updatedAtUtc,
                    cancellationToken)
                : null;
        }

        public Task<StockMutationResultDto?> TryAdjustAsync(
            Guid productId,
            int quantityChange,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            return inner.TryAdjustAsync(
                productId,
                quantityChange,
                updatedAtUtc,
                cancellationToken);
        }
    }
}
