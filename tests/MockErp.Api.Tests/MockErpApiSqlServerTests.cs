using Microsoft.EntityFrameworkCore;
using MockErp.Api.Contracts;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Tests;

[Collection(MockErpSqlServerCollection.CollectionName)]
public sealed class MockErpApiSqlServerTests(
    MockErpSqlServerFixture fixture)
{
    [Fact]
    public async Task Ensure_customer_is_idempotent_and_payload_conflicts_are_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var request = MockErpApiTestData.CreateEnsureCustomerRequest();
        var key = $"customer:{Guid.NewGuid()}";

        var created = await service.EnsureCustomerAsync(request, key);
        var replay = await service.EnsureCustomerAsync(request, key);
        var conflict = await service.EnsureCustomerAsync(
            request with { Email = "different@example.test" },
            key);

        var response = MockErpApiTestData.Read<EnsureCustomerResponse>(created);
        Assert.True(response.Created);
        Assert.Equal(201, created.Response!.StatusCode);
        Assert.Equal(created.Response, replay.Response);
        Assert.False(conflict.Succeeded);
        Assert.Equal("Idempotency.PayloadConflict", conflict.Error!.Code);
        Assert.False(conflict.Error.Retryable);
        Assert.Equal(1, await context.ErpCustomers.CountAsync());
        Assert.Equal(1, await context.ErpIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task Concurrent_ensure_customer_requests_create_one_customer()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var request = MockErpApiTestData.CreateEnsureCustomerRequest();
        var key = $"customer:{Guid.NewGuid()}";

        var results = await Task.WhenAll(
            MockErpApiTestData.CreateService(firstContext)
                .EnsureCustomerAsync(request, key),
            MockErpApiTestData.CreateService(secondContext)
                .EnsureCustomerAsync(request, key));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(results[0].Response, results[1].Response);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ErpCustomers.CountAsync());
        Assert.Equal(1, await verification.ErpIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task Create_order_commits_header_address_lines_and_idempotency_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var customer = MockErpApiTestData.Read<EnsureCustomerResponse>(
            await service.EnsureCustomerAsync(
                MockErpApiTestData.CreateEnsureCustomerRequest(),
                $"customer:{Guid.NewGuid()}"));
        var request = MockErpApiTestData.CreateOrderRequest(
            customer.ErpCustomerCode);

        var created = await service.CreateOrderAsync(
            request,
            $"order:{request.ExternalOrderId}");
        var response = MockErpApiTestData.Read<CreateOrderResponse>(created);

        context.ChangeTracker.Clear();
        var order = await context.ErpOrders
            .AsNoTracking()
            .Include(item => item.Address)
            .Include(item => item.Lines)
            .SingleAsync(item => item.Id == response.ErpOrderId);
        Assert.NotNull(order.Address);
        Assert.Single(order.Lines);
        Assert.Equal(PaymentMethod.CardSimulation, order.PaymentMethod);
        Assert.True(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.ResourceId == order.Id));

        var duplicate = MockErpApiTestData.CreateOrderRequest(
            customer.ErpCustomerCode,
            externalOrderId: request.ExternalOrderId);
        var conflict = await service.CreateOrderAsync(
            duplicate,
            $"order:{Guid.NewGuid()}");
        Assert.Equal("MockErp.ResourceConflict", conflict.Error!.Code);
        Assert.Equal(1, await context.ErpOrders.CountAsync());
    }

    [Fact]
    public async Task Invalid_or_failed_order_creates_no_resource_or_idempotency_record()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var missingCustomerRequest = MockErpApiTestData.CreateOrderRequest(
            "CARI-MISSING");
        var missingKey = $"order:{Guid.NewGuid()}";

        var missingCustomer = await service.CreateOrderAsync(
            missingCustomerRequest,
            missingKey);
        Assert.Equal("MockErp.CustomerNotFound", missingCustomer.Error!.Code);

        var customer = MockErpApiTestData.Read<EnsureCustomerResponse>(
            await service.EnsureCustomerAsync(
                MockErpApiTestData.CreateEnsureCustomerRequest(),
                $"customer:{Guid.NewGuid()}"));
        var invalid = MockErpApiTestData.CreateOrderRequest(
            customer.ErpCustomerCode);
        invalid = invalid with
        {
            Subtotal = 200m,
            VatTotal = 40m,
            GrandTotal = 240m,
            Lines = [invalid.Lines![0], invalid.Lines[0]]
        };
        var invalidKey = $"order:{Guid.NewGuid()}";
        var validation = await service.CreateOrderAsync(invalid, invalidKey);

        Assert.NotNull(validation.ValidationErrors);
        Assert.Contains(
            "Lines[1].ExternalProductId",
            validation.ValidationErrors.Keys);
        Assert.Equal(0, await context.ErpOrders.CountAsync());
        Assert.False(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.IdempotencyKey == missingKey
                || item.IdempotencyKey == invalidKey));
    }

    [Fact]
    public async Task Stock_decreases_and_movements_commit_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var (orderRequest, orderResponse) = await CreateOrderAsync(
            database,
            firstProductId,
            secondProductId);
        await using (var setup = database.CreateContext())
        {
            setup.ErpStocks.AddRange(
                MockErpApiTestData.CreateStock(firstProductId, 10),
                MockErpApiTestData.CreateStock(secondProductId, 8));
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var request = new CreateStockMovementsRequest
        {
            ExternalOrderId = orderRequest.ExternalOrderId,
            Lines =
            [
                new CreateStockMovementLineRequest
                {
                    ExternalProductId = firstProductId,
                    QuantityChange = -2
                },
                new CreateStockMovementLineRequest
                {
                    ExternalProductId = secondProductId,
                    QuantityChange = -2
                }
            ]
        };
        var result = await service.CreateStockMovementsAsync(
            request,
            $"stock:{orderRequest.ExternalOrderId}");

        var response =
            MockErpApiTestData.Read<CreateStockMovementsResponse>(result);
        Assert.Equal(orderResponse.ErpOrderId, response.ErpOrderId);
        Assert.Equal(2, response.Movements.Count);
        context.ChangeTracker.Clear();
        var quantities = await context.ErpStocks
            .OrderByDescending(item => item.Quantity)
            .Select(item => item.Quantity)
            .ToArrayAsync();
        Assert.Equal(
            [8, 6],
            quantities);
        Assert.Equal(2, await context.ErpStockMovements.CountAsync());
        Assert.True(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.IdempotencyKey
                == $"stock:{orderRequest.ExternalOrderId}"));
    }

    [Fact]
    public async Task Insufficient_multi_line_stock_rolls_back_every_mutation()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var (orderRequest, _) = await CreateOrderAsync(
            database,
            firstProductId,
            secondProductId);
        await using (var setup = database.CreateContext())
        {
            setup.ErpStocks.AddRange(
                MockErpApiTestData.CreateStock(firstProductId, 10),
                MockErpApiTestData.CreateStock(secondProductId, 1));
            await setup.SaveChangesAsync();
        }

        var key = $"stock:{Guid.NewGuid()}";
        await using (var context = database.CreateContext())
        {
            var result = await MockErpApiTestData.CreateService(context)
                .CreateStockMovementsAsync(
                    new CreateStockMovementsRequest
                    {
                        ExternalOrderId = orderRequest.ExternalOrderId,
                        Lines =
                        [
                            new CreateStockMovementLineRequest
                            {
                                ExternalProductId = firstProductId,
                                QuantityChange = -2
                            },
                            new CreateStockMovementLineRequest
                            {
                                ExternalProductId = secondProductId,
                                QuantityChange = -2
                            }
                        ]
                    },
                    key);
            Assert.Equal("MockErp.InsufficientStock", result.Error!.Code);
        }

        await using var verification = database.CreateContext();
        var quantities = await verification.ErpStocks
            .OrderByDescending(item => item.Quantity)
            .Select(item => item.Quantity)
            .ToArrayAsync();
        Assert.Equal(
            [10, 1],
            quantities);
        Assert.Equal(0, await verification.ErpStockMovements.CountAsync());
        Assert.False(await verification.ErpIdempotencyRecords.AnyAsync(
            item => item.IdempotencyKey == key));
    }

    [Fact]
    public async Task Duplicate_and_concurrent_stock_requests_never_create_negative_stock()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var productId = Guid.NewGuid();
        var (firstOrder, _) = await CreateOrderAsync(database, productId);
        var (secondOrder, _) = await CreateOrderAsync(database, productId);
        await using (var setup = database.CreateContext())
        {
            setup.ErpStocks.Add(
                MockErpApiTestData.CreateStock(productId, 3));
            await setup.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstRequest = StockRequest(firstOrder.ExternalOrderId, productId, -2);
        var secondRequest = StockRequest(secondOrder.ExternalOrderId, productId, -2);
        var results = await Task.WhenAll(
            MockErpApiTestData.CreateService(firstContext)
                .CreateStockMovementsAsync(firstRequest, $"stock:{Guid.NewGuid()}"),
            MockErpApiTestData.CreateService(secondContext)
                .CreateStockMovementsAsync(secondRequest, $"stock:{Guid.NewGuid()}"));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(
            results,
            result => result.Error?.Code == "MockErp.InsufficientStock");

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ErpStocks
            .Select(item => item.Quantity)
            .SingleAsync());
        Assert.Equal(1, await verification.ErpStockMovements.CountAsync());

        var succeededOrder = results[0].Succeeded ? firstOrder : secondOrder;
        await using var duplicateContext = database.CreateContext();
        var duplicate = await MockErpApiTestData.CreateService(duplicateContext)
            .CreateStockMovementsAsync(
                StockRequest(
                    succeededOrder.ExternalOrderId,
                    productId,
                    -2),
                $"stock:{Guid.NewGuid()}");
        Assert.Equal("MockErp.ResourceConflict", duplicate.Error!.Code);
        Assert.Equal(1, await duplicateContext.ErpStockMovements.CountAsync());
    }

    [Fact]
    public async Task Accounting_entry_and_three_balanced_account_lines_commit_together()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var (orderRequest, orderResponse) = await CreateOrderAsync(
            database,
            Guid.NewGuid());
        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var key = $"accounting:{orderRequest.ExternalOrderId}";
        var request = new CreateAccountingEntryRequest
        {
            ExternalOrderId = orderRequest.ExternalOrderId,
            EntryDateUtc = new DateTime(
                2026,
                7,
                30,
                13,
                0,
                0,
                DateTimeKind.Utc),
            Description = "Project sales voucher"
        };

        var result = await service.CreateAccountingEntryAsync(request, key);
        var replay = await service.CreateAccountingEntryAsync(request, key);
        Assert.Equal(result.Response, replay.Response);
        var response =
            MockErpApiTestData.Read<CreateAccountingEntryResponse>(result);

        context.ChangeTracker.Clear();
        var entry = await context.ErpAccountingEntries
            .AsNoTracking()
            .Include(item => item.Lines)
            .SingleAsync(item => item.Id == response.AccountingEntryId);
        var lines = entry.Lines.OrderBy(item => item.SequenceNumber).ToArray();
        Assert.Equal(orderResponse.ErpOrderId, entry.ErpOrderId);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);
        Assert.Equal(["120", "600", "391"], lines.Select(item => item.AccountCode));
        Assert.Equal(
            ["Alıcılar", "Yurtiçi Satışlar", "Hesaplanan KDV"],
            lines.Select(item => item.AccountName));
        Assert.Equal(120m, lines[0].DebitAmount);
        Assert.NotNull(lines[0].ErpCustomerId);
        Assert.Equal(100m, lines[1].CreditAmount);
        Assert.Equal(20m, lines[2].CreditAmount);
        Assert.Equal(PaymentMethod.CardSimulation, entry.PaymentMethod);
        Assert.True(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.IdempotencyKey == key));

        var duplicate = await service.CreateAccountingEntryAsync(
            request,
            $"accounting:{Guid.NewGuid()}");
        Assert.Equal("MockErp.ResourceConflict", duplicate.Error!.Code);
        Assert.Equal(1, await context.ErpAccountingEntries.CountAsync());
    }

    [Fact]
    public async Task Zero_vat_order_creates_balanced_voucher_without_vat_line()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        CreateOrderRequest orderRequest;
        CreateOrderResponse orderResponse;
        await using (var orderContext = database.CreateContext())
        {
            var service = MockErpApiTestData.CreateService(orderContext);
            var customer = MockErpApiTestData.Read<EnsureCustomerResponse>(
                await service.EnsureCustomerAsync(
                    MockErpApiTestData.CreateEnsureCustomerRequest(),
                    $"customer:{Guid.NewGuid()}"));
            orderRequest = MockErpApiTestData.CreateOrderRequest(
                customer.ErpCustomerCode);
            var line = orderRequest.Lines![0]!;
            orderRequest = orderRequest with
            {
                VatTotal = 0m,
                GrandTotal = 100m,
                Lines =
                [
                    line with
                    {
                        VatRate = 0m,
                        VatAmount = 0m,
                        LineTotal = 100m
                    }
                ]
            };
            orderResponse = MockErpApiTestData.Read<CreateOrderResponse>(
                await service.CreateOrderAsync(
                    orderRequest,
                    $"order:{orderRequest.ExternalOrderId}"));
        }

        await using var context = database.CreateContext();
        var accountingKey = $"accounting:{orderRequest.ExternalOrderId}";
        var result = await MockErpApiTestData.CreateService(context)
            .CreateAccountingEntryAsync(
                new CreateAccountingEntryRequest
                {
                    ExternalOrderId = orderRequest.ExternalOrderId,
                    EntryDateUtc = new DateTime(
                        2026,
                        7,
                        30,
                        13,
                        0,
                        0,
                        DateTimeKind.Utc),
                    Description = "Zero VAT sales voucher"
                },
                accountingKey);
        var response =
            MockErpApiTestData.Read<CreateAccountingEntryResponse>(result);

        context.ChangeTracker.Clear();
        var entry = await context.ErpAccountingEntries
            .AsNoTracking()
            .Include(item => item.Lines)
            .SingleAsync(item => item.Id == response.AccountingEntryId);
        var lines = entry.Lines.OrderBy(item => item.SequenceNumber).ToArray();

        Assert.Equal(orderResponse.ErpOrderId, entry.ErpOrderId);
        Assert.Equal(100m, entry.TotalDebit);
        Assert.Equal(100m, entry.TotalCredit);
        Assert.Equal(
            entry.TotalDebit,
            lines.Sum(item => item.DebitAmount));
        Assert.Equal(
            entry.TotalCredit,
            lines.Sum(item => item.CreditAmount));
        Assert.Equal(["120", "600"], lines.Select(item => item.AccountCode));
        Assert.DoesNotContain(lines, item => item.AccountCode == "391");
        Assert.All(
            lines,
            item => Assert.True(
                item.DebitAmount > 0 ^ item.CreditAmount > 0));
        Assert.True(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.IdempotencyKey == accountingKey));
    }

    [Fact]
    public async Task Customer_history_returns_only_that_customers_orders_newest_first()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        string firstCode;
        Guid olderOrderId;
        Guid newerOrderId;
        await using (var context = database.CreateContext())
        {
            var service = MockErpApiTestData.CreateService(context);
            var firstCustomer = MockErpApiTestData.Read<EnsureCustomerResponse>(
                await service.EnsureCustomerAsync(
                    MockErpApiTestData.CreateEnsureCustomerRequest(),
                    $"customer:{Guid.NewGuid()}"));
            var secondCustomer = MockErpApiTestData.Read<EnsureCustomerResponse>(
                await service.EnsureCustomerAsync(
                    MockErpApiTestData.CreateEnsureCustomerRequest(),
                    $"customer:{Guid.NewGuid()}"));
            firstCode = firstCustomer.ErpCustomerCode;
            var older = MockErpApiTestData.CreateOrderRequest(
                firstCode,
                placedAtUtc: new DateTime(
                    2026, 7, 29, 12, 0, 0, DateTimeKind.Utc));
            var newer = MockErpApiTestData.CreateOrderRequest(
                firstCode,
                placedAtUtc: new DateTime(
                    2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
            var other = MockErpApiTestData.CreateOrderRequest(
                secondCustomer.ErpCustomerCode);
            olderOrderId = older.ExternalOrderId;
            newerOrderId = newer.ExternalOrderId;
            Assert.True((await service.CreateOrderAsync(
                older,
                $"order:{older.ExternalOrderId}")).Succeeded);
            Assert.True((await service.CreateOrderAsync(
                newer,
                $"order:{newer.ExternalOrderId}")).Succeeded);
            Assert.True((await service.CreateOrderAsync(
                other,
                $"order:{other.ExternalOrderId}")).Succeeded);
        }

        await using var historyContext = database.CreateContext();
        var history = await MockErpApiTestData.CreateService(historyContext)
            .GetCustomerOrdersAsync(firstCode);

        Assert.True(history.Succeeded);
        Assert.Equal(
            [newerOrderId, olderOrderId],
            history.Response!.Orders.Select(item => item.ExternalOrderId));
        Assert.All(history.Response.Orders, item =>
        {
            Assert.NotNull(item.Address);
            Assert.NotEmpty(item.Lines);
            Assert.Equal(PaymentMethod.CardSimulation, item.PaymentMethod);
        });
        var missing = await MockErpApiTestData.CreateService(historyContext)
            .GetCustomerOrdersAsync("CARI-DOES-NOT-EXIST");
        Assert.Equal("MockErp.CustomerNotFound", missing.Error!.Code);
    }

    private static async Task<(CreateOrderRequest Request, CreateOrderResponse Response)>
        CreateOrderAsync(
            MockErpTestDatabase database,
            params Guid[] productIds)
    {
        await using var context = database.CreateContext();
        var service = MockErpApiTestData.CreateService(context);
        var customer = MockErpApiTestData.Read<EnsureCustomerResponse>(
            await service.EnsureCustomerAsync(
                MockErpApiTestData.CreateEnsureCustomerRequest(),
                $"customer:{Guid.NewGuid()}"));
        var request = MockErpApiTestData.CreateOrderRequest(
            customer.ErpCustomerCode,
            externalProductId: productIds[0]);
        if (productIds.Length > 1)
        {
            var first = request.Lines![0]!;
            request = request with
            {
                Subtotal = 200m,
                VatTotal = 40m,
                GrandTotal = 240m,
                Lines =
                [
                    first,
                    first with
                    {
                        ExternalProductId = productIds[1],
                        Sku = $"SKU-{Guid.NewGuid():N}"
                    }
                ]
            };
        }

        var result = await service.CreateOrderAsync(
            request,
            $"order:{request.ExternalOrderId}");
        return (
            request,
            MockErpApiTestData.Read<CreateOrderResponse>(result));
    }

    private static CreateStockMovementsRequest StockRequest(
        Guid externalOrderId,
        Guid productId,
        int quantityChange)
    {
        return new CreateStockMovementsRequest
        {
            ExternalOrderId = externalOrderId,
            Lines =
            [
                new CreateStockMovementLineRequest
                {
                    ExternalProductId = productId,
                    QuantityChange = quantityChange
                }
            ]
        };
    }
}
