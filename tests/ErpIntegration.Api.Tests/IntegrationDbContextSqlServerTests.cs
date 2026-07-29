using ErpIntegration.Api.Domain.Entities;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ErpIntegration.Api.Tests;

[Collection(IntegrationSqlServerCollection.CollectionName)]
public sealed class IntegrationDbContextSqlServerTests(
    IntegrationSqlServerFixture fixture)
{
    private static readonly string[] ExpectedTables =
    [
        "ErpCustomerLinks",
        "IntegrationAttempts",
        "IntegrationBatches",
        "IntegrationOrderLines",
        "IntegrationOrderSnapshots",
        "IntegrationSteps",
        "ProcessedEvents"
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "IX_IntegrationAttempts_CorrelationId",
        "IX_IntegrationAttempts_StepId_StartedAtUtc",
        "IX_IntegrationBatches_CustomerId_CreatedAtUtc",
        "IX_IntegrationBatches_Status_CreatedAtUtc",
        "IX_IntegrationSteps_Status_NextAttemptAtUtc_SequenceNumber",
        "IX_ProcessedEvents_CorrelationId",
        "UX_ErpCustomerLinks_CustomerId",
        "UX_ErpCustomerLinks_ErpCustomerCode",
        "UX_IntegrationAttempts_StepId_AttemptNumber",
        "UX_IntegrationBatches_CorrelationId",
        "UX_IntegrationBatches_EventId",
        "UX_IntegrationBatches_MarketOrderId",
        "UX_IntegrationBatches_OrderNumber",
        "UX_IntegrationOrderLines_BatchId_ProductId",
        "UX_IntegrationSteps_BatchId_SequenceNumber",
        "UX_IntegrationSteps_BatchId_StepType",
        "UX_IntegrationSteps_IdempotencyKey"
    ];

    private static readonly string[] ExpectedCheckConstraints =
    [
        "CK_IntegrationAttempts_AttemptNumber_Positive",
        "CK_IntegrationAttempts_Duration_NonNegative",
        "CK_IntegrationAttempts_RequestPayloadMasked_IsJson",
        "CK_IntegrationAttempts_ResponsePayloadMasked_IsJson",
        "CK_IntegrationOrderLines_Amounts_NonNegative",
        "CK_IntegrationOrderLines_LineTotal",
        "CK_IntegrationOrderLines_Quantity_Positive",
        "CK_IntegrationOrderLines_VatRate_Range",
        "CK_IntegrationOrderSnapshots_GrandTotal",
        "CK_IntegrationOrderSnapshots_Totals_NonNegative",
        "CK_IntegrationSteps_Attempts_Range",
        "CK_IntegrationSteps_Sequence_Range"
    ];

    [Fact]
    public async Task Empty_database_can_apply_all_integration_migrations()
    {
        await using var database = await fixture.CreateDatabaseAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task All_seven_application_tables_exist()
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

    [Fact]
    public async Task Event_id_uniqueness_is_enforced()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var eventId = Guid.NewGuid();
        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Market_order_id_uniqueness_is_enforced()
    {
        await AssertDuplicateBatchesFailAsync(
            static (first, second) => second.MarketOrderId = first.MarketOrderId);
    }

    [Fact]
    public async Task Order_number_uniqueness_is_enforced()
    {
        await AssertDuplicateBatchesFailAsync(
            static (first, second) => second.OrderNumber = first.OrderNumber);
    }

    [Fact]
    public async Task Batch_correlation_id_uniqueness_is_enforced()
    {
        await AssertDuplicateBatchesFailAsync(
            static (first, second) => second.CorrelationId = first.CorrelationId);
    }

    [Theory]
    [InlineData(-0.01, 1.00, 0.99)]
    [InlineData(1.00, -0.01, 0.99)]
    [InlineData(0.00, 0.00, -0.01)]
    public async Task Snapshot_totals_cannot_be_negative(
        double subtotal,
        double vatTotal,
        double grandTotal)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var snapshot = CreateSnapshot(CreateBatch());
        snapshot.Subtotal = (decimal)subtotal;
        snapshot.VatTotal = (decimal)vatTotal;
        snapshot.GrandTotal = (decimal)grandTotal;
        context.IntegrationOrderSnapshots.Add(snapshot);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Snapshot_grand_total_must_equal_subtotal_plus_vat_total()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var snapshot = CreateSnapshot(CreateBatch());
        snapshot.GrandTotal += 0.01m;
        context.IntegrationOrderSnapshots.Add(snapshot);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_method_persists_using_documented_numeric_value()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var snapshot = CreateSnapshot(CreateBatch());
        snapshot.PaymentMethod = PaymentMethod.TransferSimulation;
        context.IntegrationOrderSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [PaymentMethod]
            FROM [dbo].[IntegrationOrderSnapshots]
            WHERE [BatchId] = @batchId;
            """;
        command.Parameters.AddWithValue("@batchId", snapshot.BatchId);

        var storedValue = Convert.ToByte(await command.ExecuteScalarAsync());

        Assert.Equal((byte)3, storedValue);
    }

    [Fact]
    public async Task Integration_line_quantity_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var line = CreateLine(CreateBatch());
        line.Quantity = 0;
        context.IntegrationOrderLines.Add(line);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public async Task Integration_line_vat_must_be_within_documented_range(
        double vatRate)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var line = CreateLine(CreateBatch());
        line.VatRate = (decimal)vatRate;
        context.IntegrationOrderLines.Add(line);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Integration_line_total_must_be_internally_consistent()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var line = CreateLine(CreateBatch());
        line.LineTotal += 0.01m;
        context.IntegrationOrderLines.Add(line);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_batch_and_product_id_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var batch = CreateBatch();
        var first = CreateLine(batch);
        var second = CreateLine(batch);
        second.ProductId = first.ProductId;
        context.IntegrationOrderLines.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Batch_and_step_type_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var batch = CreateBatch();
        var first = CreateStep(
            batch,
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var second = CreateStep(
            batch,
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 2);
        context.IntegrationSteps.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Batch_and_sequence_number_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var batch = CreateBatch();
        var first = CreateStep(
            batch,
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var second = CreateStep(
            batch,
            IntegrationStepType.CreateOrder,
            sequenceNumber: 1);
        context.IntegrationSteps.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Sequence_number_is_restricted_to_one_through_four(
        byte sequenceNumber)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        context.IntegrationSteps.Add(
            CreateStep(
                CreateBatch(),
                IntegrationStepType.EnsureCustomer,
                sequenceNumber));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Idempotency_key_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var second = CreateStep(
            CreateBatch(),
            IntegrationStepType.CreateOrder,
            sequenceNumber: 2);
        second.IdempotencyKey = first.IdempotencyKey;
        context.IntegrationSteps.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, -1)]
    [InlineData(6, 5)]
    public async Task Attempt_count_and_max_attempts_checks_are_enforced(
        int attemptCount,
        int maxAttempts)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        step.AttemptCount = attemptCount;
        step.MaxAttempts = maxAttempts;
        context.IntegrationSteps.Add(step);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Step_and_attempt_number_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        context.IntegrationAttempts.AddRange(
            CreateAttempt(step, attemptNumber: 1),
            CreateAttempt(step, attemptNumber: 1));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Attempt_number_must_be_positive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        context.IntegrationAttempts.Add(CreateAttempt(step, attemptNumber: 0));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Attempt_duration_cannot_be_negative()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var attempt = CreateAttempt(step, attemptNumber: 1);
        attempt.DurationMs = -1;
        context.IntegrationAttempts.Add(attempt);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Masked_request_payload_must_be_valid_json_when_present()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var attempt = CreateAttempt(step, attemptNumber: 1);
        attempt.RequestPayloadMasked = "not-json";
        context.IntegrationAttempts.Add(attempt);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Masked_response_payload_must_be_valid_json_when_present()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var step = CreateStep(
            CreateBatch(),
            IntegrationStepType.EnsureCustomer,
            sequenceNumber: 1);
        var attempt = CreateAttempt(step, attemptNumber: 1);
        attempt.ResponsePayloadMasked = "not-json";
        context.IntegrationAttempts.Add(attempt);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Erp_customer_link_customer_id_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateCustomerLink();
        var second = CreateCustomerLink();
        second.CustomerId = first.CustomerId;
        context.ErpCustomerLinks.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Erp_customer_code_is_unique()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateCustomerLink();
        var second = CreateCustomerLink();
        second.ErpCustomerCode = first.ErpCustomerCode;
        context.ErpCustomerLinks.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Row_version_concurrency_works()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        Guid linkId;
        byte[] initialRowVersion;

        await using (var seedContext = database.CreateContext())
        {
            var link = CreateCustomerLink();
            linkId = link.Id;
            seedContext.ErpCustomerLinks.Add(link);
            await seedContext.SaveChangesAsync();
            initialRowVersion = link.RowVersion.ToArray();
        }

        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstLink = await firstContext.ErpCustomerLinks.SingleAsync(
            link => link.Id == linkId);
        var staleLink = await staleContext.ErpCustomerLinks.SingleAsync(
            link => link.Id == linkId);

        firstLink.LastVerifiedAtUtc = DateTime.UtcNow;
        await firstContext.SaveChangesAsync();

        Assert.NotEqual(initialRowVersion, firstLink.RowVersion);

        staleLink.LastVerifiedAtUtc = DateTime.UtcNow.AddMinutes(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
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
                "dbo.IntegrationAttempts->dbo.IntegrationSteps",
                "dbo.IntegrationBatches->dbo.ProcessedEvents",
                "dbo.IntegrationOrderLines->dbo.IntegrationBatches",
                "dbo.IntegrationOrderSnapshots->dbo.IntegrationBatches",
                "dbo.IntegrationSteps->dbo.IntegrationBatches"
            ],
            StringComparer.Ordinal);

        Assert.True(
            actualForeignKeys.SetEquals(expectedForeignKeys),
            $"Unexpected foreign key set: {string.Join(", ", actualForeignKeys.Order())}");
    }

    [Fact]
    public async Task Complete_integration_aggregate_is_persisted_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var batch = CreateBatch();
        var orderId = batch.MarketOrderId;
        batch.OrderSnapshot = CreateSnapshot(batch);
        batch.OrderLines.Add(CreateLine(batch));

        var expectedSteps = new[]
        {
            (IntegrationStepType.EnsureCustomer, (byte)1, $"customer:{orderId}"),
            (IntegrationStepType.CreateOrder, (byte)2, $"order:{orderId}"),
            (IntegrationStepType.CreateStockMovement, (byte)3, $"stock:{orderId}"),
            (IntegrationStepType.CreateAccountingEntry, (byte)4, $"accounting:{orderId}")
        };

        foreach (var (stepType, sequenceNumber, idempotencyKey) in expectedSteps)
        {
            var step = CreateStep(batch, stepType, sequenceNumber);
            step.IdempotencyKey = idempotencyKey;
            batch.Steps.Add(step);
        }

        context.IntegrationBatches.Add(batch);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persistedBatch = await context.IntegrationBatches
            .Include(item => item.ProcessedEvent)
            .Include(item => item.OrderSnapshot)
            .Include(item => item.OrderLines)
            .Include(item => item.Steps)
            .SingleAsync(item => item.Id == batch.Id);
        var persistedSteps = persistedBatch.Steps
            .OrderBy(step => step.SequenceNumber)
            .ToArray();

        Assert.NotNull(persistedBatch.ProcessedEvent);
        Assert.NotNull(persistedBatch.OrderSnapshot);
        Assert.Single(persistedBatch.OrderLines);
        Assert.Equal(4, persistedSteps.Length);
        Assert.Equal([1, 2, 3, 4], persistedSteps.Select(step => (int)step.SequenceNumber));
        Assert.Equal(
            expectedSteps.Select(step => step.Item1),
            persistedSteps.Select(step => step.StepType));
        Assert.Equal(
            expectedSteps.Select(step => step.Item3),
            persistedSteps.Select(step => step.IdempotencyKey));
    }

    private async Task AssertDuplicateBatchesFailAsync(
        Action<IntegrationBatch, IntegrationBatch> duplicateValue)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var first = CreateBatch();
        var second = CreateBatch();
        duplicateValue(first, second);
        context.IntegrationBatches.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
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

    private static ProcessedEvent CreateProcessedEvent(Guid? eventId = null)
    {
        return new ProcessedEvent
        {
            EventId = eventId ?? Guid.NewGuid(),
            EventType = "OrderReadyForErpV1",
            PayloadHash = new string('A', 64),
            CorrelationId = Guid.NewGuid(),
            ReceivedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = DateTime.UtcNow
        };
    }

    private static IntegrationBatch CreateBatch()
    {
        var processedEvent = CreateProcessedEvent();
        var batch = new IntegrationBatch
        {
            Id = Guid.NewGuid(),
            EventId = processedEvent.EventId,
            MarketOrderId = Guid.NewGuid(),
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            CustomerId = Guid.NewGuid(),
            Status = IntegrationBatchStatus.Pending,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedEvent = processedEvent
        };
        processedEvent.Batch = batch;

        return batch;
    }

    private static IntegrationOrderSnapshot CreateSnapshot(
        IntegrationBatch batch)
    {
        return new IntegrationOrderSnapshot
        {
            BatchId = batch.Id,
            CustomerId = batch.CustomerId,
            FirstName = "Ada",
            LastName = "Lovelace",
            RecipientName = "Ada Lovelace",
            Email = "ada@example.test",
            PhoneNumber = "+900000000000",
            AddressLine1 = "Integration Street 1",
            District = "Test District",
            City = "Test City",
            CountryCode = "TR",
            OrderPlacedAtUtc = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.CardSimulation,
            Subtotal = 100.00m,
            VatTotal = 20.00m,
            GrandTotal = 120.00m,
            Currency = "TRY",
            Batch = batch
        };
    }

    private static IntegrationOrderLine CreateLine(IntegrationBatch batch)
    {
        return new IntegrationOrderLine
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ProductId = Guid.NewGuid(),
            Sku = $"SKU-{Guid.NewGuid():N}",
            ProductName = "Integration product snapshot",
            Quantity = 2,
            UnitPrice = 50.00m,
            VatRate = 20.00m,
            NetLineAmount = 100.00m,
            VatAmount = 20.00m,
            LineTotal = 120.00m,
            Batch = batch
        };
    }

    private static IntegrationStep CreateStep(
        IntegrationBatch batch,
        IntegrationStepType stepType,
        byte sequenceNumber)
    {
        return new IntegrationStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            StepType = stepType,
            SequenceNumber = sequenceNumber,
            Status = IntegrationStepStatus.Pending,
            IdempotencyKey = $"{stepType}:{Guid.NewGuid()}",
            Batch = batch
        };
    }

    private static IntegrationAttempt CreateAttempt(
        IntegrationStep step,
        int attemptNumber)
    {
        return new IntegrationAttempt
        {
            StepId = step.Id,
            AttemptNumber = attemptNumber,
            StartedAtUtc = DateTime.UtcNow,
            ResultType = IntegrationResultType.Succeeded,
            RequestHash = new string('B', 64),
            CorrelationId = Guid.NewGuid(),
            Step = step
        };
    }

    private static ErpCustomerLink CreateCustomerLink()
    {
        return new ErpCustomerLink
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ErpCustomerCode = $"ERP-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
