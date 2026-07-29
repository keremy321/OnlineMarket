using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Tests;

[Collection(MockErpSqlServerCollection.CollectionName)]
public sealed class MockErpDbContextSqlServerTests(
    MockErpSqlServerFixture fixture)
{
    private static readonly string[] ExpectedTables =
    [
        "ErpAccountingEntries",
        "ErpAccountingEntryLines",
        "ErpCustomers",
        "ErpIdempotencyRecords",
        "ErpOrderAddresses",
        "ErpOrderLines",
        "ErpOrders",
        "ErpStockMovements",
        "ErpStocks"
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "IX_ErpAccountingEntries_ErpCustomerId_EntryDateUtc",
        "IX_ErpAccountingEntryLines_AccountCode_CreatedAtUtc",
        "IX_ErpAccountingEntryLines_ErpCustomerId_CreatedAtUtc",
        "IX_ErpCustomers_Email",
        "IX_ErpIdempotencyRecords_OperationType_CreatedAtUtc",
        "IX_ErpIdempotencyRecords_ResourceType_ResourceId",
        "IX_ErpOrderLines_ExternalProductId",
        "IX_ErpOrders_ErpCustomerId_CreatedAtUtc",
        "IX_ErpStockMovements_ErpOrderId",
        "IX_ErpStockMovements_ExternalProductId_CreatedAtUtc",
        "IX_ErpStocks_Quantity",
        "UX_ErpAccountingEntries_ErpOrderId",
        "UX_ErpAccountingEntries_ErpVoucherNumber",
        "UX_ErpAccountingEntries_ExternalOrderId",
        "UX_ErpAccountingEntryLines_Entry_Account",
        "UX_ErpAccountingEntryLines_Entry_Sequence",
        "UX_ErpCustomers_ErpCustomerCode",
        "UX_ErpCustomers_ExternalCustomerId",
        "UX_ErpIdempotencyRecords_IdempotencyKey",
        "UX_ErpOrderLines_ErpOrderId_ExternalProductId",
        "UX_ErpOrders_ErpOrderNumber",
        "UX_ErpOrders_ExternalOrderId",
        "UX_ErpOrders_MarketOrderNumber",
        "UX_ErpStockMovements_ExternalOrderId_ExternalProductId",
        "UX_ErpStocks_ExternalProductId",
        "UX_ErpStocks_Sku"
    ];

    private static readonly string[] ExpectedCheckConstraints =
    [
        "CK_ErpAccountingEntries_Amounts_NonNegative",
        "CK_ErpAccountingEntries_Balanced",
        "CK_ErpAccountingEntryLines_AccountCode_V1",
        "CK_ErpAccountingEntryLines_Amounts_NonNegative",
        "CK_ErpAccountingEntryLines_OneSided",
        "CK_ErpAccountingEntryLines_Sequence_Range",
        "CK_ErpIdempotencyRecords_ResponseBody_IsJson",
        "CK_ErpOrderLines_Amounts_NonNegative",
        "CK_ErpOrderLines_LineTotal",
        "CK_ErpOrderLines_Quantity_Positive",
        "CK_ErpOrderLines_VatRate_Range",
        "CK_ErpOrders_GrandTotal",
        "CK_ErpOrders_Totals_NonNegative",
        "CK_ErpStockMovements_Balance",
        "CK_ErpStockMovements_Quantities_NonNegative",
        "CK_ErpStockMovements_QuantityChange_NotZero",
        "CK_ErpStocks_NetContent_Positive",
        "CK_ErpStocks_Quantity_NonNegative",
        "CK_ErpStocks_ReorderLevel_NonNegative"
    ];

    [Fact]
    public async Task Empty_database_can_apply_all_mock_erp_migrations()
    {
        await using var database = await fixture.CreateDatabaseAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task All_nine_application_tables_exist()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var actualTables = await ReadNameSetAsync(
            database.ConnectionString,
            """
            SELECT [name]
            FROM sys.tables
            WHERE [schema_id] = SCHEMA_ID(N'dbo')
              AND [name] <> N'__EFMigrationsHistory';
            """);

        Assert.True(
            actualTables.SetEquals(ExpectedTables),
            $"Unexpected table set: {string.Join(", ", actualTables.Order())}");
    }

    [Fact]
    public async Task Expected_indexes_and_check_constraints_exist()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var actualIndexes = await ReadNameSetAsync(
            database.ConnectionString,
            """
            SELECT i.[name]
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.[object_id] = i.[object_id]
            WHERE t.[schema_id] = SCHEMA_ID(N'dbo')
              AND i.[name] IS NOT NULL
              AND i.[is_primary_key] = 0
              AND i.[is_unique_constraint] = 0;
            """);
        var actualChecks = await ReadNameSetAsync(
            database.ConnectionString,
            """
            SELECT cc.[name]
            FROM sys.check_constraints AS cc
            INNER JOIN sys.tables AS t
                ON t.[object_id] = cc.[parent_object_id]
            WHERE t.[schema_id] = SCHEMA_ID(N'dbo');
            """);

        Assert.True(
            actualIndexes.SetEquals(ExpectedIndexes),
            $"Unexpected index set: {string.Join(", ", actualIndexes.Order())}");
        Assert.True(
            actualChecks.SetEquals(ExpectedCheckConstraints),
            $"Unexpected check set: {string.Join(", ", actualChecks.Order())}");
    }

    [Theory]
    [InlineData("code")]
    [InlineData("external")]
    public async Task Customer_code_and_external_customer_id_are_unique(
        string duplicateField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateCustomer();
        var second = CreateCustomer();

        if (duplicateField == "code")
        {
            second.ErpCustomerCode = first.ErpCustomerCode;
        }
        else
        {
            second.ExternalCustomerId = first.ExternalCustomerId;
        }

        context.ErpCustomers.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("erp")]
    [InlineData("external")]
    [InlineData("market")]
    public async Task Erp_order_identifiers_are_unique(string duplicateField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var customer = CreateCustomer();
        var first = CreateOrder(customer);
        var second = CreateOrder(customer);

        switch (duplicateField)
        {
            case "erp":
                second.ErpOrderNumber = first.ErpOrderNumber;
                break;
            case "external":
                second.ExternalOrderId = first.ExternalOrderId;
                break;
            default:
                second.MarketOrderNumber = first.MarketOrderNumber;
                break;
        }

        context.ErpOrders.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_address_snapshot_can_exist_per_order()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());
        order.Address = CreateAddress(order);
        context.ErpOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.ErpOrderAddresses.Add(CreateAddress(order.Id));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("subtotal")]
    [InlineData("vat")]
    [InlineData("grand")]
    [InlineData("mismatch")]
    public async Task Erp_order_totals_are_non_negative_and_consistent(
        string invalidField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());

        switch (invalidField)
        {
            case "subtotal":
                order.Subtotal = -1m;
                order.VatTotal = 1m;
                order.GrandTotal = 0m;
                break;
            case "vat":
                order.Subtotal = 1m;
                order.VatTotal = -1m;
                order.GrandTotal = 0m;
                break;
            case "grand":
                order.Subtotal = 0m;
                order.VatTotal = 0m;
                order.GrandTotal = -1m;
                break;
            default:
                order.GrandTotal += 0.01m;
                break;
        }

        context.ErpOrders.Add(order);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_method_numeric_value_persists_correctly()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());
        order.PaymentMethod = PaymentMethod.TransferSimulation;
        context.ErpOrders.Add(order);
        await context.SaveChangesAsync();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [PaymentMethod]
            FROM [dbo].[ErpOrders]
            WHERE [Id] = @orderId;
            """;
        command.Parameters.AddWithValue("@orderId", order.Id);

        var storedValue = Convert.ToByte(await command.ExecuteScalarAsync());

        Assert.Equal((byte)3, storedValue);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("vat-low")]
    [InlineData("vat-high")]
    [InlineData("amount")]
    [InlineData("total")]
    public async Task Order_line_quantity_vat_and_amount_checks_are_enforced(
        string invalidField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var line = CreateOrderLine(CreateOrder(CreateCustomer()));

        switch (invalidField)
        {
            case "quantity":
                line.Quantity = 0;
                break;
            case "vat-low":
                line.VatRate = -0.01m;
                break;
            case "vat-high":
                line.VatRate = 100.01m;
                break;
            case "amount":
                line.UnitPrice = -0.01m;
                break;
            default:
                line.LineTotal += 0.01m;
                break;
        }

        context.ErpOrderLines.Add(line);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_order_and_product_line_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());
        var first = CreateOrderLine(order);
        var second = CreateOrderLine(order);
        second.ExternalProductId = first.ExternalProductId;
        context.ErpOrderLines.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("product")]
    [InlineData("sku")]
    public async Task Stock_external_product_id_and_sku_are_unique(
        string duplicateField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateStock();
        var second = CreateStock();

        if (duplicateField == "product")
        {
            second.ExternalProductId = first.ExternalProductId;
        }
        else
        {
            second.Sku = first.Sku;
        }

        context.ErpStocks.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("reorder")]
    public async Task Stock_quantity_and_reorder_level_cannot_be_negative(
        string invalidField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var stock = CreateStock();

        if (invalidField == "quantity")
        {
            stock.Quantity = -1;
        }
        else
        {
            stock.ReorderLevel = -1;
        }

        context.ErpStocks.Add(stock);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Stock_net_content_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var stock = CreateStock();
        stock.NetContent = 0;
        context.ErpStocks.Add(stock);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Stock_row_version_concurrency_works()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        Guid stockId;
        byte[] initialRowVersion;

        await using (var seedContext = database.CreateContext())
        {
            var stock = CreateStock();
            stockId = stock.Id;
            seedContext.ErpStocks.Add(stock);
            await seedContext.SaveChangesAsync();
            initialRowVersion = stock.RowVersion.ToArray();
        }

        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstStock = await firstContext.ErpStocks.SingleAsync(
            stock => stock.Id == stockId);
        var staleStock = await staleContext.ErpStocks.SingleAsync(
            stock => stock.Id == stockId);

        firstStock.Quantity--;
        await firstContext.SaveChangesAsync();

        Assert.NotEqual(initialRowVersion, firstStock.RowVersion);

        staleStock.Quantity--;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_stock_movement_exists_per_order_and_product()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());
        var first = CreateStockMovement(order);
        var second = CreateStockMovement(order);
        second.ExternalOrderId = first.ExternalOrderId;
        second.ExternalProductId = first.ExternalProductId;
        context.ErpStockMovements.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("zero-change")]
    public async Task Stock_movement_balance_constraints_are_enforced(
        string invalidField)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var movement = CreateStockMovement(
            CreateOrder(CreateCustomer()));

        if (invalidField == "balance")
        {
            movement.NewQuantity++;
        }
        else
        {
            movement.QuantityChange = 0;
            movement.NewQuantity = movement.PreviousQuantity;
        }

        context.ErpStockMovements.Add(movement);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_accounting_entry_can_exist_per_order()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var customer = CreateCustomer();
        var order = CreateOrder(customer);
        context.ErpOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var first = CreateAccountingEntry(
            order.Id,
            order.ExternalOrderId,
            customer.Id);
        var second = CreateAccountingEntry(
            order.Id,
            Guid.NewGuid(),
            customer.Id);
        context.ErpAccountingEntries.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Accounting_entry_has_a_direct_customer_relationship()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var customer = CreateCustomer();
        var order = CreateOrder(customer);
        context.ErpOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var entry = CreateAccountingEntry(
            order.Id,
            order.ExternalOrderId,
            Guid.NewGuid());
        context.ErpAccountingEntries.Add(entry);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Accounting_total_debit_must_equal_total_credit()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var entry = CreateAccountingEntry(
            CreateOrder(CreateCustomer()));
        entry.TotalCredit -= 0.01m;
        context.ErpAccountingEntries.Add(entry);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Accounting_sequence_number_is_unique_per_voucher()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var entry = CreateAccountingEntry(
            CreateOrder(CreateCustomer()));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 1,
            accountCode: "120",
            debitAmount: 120m,
            creditAmount: 0m,
            entry.ErpCustomer));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 1,
            accountCode: "600",
            debitAmount: 0m,
            creditAmount: 120m));
        context.ErpAccountingEntries.Add(entry);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Accounting_line_cannot_contain_both_debit_and_credit()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var entry = CreateAccountingEntry(
            CreateOrder(CreateCustomer()));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 1,
            accountCode: "120",
            debitAmount: 120m,
            creditAmount: 1m,
            entry.ErpCustomer));
        context.ErpAccountingEntries.Add(entry);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Accounting_mapping_supports_accounts_120_600_and_391()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var entry = CreateAccountingEntry(
            CreateOrder(CreateCustomer()));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 1,
            accountCode: "120",
            debitAmount: 120m,
            creditAmount: 0m,
            entry.ErpCustomer));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 2,
            accountCode: "600",
            debitAmount: 0m,
            creditAmount: 100m));
        entry.Lines.Add(CreateAccountingLine(
            entry,
            sequenceNumber: 3,
            accountCode: "391",
            debitAmount: 0m,
            creditAmount: 20m));
        context.ErpAccountingEntries.Add(entry);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persistedEntry = await context.ErpAccountingEntries
            .Include(item => item.Lines)
            .SingleAsync(item => item.Id == entry.Id);
        var lines = persistedEntry.Lines
            .OrderBy(line => line.SequenceNumber)
            .ToArray();

        Assert.Equal(["120", "600", "391"], lines.Select(line => line.AccountCode));
        Assert.Equal(
            [120m, 0m, 0m],
            lines.Select(line => line.DebitAmount));
        Assert.Equal(
            [0m, 100m, 20m],
            lines.Select(line => line.CreditAmount));
        Assert.Equal(persistedEntry.ErpCustomerId, lines[0].ErpCustomerId);
        Assert.Null(lines[1].ErpCustomerId);
        Assert.Null(lines[2].ErpCustomerId);
        Assert.Equal(
            persistedEntry.TotalDebit,
            lines.Sum(line => line.DebitAmount));
        Assert.Equal(
            persistedEntry.TotalCredit,
            lines.Sum(line => line.CreditAmount));
    }

    [Fact]
    public async Task Idempotency_key_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateIdempotencyRecord();
        var second = CreateIdempotencyRecord();
        second.IdempotencyKey = first.IdempotencyKey;
        context.ErpIdempotencyRecords.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Stored_response_body_must_be_valid_json()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var record = CreateIdempotencyRecord();
        record.ResponseBody = "not-json";
        context.ErpIdempotencyRecords.Add(record);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Business_write_and_idempotency_record_commit_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var order = CreateOrder(CreateCustomer());
        order.Address = CreateAddress(order);
        order.Lines.Add(CreateOrderLine(order));
        var record = CreateIdempotencyRecord();
        record.OperationType = "CreateOrder";
        record.ResourceType = "ErpOrder";
        record.ResourceId = order.Id;

        await using (var transaction =
            await context.Database.BeginTransactionAsync())
        {
            context.ErpOrders.Add(order);
            context.ErpIdempotencyRecords.Add(record);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        context.ChangeTracker.Clear();

        Assert.True(await context.ErpOrders.AnyAsync(item => item.Id == order.Id));
        Assert.True(await context.ErpOrderAddresses.AnyAsync(
            item => item.ErpOrderId == order.Id));
        Assert.True(await context.ErpOrderLines.AnyAsync(
            item => item.ErpOrderId == order.Id));
        Assert.True(await context.ErpIdempotencyRecords.AnyAsync(
            item => item.ResourceId == order.Id));
    }

    [Fact]
    public async Task No_cross_database_foreign_keys_exist()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var actualForeignKeys = new HashSet<string>(StringComparer.Ordinal);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                OBJECT_SCHEMA_NAME(fk.[parent_object_id]),
                OBJECT_NAME(fk.[parent_object_id]),
                OBJECT_SCHEMA_NAME(fk.[referenced_object_id]),
                OBJECT_NAME(fk.[referenced_object_id])
            FROM sys.foreign_keys AS fk
            ORDER BY fk.[name];
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actualForeignKeys.Add(
                $"{reader.GetString(0)}.{reader.GetString(1)}" +
                $"->{reader.GetString(2)}.{reader.GetString(3)}");
        }

        var expectedForeignKeys = new HashSet<string>(
            [
                "dbo.ErpAccountingEntries->dbo.ErpCustomers",
                "dbo.ErpAccountingEntries->dbo.ErpOrders",
                "dbo.ErpAccountingEntryLines->dbo.ErpAccountingEntries",
                "dbo.ErpAccountingEntryLines->dbo.ErpCustomers",
                "dbo.ErpOrderAddresses->dbo.ErpOrders",
                "dbo.ErpOrderLines->dbo.ErpOrders",
                "dbo.ErpOrders->dbo.ErpCustomers",
                "dbo.ErpStockMovements->dbo.ErpOrders"
            ],
            StringComparer.Ordinal);

        Assert.True(
            actualForeignKeys.SetEquals(expectedForeignKeys),
            $"Unexpected foreign key set: {string.Join(", ", actualForeignKeys.Order())}");
    }

    private static async Task<HashSet<string>> ReadNameSetAsync(
        string connectionString,
        string query)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static ErpCustomer CreateCustomer()
    {
        return new ErpCustomer
        {
            Id = Guid.NewGuid(),
            ErpCustomerCode = $"CARI-{Guid.NewGuid():N}",
            ExternalCustomerId = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"{Guid.NewGuid():N}@example.test",
            PhoneNumber = "+900000000000",
            AddressLine1 = "Mock ERP Street 1",
            District = "Test District",
            City = "Test City",
            CountryCode = "TR",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static ErpOrder CreateOrder(ErpCustomer customer)
    {
        return new ErpOrder
        {
            Id = Guid.NewGuid(),
            ErpOrderNumber = $"ERP-{Guid.NewGuid():N}"[..36],
            ExternalOrderId = Guid.NewGuid(),
            MarketOrderNumber = $"OM-{Guid.NewGuid():N}"[..32],
            ErpCustomerId = customer.Id,
            OrderPlacedAtUtc = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.CardSimulation,
            Subtotal = 100m,
            VatTotal = 20m,
            GrandTotal = 120m,
            Currency = "TRY",
            CreatedAtUtc = DateTime.UtcNow,
            ErpCustomer = customer
        };
    }

    private static ErpOrderAddress CreateAddress(ErpOrder order)
    {
        var address = CreateAddress(order.Id);
        address.ErpOrder = order;
        return address;
    }

    private static ErpOrderAddress CreateAddress(Guid orderId)
    {
        return new ErpOrderAddress
        {
            ErpOrderId = orderId,
            RecipientName = "Ada Lovelace",
            PhoneNumber = "+900000000000",
            AddressLine1 = "Delivery Street 1",
            District = "Test District",
            City = "Test City",
            CountryCode = "TR"
        };
    }

    private static ErpOrderLine CreateOrderLine(ErpOrder order)
    {
        return new ErpOrderLine
        {
            Id = Guid.NewGuid(),
            ErpOrderId = order.Id,
            ExternalProductId = Guid.NewGuid(),
            Sku = $"SKU-{Guid.NewGuid():N}",
            ProductName = "Mock ERP product snapshot",
            Quantity = 2,
            UnitPrice = 50m,
            VatRate = 20m,
            NetLineAmount = 100m,
            VatAmount = 20m,
            LineTotal = 120m,
            ErpOrder = order
        };
    }

    private static ErpStock CreateStock()
    {
        return new ErpStock
        {
            Id = Guid.NewGuid(),
            ExternalProductId = Guid.NewGuid(),
            Sku = $"SKU-{Guid.NewGuid():N}",
            ProductName = "Mock ERP stock card",
            UnitType = UnitType.Piece,
            NetContent = 1m,
            Quantity = 100,
            ReorderLevel = 10,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static ErpStockMovement CreateStockMovement(ErpOrder order)
    {
        return new ErpStockMovement
        {
            Id = Guid.NewGuid(),
            ErpOrderId = order.Id,
            ExternalOrderId = order.ExternalOrderId,
            ExternalProductId = Guid.NewGuid(),
            Sku = $"SKU-{Guid.NewGuid():N}",
            QuantityChange = -2,
            PreviousQuantity = 100,
            NewQuantity = 98,
            CreatedAtUtc = DateTime.UtcNow,
            ErpOrder = order
        };
    }

    private static ErpAccountingEntry CreateAccountingEntry(ErpOrder order)
    {
        var entry = CreateAccountingEntry(
            order.Id,
            order.ExternalOrderId,
            order.ErpCustomerId);
        entry.ErpOrder = order;
        entry.ErpCustomer = order.ErpCustomer;
        return entry;
    }

    private static ErpAccountingEntry CreateAccountingEntry(
        Guid orderId,
        Guid externalOrderId,
        Guid customerId)
    {
        return new ErpAccountingEntry
        {
            Id = Guid.NewGuid(),
            ErpVoucherNumber = $"FIS-{Guid.NewGuid():N}"[..36],
            VoucherType = AccountingVoucherType.SalesInvoice,
            ErpOrderId = orderId,
            ExternalOrderId = externalOrderId,
            ErpCustomerId = customerId,
            PaymentMethod = PaymentMethod.CardSimulation,
            EntryDateUtc = DateTime.UtcNow,
            TotalDebit = 120m,
            TotalCredit = 120m,
            Currency = "TRY",
            Description = "Project sales voucher",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static ErpAccountingEntryLine CreateAccountingLine(
        ErpAccountingEntry entry,
        byte sequenceNumber,
        string accountCode,
        decimal debitAmount,
        decimal creditAmount,
        ErpCustomer? customer = null)
    {
        return new ErpAccountingEntryLine
        {
            Id = Guid.NewGuid(),
            AccountingEntryId = entry.Id,
            SequenceNumber = sequenceNumber,
            AccountCode = accountCode,
            AccountName = accountCode switch
            {
                "120" => "Customers/Receivables",
                "600" => "Domestic Sales",
                _ => "VAT Payable"
            },
            ErpCustomerId = customer?.Id,
            DebitAmount = debitAmount,
            CreditAmount = creditAmount,
            Description = "Project accounting line",
            CreatedAtUtc = DateTime.UtcNow,
            AccountingEntry = entry,
            ErpCustomer = customer
        };
    }

    private static ErpIdempotencyRecord CreateIdempotencyRecord()
    {
        return new ErpIdempotencyRecord
        {
            IdempotencyKey = $"order:{Guid.NewGuid()}",
            OperationType = "CreateOrder",
            RequestHash = new string('A', 64),
            ResponseStatusCode = 201,
            ResponseBody = """{"status":"created"}""",
            ResourceType = "ErpOrder",
            ResourceId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            LastAccessedAtUtc = DateTime.UtcNow
        };
    }
}
