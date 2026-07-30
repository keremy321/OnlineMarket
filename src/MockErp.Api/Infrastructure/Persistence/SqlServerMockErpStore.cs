using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Application.Models;
using MockErp.Api.Contracts;
using MockErp.Api.Domain.Entities;
using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Infrastructure.Persistence;

public sealed class SqlServerMockErpStore(
    MockErpDbContext dbContext,
    ILogger<SqlServerMockErpStore> logger)
    : IMockErpStore
{
    private const string EnsureCustomerOperation = "EnsureCustomer";
    private const string CreateOrderOperation = "CreateOrder";
    private const string CreateStockMovementOperation = "CreateStockMovement";
    private const string CreateAccountingEntryOperation = "CreateAccountingEntry";

    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<StoreOperationResult> EnsureCustomerAsync(
        EnsureCustomerRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            idempotencyKey,
            EnsureCustomerOperation,
            requestHash,
            $"Customer:{request.ExternalCustomerId:D}",
            nowUtc,
            async () =>
            {
                var customer = await dbContext.ErpCustomers
                    .SingleOrDefaultAsync(
                        item => item.ExternalCustomerId
                            == request.ExternalCustomerId,
                        cancellationToken);
                var created = customer is null;
                if (customer is null)
                {
                    customer = new ErpCustomer
                    {
                        Id = Guid.NewGuid(),
                        ErpCustomerCode =
                            $"CARI-{Guid.NewGuid():N}",
                        ExternalCustomerId = request.ExternalCustomerId,
                        CreatedAtUtc = nowUtc
                    };
                    dbContext.ErpCustomers.Add(customer);
                }

                customer.FirstName = request.FirstName!;
                customer.LastName = request.LastName!;
                customer.Email = request.Email!;
                customer.PhoneNumber = request.PhoneNumber!;
                customer.AddressLine1 = request.AddressLine1!;
                customer.AddressLine2 = request.AddressLine2;
                customer.District = request.District!;
                customer.City = request.City!;
                customer.PostalCode = request.PostalCode;
                customer.CountryCode = request.CountryCode!;
                customer.IsActive = true;
                customer.UpdatedAtUtc = nowUtc;

                return ResourceWorkResult.Success(
                    created
                        ? StatusCodes.Status201Created
                        : StatusCodes.Status200OK,
                    new EnsureCustomerResponse(
                        customer.Id,
                        customer.ErpCustomerCode,
                        customer.ExternalCustomerId,
                        created),
                    "ErpCustomer",
                    customer.Id);
            },
            cancellationToken);
    }

    public Task<StoreOperationResult> CreateOrderAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            idempotencyKey,
            CreateOrderOperation,
            requestHash,
            $"Order:{request.ExternalOrderId:D}",
            nowUtc,
            async () =>
            {
                var customer = await dbContext.ErpCustomers
                    .SingleOrDefaultAsync(
                        item => item.ErpCustomerCode
                            == request.ErpCustomerCode,
                        cancellationToken);
                if (customer is null)
                {
                    return ResourceWorkResult.Failure(
                        NotFound(
                            "MockErp.CustomerNotFound",
                            "The ERP customer does not exist."));
                }

                var orderConflict = await dbContext.ErpOrders
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.ExternalOrderId == request.ExternalOrderId
                            || item.MarketOrderNumber
                                == request.MarketOrderNumber,
                        cancellationToken);
                if (orderConflict)
                {
                    return ResourceWorkResult.Failure(ResourceConflict(
                        "An ERP order already exists for the supplied order identity."));
                }

                var order = new ErpOrder
                {
                    Id = Guid.NewGuid(),
                    ErpOrderNumber = $"ERP-{Guid.NewGuid():N}",
                    ExternalOrderId = request.ExternalOrderId,
                    MarketOrderNumber = request.MarketOrderNumber!,
                    ErpCustomerId = customer.Id,
                    OrderPlacedAtUtc = request.OrderPlacedAtUtc,
                    PaymentMethod = request.PaymentMethod,
                    Subtotal = request.Subtotal,
                    VatTotal = request.VatTotal,
                    GrandTotal = request.GrandTotal,
                    Currency = request.Currency!,
                    CreatedAtUtc = nowUtc,
                    ErpCustomer = customer
                };
                var address = request.Address!;
                order.Address = new ErpOrderAddress
                {
                    ErpOrderId = order.Id,
                    RecipientName = address.RecipientName!,
                    PhoneNumber = address.PhoneNumber!,
                    AddressLine1 = address.AddressLine1!,
                    AddressLine2 = address.AddressLine2,
                    District = address.District!,
                    City = address.City!,
                    PostalCode = address.PostalCode,
                    CountryCode = address.CountryCode!,
                    ErpOrder = order
                };
                foreach (var lineValue in request.Lines!)
                {
                    var line = lineValue!;
                    order.Lines.Add(new ErpOrderLine
                    {
                        Id = Guid.NewGuid(),
                        ErpOrderId = order.Id,
                        ExternalProductId = line.ExternalProductId,
                        Sku = line.Sku!,
                        ProductName = line.ProductName!,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        VatRate = line.VatRate,
                        NetLineAmount = line.NetLineAmount,
                        VatAmount = line.VatAmount,
                        LineTotal = line.LineTotal,
                        ErpOrder = order
                    });
                }

                dbContext.ErpOrders.Add(order);
                return ResourceWorkResult.Success(
                    StatusCodes.Status201Created,
                    new CreateOrderResponse(
                        order.Id,
                        order.ErpOrderNumber,
                        order.ExternalOrderId),
                    "ErpOrder",
                    order.Id);
            },
            cancellationToken);
    }

    public Task<StoreOperationResult> CreateStockMovementsAsync(
        CreateStockMovementsRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            idempotencyKey,
            CreateStockMovementOperation,
            requestHash,
            $"StockOrder:{request.ExternalOrderId:D}",
            nowUtc,
            async () =>
            {
                var order = await dbContext.ErpOrders
                    .Include(item => item.Lines)
                    .SingleOrDefaultAsync(
                        item => item.ExternalOrderId
                            == request.ExternalOrderId,
                        cancellationToken);
                if (order is null)
                {
                    return ResourceWorkResult.Failure(
                        NotFound(
                            "MockErp.OrderNotFound",
                            "The ERP order does not exist."));
                }

                foreach (var lineValue in request.Lines!)
                {
                    var line = lineValue!;
                    var orderLine = order.Lines.SingleOrDefault(
                        item => item.ExternalProductId
                            == line.ExternalProductId);
                    if (orderLine is null
                        || line.QuantityChange != -orderLine.Quantity)
                    {
                        return ResourceWorkResult.Failure(ResourceConflict(
                            "Stock movements must match the ERP order product quantities."));
                    }
                }

                var productIds = request.Lines!
                    .Select(line => line!.ExternalProductId)
                    .ToArray();
                if (await dbContext.ErpStockMovements
                    .AsNoTracking()
                    .AnyAsync(
                        movement => movement.ExternalOrderId
                                == request.ExternalOrderId
                            && productIds.Contains(
                                movement.ExternalProductId),
                        cancellationToken))
                {
                    return ResourceWorkResult.Failure(ResourceConflict(
                        "A stock movement already exists for this order and product."));
                }

                var responses = new List<StockMovementResponse>();
                foreach (var lineValue in request.Lines!
                    .OrderBy(line => line!.ExternalProductId))
                {
                    var line = lineValue!;
                    var mutation = await TryDecreaseStockAsync(
                        line.ExternalProductId,
                        line.QuantityChange,
                        nowUtc,
                        cancellationToken);
                    if (mutation is null)
                    {
                        var stockExists = await dbContext.ErpStocks
                            .AsNoTracking()
                            .AnyAsync(
                                stock => stock.ExternalProductId
                                    == line.ExternalProductId,
                                cancellationToken);
                        return ResourceWorkResult.Failure(
                            stockExists
                                ? Conflict(
                                    "MockErp.InsufficientStock",
                                    "ERP stock is insufficient for the requested sale.")
                                : NotFound(
                                    "MockErp.StockNotFound",
                                    "The ERP stock card does not exist."));
                    }

                    var movement = new ErpStockMovement
                    {
                        Id = Guid.NewGuid(),
                        ErpOrderId = order.Id,
                        ExternalOrderId = order.ExternalOrderId,
                        ExternalProductId = line.ExternalProductId,
                        Sku = mutation.Sku,
                        QuantityChange = line.QuantityChange,
                        PreviousQuantity = mutation.PreviousQuantity,
                        NewQuantity = mutation.NewQuantity,
                        CreatedAtUtc = nowUtc,
                        ErpOrder = order
                    };
                    dbContext.ErpStockMovements.Add(movement);
                    responses.Add(new StockMovementResponse(
                        movement.Id,
                        movement.ExternalProductId,
                        movement.Sku,
                        movement.QuantityChange,
                        movement.PreviousQuantity,
                        movement.NewQuantity));
                }

                return ResourceWorkResult.Success(
                    StatusCodes.Status201Created,
                    new CreateStockMovementsResponse(
                        order.Id,
                        order.ExternalOrderId,
                        responses),
                    "ErpOrder",
                    order.Id);
            },
            cancellationToken);
    }

    public Task<StoreOperationResult> CreateAccountingEntryAsync(
        CreateAccountingEntryRequest request,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            idempotencyKey,
            CreateAccountingEntryOperation,
            requestHash,
            $"Accounting:{request.ExternalOrderId:D}",
            nowUtc,
            async () =>
            {
                var order = await dbContext.ErpOrders
                    .Include(item => item.ErpCustomer)
                    .SingleOrDefaultAsync(
                        item => item.ExternalOrderId
                            == request.ExternalOrderId,
                        cancellationToken);
                if (order is null)
                {
                    return ResourceWorkResult.Failure(
                        NotFound(
                            "MockErp.OrderNotFound",
                            "The ERP order does not exist."));
                }

                if (order.ErpCustomer is null)
                {
                    return ResourceWorkResult.Failure(
                        NotFound(
                            "MockErp.CustomerNotFound",
                            "The ERP customer does not exist."));
                }

                if (await dbContext.ErpAccountingEntries
                    .AsNoTracking()
                    .AnyAsync(
                        entry => entry.ErpOrderId == order.Id
                            || entry.ExternalOrderId
                                == order.ExternalOrderId,
                        cancellationToken))
                {
                    return ResourceWorkResult.Failure(ResourceConflict(
                        "An accounting entry already exists for this order."));
                }

                if (order.Subtotal <= 0
                    || order.GrandTotal <= 0)
                {
                    return ResourceWorkResult.Failure(ResourceConflict(
                        "Subtotal and GrandTotal must be positive for the required accounting lines."));
                }

                var entry = new ErpAccountingEntry
                {
                    Id = Guid.NewGuid(),
                    ErpVoucherNumber = $"FIS-{Guid.NewGuid():N}",
                    VoucherType = AccountingVoucherType.SalesInvoice,
                    ErpOrderId = order.Id,
                    ExternalOrderId = order.ExternalOrderId,
                    ErpCustomerId = order.ErpCustomerId,
                    PaymentMethod = order.PaymentMethod,
                    EntryDateUtc = request.EntryDateUtc,
                    TotalDebit = order.GrandTotal,
                    TotalCredit = order.Subtotal + order.VatTotal,
                    Currency = order.Currency,
                    Description = request.Description!,
                    CreatedAtUtc = nowUtc,
                    ErpOrder = order,
                    ErpCustomer = order.ErpCustomer
                };
                AddAccountingLine(
                    entry,
                    1,
                    "120",
                    "Alıcılar",
                    order.ErpCustomer,
                    order.GrandTotal,
                    0,
                    request.Description!,
                    nowUtc);
                AddAccountingLine(
                    entry,
                    2,
                    "600",
                    "Yurtiçi Satışlar",
                    null,
                    0,
                    order.Subtotal,
                    request.Description!,
                    nowUtc);
                if (order.VatTotal > 0)
                {
                    AddAccountingLine(
                        entry,
                        3,
                        "391",
                        "Hesaplanan KDV",
                        null,
                        0,
                        order.VatTotal,
                        request.Description!,
                        nowUtc);
                }

                dbContext.ErpAccountingEntries.Add(entry);

                return ResourceWorkResult.Success(
                    StatusCodes.Status201Created,
                    new CreateAccountingEntryResponse(
                        entry.Id,
                        entry.ErpVoucherNumber,
                        entry.ExternalOrderId),
                    "ErpAccountingEntry",
                    entry.Id);
            },
            cancellationToken);
    }

    public async Task<CustomerOrderHistoryResponse?> GetCustomerOrdersAsync(
        string erpCustomerCode,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.ErpCustomers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ErpCustomerCode == erpCustomerCode,
                cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var orders = await dbContext.ErpOrders
            .AsNoTracking()
            .Where(order => order.ErpCustomerId == customer.Id)
            .Include(order => order.Address)
            .Include(order => order.Lines)
            .Include(order => order.AccountingEntry)
            .OrderByDescending(order => order.OrderPlacedAtUtc)
            .ThenByDescending(order => order.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return new CustomerOrderHistoryResponse(
            customer.Id,
            customer.ErpCustomerCode,
            customer.ExternalCustomerId,
            orders.Select(MapHistoryOrder).ToArray());
    }

    private async Task<StoreOperationResult> ExecuteIdempotentAsync(
        string idempotencyKey,
        string operationType,
        string requestHash,
        string resourceLock,
        DateTime nowUtc,
        Func<Task<ResourceWorkResult>> work,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await AcquireLockAsync(
                $"Idempotency:{HashLockValue(idempotencyKey)}",
                cancellationToken);
            var existing = await dbContext.ErpIdempotencyRecords
                .SingleOrDefaultAsync(
                    record => record.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.OperationType != operationType
                    || !existing.RequestHash.Equals(
                        requestHash,
                        StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    return new StoreOperationResult(
                        null,
                        Conflict(
                            "Idempotency.PayloadConflict",
                            "The Idempotency-Key was already used with a different request."));
                }

                existing.LastAccessedAtUtc = nowUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new StoreOperationResult(
                    new StoredHttpResponse(
                        existing.ResponseStatusCode,
                        existing.ResponseBody),
                    null);
            }

            await AcquireLockAsync(
                $"Resource:{resourceLock}",
                cancellationToken);
            var result = await work();
            if (result.Error is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return new StoreOperationResult(null, result.Error);
            }

            var body = JsonSerializer.Serialize(
                result.Response,
                result.Response!.GetType(),
                ResponseJsonOptions);
            dbContext.ErpIdempotencyRecords.Add(new ErpIdempotencyRecord
            {
                IdempotencyKey = idempotencyKey,
                OperationType = operationType,
                RequestHash = requestHash,
                ResponseStatusCode = checked((short)result.StatusCode),
                ResponseBody = body,
                ResourceType = result.ResourceType,
                ResourceId = result.ResourceId,
                CreatedAtUtc = nowUtc,
                LastAccessedAtUtc = nowUtc
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StoreOperationResult(
                new StoredHttpResponse(result.StatusCode, body),
                null);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(
                exception,
                "A Mock ERP write encountered a concurrency conflict.");
            return new StoreOperationResult(
                null,
                InfrastructureFailure());
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.ErpIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null
                && existing.OperationType == operationType
                && existing.RequestHash.Equals(
                    requestHash,
                    StringComparison.Ordinal))
            {
                return new StoreOperationResult(
                    new StoredHttpResponse(
                        existing.ResponseStatusCode,
                        existing.ResponseBody),
                    null);
            }

            return new StoreOperationResult(
                null,
                existing is not null
                    ? Conflict(
                        "Idempotency.PayloadConflict",
                        "The Idempotency-Key was already used with a different request.")
                    : ResourceConflict(
                        "A resource with the supplied identity already exists."));
        }
        catch (SqlException exception)
            when (exception.Number is -2 or 1205 or 1222 or 51000)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(
                exception,
                "A transient SQL failure interrupted a Mock ERP operation.");
            return new StoreOperationResult(null, InfrastructureFailure());
        }
    }

    private async Task AcquireLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;
            IF @result < 0
                THROW 51000, 'Unable to acquire Mock ERP operation lock.', 1;
            """,
            cancellationToken);
    }

    private async Task<StockMutation?> TryDecreaseStockAsync(
        Guid externalProductId,
        int quantityChange,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!
            .GetDbTransaction();
        command.CommandText = """
            UPDATE [dbo].[ErpStocks] WITH (UPDLOCK, ROWLOCK)
            SET [Quantity] = [Quantity] + @quantityChange,
                [UpdatedAtUtc] = @updatedAtUtc
            OUTPUT deleted.[Quantity], inserted.[Quantity], inserted.[Sku]
            WHERE [ExternalProductId] = @externalProductId
              AND [Quantity] + @quantityChange >= 0;
            """;
        AddParameter(command, "@quantityChange", quantityChange);
        AddParameter(command, "@updatedAtUtc", nowUtc);
        AddParameter(command, "@externalProductId", externalProductId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StockMutation(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2));
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddAccountingLine(
        ErpAccountingEntry entry,
        byte sequenceNumber,
        string accountCode,
        string accountName,
        ErpCustomer? customer,
        decimal debit,
        decimal credit,
        string description,
        DateTime nowUtc)
    {
        entry.Lines.Add(new ErpAccountingEntryLine
        {
            Id = Guid.NewGuid(),
            AccountingEntryId = entry.Id,
            SequenceNumber = sequenceNumber,
            AccountCode = accountCode,
            AccountName = accountName,
            ErpCustomerId = customer?.Id,
            DebitAmount = debit,
            CreditAmount = credit,
            Description = description,
            CreatedAtUtc = nowUtc,
            AccountingEntry = entry,
            ErpCustomer = customer
        });
    }

    private static CustomerOrderResponse MapHistoryOrder(ErpOrder order)
    {
        var address = order.Address
            ?? throw new InvalidOperationException(
                "An ERP order is missing its immutable address snapshot.");
        return new CustomerOrderResponse(
            order.Id,
            order.ErpOrderNumber,
            order.ExternalOrderId,
            order.MarketOrderNumber,
            AsUtc(order.OrderPlacedAtUtc),
            order.PaymentMethod,
            order.Subtotal,
            order.VatTotal,
            order.GrandTotal,
            order.Currency,
            new CustomerOrderAddressResponse(
                address.RecipientName,
                address.PhoneNumber,
                address.AddressLine1,
                address.AddressLine2,
                address.District,
                address.City,
                address.PostalCode,
                address.CountryCode),
            order.Lines
                .OrderBy(line => line.Id)
                .Select(line => new CustomerOrderLineResponse(
                    line.ExternalProductId,
                    line.Sku,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.VatRate,
                    line.NetLineAmount,
                    line.VatAmount,
                    line.LineTotal))
                .ToArray(),
            order.AccountingEntry is null
                ? null
                : new CustomerOrderAccountingResponse(
                    order.AccountingEntry.Id,
                    order.AccountingEntry.ErpVoucherNumber,
                    order.AccountingEntry.VoucherType,
                    AsUtc(order.AccountingEntry.EntryDateUtc)));
    }

    private static DateTime AsUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static string HashLockValue(string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException
        {
            Number: 2601 or 2627
        };
    }

    private static ApplicationError NotFound(string code, string message)
    {
        return new ApplicationError(
            StatusCodes.Status404NotFound,
            code,
            message,
            false);
    }

    private static ApplicationError Conflict(string code, string message)
    {
        return new ApplicationError(
            StatusCodes.Status409Conflict,
            code,
            message,
            false);
    }

    private static ApplicationError ResourceConflict(string message)
    {
        return Conflict("MockErp.ResourceConflict", message);
    }

    private static ApplicationError InfrastructureFailure()
    {
        return new ApplicationError(
            StatusCodes.Status503ServiceUnavailable,
            "MockErp.InfrastructureFailure",
            "A transient Mock ERP infrastructure failure occurred.",
            true);
    }

    private sealed record StockMutation(
        int PreviousQuantity,
        int NewQuantity,
        string Sku);

    private sealed record ResourceWorkResult(
        int StatusCode,
        object? Response,
        string? ResourceType,
        Guid? ResourceId,
        ApplicationError? Error)
    {
        public static ResourceWorkResult Success(
            int statusCode,
            object response,
            string resourceType,
            Guid resourceId)
        {
            return new ResourceWorkResult(
                statusCode,
                response,
                resourceType,
                resourceId,
                null);
        }

        public static ResourceWorkResult Failure(ApplicationError error)
        {
            return new ResourceWorkResult(0, null, null, null, error);
        }
    }
}
