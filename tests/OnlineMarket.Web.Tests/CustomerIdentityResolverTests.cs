using OnlineMarket.Web.Infrastructure.Identity;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class CustomerIdentityResolverTests(
    OnlineMarketSqlServerFixture fixture)
{
    [Fact]
    public async Task Resolves_only_the_active_customer_for_the_identity_user()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var scenario = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
            context);
        var resolver = new CustomerIdentityResolver(context);

        var customerId = await resolver.GetActiveCustomerIdByUserIdAsync(
            scenario.UserId);

        Assert.Equal(scenario.CustomerId, customerId);
        Assert.Null(await resolver.GetActiveCustomerIdByUserIdAsync(Guid.Empty));
        Assert.Null(await resolver.GetActiveCustomerIdByUserIdAsync(Guid.NewGuid()));

        var customer = await context.Customers.FindAsync(scenario.CustomerId);
        Assert.NotNull(customer);
        customer.IsActive = false;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();

        Assert.Null(await resolver.GetActiveCustomerIdByUserIdAsync(
            scenario.UserId));
    }
}
