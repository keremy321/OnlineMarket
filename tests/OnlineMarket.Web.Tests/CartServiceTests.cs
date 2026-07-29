using OnlineMarket.Web.Application.Services;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class CartServiceTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public CartServiceTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task AddUpdateAndClearUseIsolatedMigratedSqlDatabase()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
            context,
            stockQuantity: 50,
            cartQuantity: 1);
        context.CartItems.RemoveRange(context.CartItems);
        await context.SaveChangesAsync();
        var service = new CartService(context);

        var cart = await service.AddItemToCartAsync(
            scenario.CustomerId,
            scenario.ProductId,
            2);
        Assert.Single(cart.Items);
        Assert.Equal(100m, cart.Subtotal);
        Assert.Equal(20m, cart.VatTotal);
        Assert.Equal(120m, cart.GrandTotal);

        cart = await service.UpdateItemQuantityAsync(
            scenario.CustomerId,
            cart.Items.Single().Id,
            3);
        Assert.Equal(150m, cart.Subtotal);
        Assert.Equal(30m, cart.VatTotal);
        Assert.Equal(180m, cart.GrandTotal);

        cart = await service.ClearCartAsync(scenario.CustomerId);
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.GrandTotal);
    }
}
