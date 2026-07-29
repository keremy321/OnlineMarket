using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Controllers;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Tests;

[Collection(OnlineMarketSqlServerCollection.CollectionName)]
public sealed class SecurityAndMvcBoundaryTests
{
    private readonly OnlineMarketSqlServerFixture fixture;

    public SecurityAndMvcBoundaryTests(OnlineMarketSqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task CustomerScopedServicesRejectAnotherCustomersResources()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CheckoutScenario owner;
        CheckoutScenario other;
        Guid orderId;

        await using (var context = database.CreateContext())
        {
            owner = await OnlineMarketTestData.SeedCheckoutScenarioAsync(
                context,
                stockQuantity: 5,
                cartQuantity: 1);
            other = await OnlineMarketTestData.AddCustomerCartForProductAsync(
                context,
                owner.CategoryId,
                owner.BrandId,
                owner.ProductId,
                1);

            var addressService = new CustomerAddressService(context);
            Assert.Null(await addressService.GetAddressByIdAsync(
                owner.AddressId,
                other.CustomerId));

            var cartService = new CartService(context);
            await cartService.UpdateItemQuantityAsync(
                other.CustomerId,
                owner.CartItemId,
                99);
            context.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await context.CartItems
                    .Where(item => item.Id == owner.CartItemId)
                    .Select(item => item.Quantity)
                    .SingleAsync());

            var checkout = await OnlineMarketTestData.CreateCheckoutService(context)
                .ExecuteCheckoutAsync(
                    owner.CustomerId,
                    new CheckoutRequestDto(owner.AddressId, "", "", true));
            Assert.True(checkout.Success);
            orderId = checkout.OrderId!.Value;
        }

        await using (var context = database.CreateContext())
        {
            var erpClient = new RecordingErpClient();
            var orderService = new OrderService(context, erpClient);
            Assert.Null(await orderService.GetOrderByIdAsync(orderId, other.CustomerId));
            Assert.Equal(0, erpClient.CallCount);
        }
    }

    [Fact]
    public void AdminControllerRequiresAdminRoleAndHasNoDbContextDependency()
    {
        var authorize = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize.Roles);
        var constructor = Assert.Single(typeof(AdminController).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(OnlineMarketDbContext));
    }

    [Fact]
    public void EveryStateChangingMvcActionRequiresAntiforgeryValidation()
    {
        var controllerTypes = typeof(AdminController).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(Controller).IsAssignableFrom(type));
        var postActions = controllerTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(postActions);
        Assert.All(
            postActions,
            method => Assert.NotNull(
                method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()));
    }

    [Fact]
    public void AdminOutboxViewModelDoesNotExposePersistenceEntity()
    {
        Assert.False(typeof(OutboxMessage).IsAssignableFrom(
            typeof(AdminOutboxMessageViewModel)));
        Assert.DoesNotContain(
            typeof(AdminOutboxMessageViewModel).GetProperties(),
            property => typeof(OutboxMessage).IsAssignableFrom(property.PropertyType));
    }

    private sealed class RecordingErpClient : IErpIntegrationClient
    {
        public int CallCount { get; private set; }

        public Task<ErpOrderTransferStatusDto?> GetErpOrderTransferStatusAsync(
            Guid orderId)
        {
            CallCount++;
            return Task.FromResult<ErpOrderTransferStatusDto?>(null);
        }
    }
}
