using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public sealed class CheckoutService : ICheckoutService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IStockMutationService _stockMutationService;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        OnlineMarketDbContext dbContext,
        IStockMutationService stockMutationService,
        IOrderNumberGenerator orderNumberGenerator,
        ILogger<CheckoutService> logger)
    {
        _dbContext = dbContext;
        _stockMutationService = stockMutationService;
        _orderNumberGenerator = orderNumberGenerator;
        _logger = logger;
    }

    public async Task<CheckoutResultDto> ExecuteCheckoutAsync(
        Guid customerId,
        CheckoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Stable identifiers, allocated once so an execution-strategy retry after a
        // commit whose acknowledgement was lost re-observes the same logical order
        // instead of creating a duplicate.
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var recommendationEventId = Guid.NewGuid();
        var erpEventId = Guid.NewGuid();

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                // Entities tracked by a prior, rolled-back attempt must not resurface
                // on this attempt's queries or SaveChanges call.
                _dbContext.ChangeTracker.Clear();

                var alreadyCommittedOrder = await _dbContext.Orders
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == orderId)
                    .Select(candidate => new { candidate.Id, candidate.OrderNumber })
                    .FirstOrDefaultAsync(cancellationToken);

                if (alreadyCommittedOrder is not null)
                {
                    return new CheckoutResultDto(
                        true,
                        alreadyCommittedOrder.Id,
                        alreadyCommittedOrder.OrderNumber,
                        null);
                }

                await using var transaction = await _dbContext.Database
                    .BeginTransactionAsync(cancellationToken);

                try
                {
                    var customer = await _dbContext.Customers
                        .Include(candidate => candidate.User)
                        .FirstOrDefaultAsync(
                            candidate => candidate.Id == customerId && candidate.IsActive,
                            cancellationToken);

                    if (customer is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Müşteri hesabı bulunamadı.");
                    }

                    var cart = await _dbContext.Carts
                        .Include(candidate => candidate.Items)
                        .FirstOrDefaultAsync(
                            candidate =>
                                candidate.CustomerId == customerId
                                && candidate.Status == CartStatus.Active,
                            cancellationToken);

                    if (cart is null || cart.Items.Count == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Sepetiniz boş.");
                    }

                    var address = await _dbContext.CustomerAddresses
                        .FirstOrDefaultAsync(
                            candidate =>
                                candidate.Id == request.AddressId
                                && candidate.CustomerId == customerId
                                && candidate.IsActive,
                            cancellationToken);

                    if (address is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Geçerli bir teslimat adresi seçilmedi.");
                    }

                    var duplicateProductId = cart.Items
                        .GroupBy(item => item.ProductId)
                        .FirstOrDefault(group => group.Count() > 1);

                    if (duplicateProductId is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Sepet aynı ürün için birden fazla satır içeriyor.");
                    }

                    var productIds = cart.Items.Select(item => item.ProductId).ToArray();
                    var products = await _dbContext.Products
                        .AsNoTracking()
                        .Where(product => productIds.Contains(product.Id) && product.IsActive)
                        .ToDictionaryAsync(product => product.Id, cancellationToken);

                    if (products.Count != productIds.Length)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Sepetinizde satışta olmayan bir ürün bulunuyor.");
                    }

                    if (!request.SimulateSuccess)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Ödeme simülasyonu reddedildi.");
                    }

                    var placedAtUtc = UtcNowAtDatabasePrecision();
                    var orderNumber = await _orderNumberGenerator.NextAsync(cancellationToken);

                    decimal subtotal = 0;
                    decimal vatTotal = 0;
                    var orderItems = new List<OrderItem>(cart.Items.Count);
                    var recommendationItems = new List<RecommendationOrderItemV1>(cart.Items.Count);
                    var erpItems = new List<ErpOrderItemV1>(cart.Items.Count);

                    foreach (var cartItem in cart.Items)
                    {
                        var product = products[cartItem.ProductId];
                        var lineSubtotal = Math.Round(
                            product.Price * cartItem.Quantity,
                            2,
                            MidpointRounding.AwayFromZero);
                        var lineVat = Math.Round(
                            lineSubtotal * (product.VatRate / 100m),
                            2,
                            MidpointRounding.AwayFromZero);
                        var lineTotal = lineSubtotal + lineVat;

                        subtotal += lineSubtotal;
                        vatTotal += lineVat;

                        orderItems.Add(new OrderItem
                        {
                            Id = Guid.NewGuid(),
                            OrderId = orderId,
                            ProductId = product.Id,
                            SkuSnapshot = product.Sku,
                            ProductNameSnapshot = product.Name,
                            UnitPrice = product.Price,
                            VatRate = product.VatRate,
                            Quantity = cartItem.Quantity,
                            NetLineAmount = lineSubtotal,
                            VatAmount = lineVat,
                            LineTotal = lineTotal
                        });

                        recommendationItems.Add(new RecommendationOrderItemV1(
                            product.Id,
                            cartItem.Quantity));

                        erpItems.Add(new ErpOrderItemV1(
                            product.Id,
                            product.Sku,
                            product.Name,
                            cartItem.Quantity,
                            product.Price,
                            product.VatRate,
                            lineSubtotal,
                            lineVat,
                            lineTotal));
                    }

                    var stockMovements = new List<StockMovement>(cart.Items.Count);
                    foreach (var cartItem in cart.Items)
                    {
                        var stockResult = await _stockMutationService.TryDecreaseAsync(
                            cartItem.ProductId,
                            cartItem.Quantity,
                            placedAtUtc,
                            cancellationToken);

                        if (stockResult is null)
                        {
                            var availableQuantity = await _dbContext.Stocks
                                .AsNoTracking()
                                .Where(stock => stock.ProductId == cartItem.ProductId)
                                .Select(stock => (int?)stock.Quantity)
                                .SingleOrDefaultAsync(cancellationToken) ?? 0;

                            await transaction.RollbackAsync(cancellationToken);
                            return Failure(
                                $"'{products[cartItem.ProductId].Name}' ürünü için yetersiz stok. "
                                + $"Mevcut: {availableQuantity}, İstenen: {cartItem.Quantity}");
                        }

                        stockMovements.Add(new StockMovement
                        {
                            ProductId = cartItem.ProductId,
                            MovementType = StockMovementType.Sale,
                            QuantityChange = -cartItem.Quantity,
                            PreviousQuantity = stockResult.Value.PreviousQuantity,
                            NewQuantity = stockResult.Value.NewQuantity,
                            ReferenceType = StockReferenceType.Order,
                            ReferenceId = orderId,
                            Description = "Order Checkout",
                            CreatedAtUtc = placedAtUtc
                        });
                    }

                    var grandTotal = subtotal + vatTotal;
                    const string currency = "TRY";
                    const PaymentMethod paymentMethod = PaymentMethod.CardSimulation;

                    var order = new Order
                    {
                        Id = orderId,
                        OrderNumber = orderNumber,
                        CustomerId = customerId,
                        SourceCartId = cart.Id,
                        Status = OrderStatus.Confirmed,
                        Subtotal = subtotal,
                        VatTotal = vatTotal,
                        GrandTotal = grandTotal,
                        Currency = currency,
                        CorrelationId = correlationId,
                        PlacedAtUtc = placedAtUtc,
                        CreatedAtUtc = placedAtUtc,
                        AddressSnapshot = new OrderAddress
                        {
                            OrderId = orderId,
                            RecipientName = address.ContactName,
                            PhoneNumber = address.PhoneNumber,
                            AddressLine1 = address.AddressLine1,
                            AddressLine2 = address.AddressLine2,
                            District = address.District,
                            City = address.City,
                            PostalCode = address.PostalCode,
                            CountryCode = address.CountryCode
                        },
                        Payment = new Payment
                        {
                            Id = paymentId,
                            OrderId = orderId,
                            Amount = grandTotal,
                            Method = paymentMethod,
                            SimulationReference = $"SIM-PAY-{Guid.NewGuid():N}",
                            Status = PaymentStatus.Succeeded,
                            ProcessedAtUtc = placedAtUtc,
                            CreatedAtUtc = placedAtUtc
                        },
                        Items = orderItems
                    };

                    cart.Status = CartStatus.Converted;
                    cart.UpdatedAtUtc = placedAtUtc;

                    var recommendationEvent = new OrderConfirmedForRecommendationV1(
                        recommendationEventId,
                        placedAtUtc,
                        correlationId,
                        orderId,
                        orderNumber,
                        customerId,
                        recommendationItems);

                    var erpEvent = new OrderReadyForErpV1(
                        erpEventId,
                        placedAtUtc,
                        correlationId,
                        orderId,
                        orderNumber,
                        placedAtUtc,
                        paymentMethod,
                        new ErpOrderCustomerV1(
                            customerId,
                            customer.FirstName,
                            customer.LastName,
                            customer.User?.Email ?? string.Empty),
                        new ErpOrderAddressV1(
                            address.ContactName,
                            address.PhoneNumber,
                            address.AddressLine1,
                            address.AddressLine2,
                            address.District,
                            address.City,
                            address.PostalCode,
                            address.CountryCode),
                        new ErpOrderTotalsV1(
                            subtotal,
                            vatTotal,
                            grandTotal,
                            currency),
                        erpItems);

                    _dbContext.Orders.Add(order);
                    _dbContext.StockMovements.AddRange(stockMovements);
                    _dbContext.OutboxMessages.Add(
                        OutboxMessageFactory.Create(
                            recommendationEvent,
                            OutboxMessageFactory.RecommendationDestination,
                            "Order",
                            orderId));
                    _dbContext.OutboxMessages.Add(
                        OutboxMessageFactory.Create(
                            erpEvent,
                            OutboxMessageFactory.ErpIntegrationDestination,
                            "Order",
                            orderId));

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new CheckoutResultDto(true, orderId, orderNumber, null);
                }
                catch
                {
                    await TryRollbackAsync(transaction, cancellationToken);
                    throw;
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Checkout failed for customer {CustomerId}. All database changes were rolled back.",
                customerId);

            return Failure("Sipariş oluşturulurken bir hata oluştu.");
        }
    }

    private static async Task TryRollbackAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A commit outcome can be unknown to the client while already complete on SQL Server.
        }
        catch (DbException)
        {
            // Preserve the original transient exception so the execution strategy can classify it.
        }
    }

    private static CheckoutResultDto Failure(string message)
    {
        return new CheckoutResultDto(false, null, null, message);
    }

    private static DateTime UtcNowAtDatabasePrecision()
    {
        var now = DateTime.UtcNow;
        return new DateTime(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
