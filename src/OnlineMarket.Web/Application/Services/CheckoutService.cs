using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class CheckoutService : ICheckoutService
{
    private readonly OnlineMarketDbContext _dbContext;

    public CheckoutService(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CheckoutResultDto> ExecuteCheckoutAsync(Guid customerId, CheckoutRequestDto request)
    {
        // 1. Validate Customer & Selected Address
        var customer = await _dbContext.Customers
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive);

        if (customer == null)
        {
            return new CheckoutResultDto(false, null, null, "Müşteri hesabı bulunamadı.");
        }

        var address = await _dbContext.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == customerId && a.IsActive);

        if (address == null)
        {
            return new CheckoutResultDto(false, null, null, "Geçerli bir teslimat adresi seçilmedi.");
        }

        // 2. Begin SQL Transaction
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Fetch Active Cart
            var cart = await _dbContext.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active);

            if (cart == null || !cart.Items.Any())
            {
                return new CheckoutResultDto(false, null, null, "Sepetiniz boş.");
            }

            var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();

            // Reload products and stocks inside transaction
            var products = await _dbContext.Products
                .Include(p => p.Stock)
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync();

            decimal subtotal = 0;
            decimal vatTotal = 0;
            var orderItems = new List<OrderItem>();
            var stockMovements = new List<StockMovement>();

            var recommendationItemsPayload = new List<object>();
            var erpItemsPayload = new List<object>();

            foreach (var cartItem in cart.Items)
            {
                var product = products.FirstOrDefault(p => p.Id == cartItem.ProductId);
                if (product == null || !product.IsActive)
                {
                    await transaction.RollbackAsync();
                    return new CheckoutResultDto(false, null, null, $"'{cartItem.ProductId}' kodlu ürün satışta değil.");
                }

                if (product.Stock == null || product.Stock.Quantity < cartItem.Quantity)
                {
                    await transaction.RollbackAsync();
                    var available = product.Stock?.Quantity ?? 0;
                    return new CheckoutResultDto(false, null, null, $"'{product.Name}' ürünü için yetersiz stok! Mevcut: {available}, İstenen: {cartItem.Quantity}");
                }

                var price = product.Price;
                var vatRate = product.VatRate;
                var qty = cartItem.Quantity;

                var lineSubtotal = Math.Round(price * qty, 2, MidpointRounding.AwayFromZero);
                var lineVat = Math.Round(lineSubtotal * (vatRate / 100m), 2, MidpointRounding.AwayFromZero);
                var lineTotal = lineSubtotal + lineVat;

                subtotal += lineSubtotal;
                vatTotal += lineVat;

                var orderItem = new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = product.Name,
                    UnitPrice = price,
                    VatRate = vatRate,
                    Quantity = qty,
                    NetLineAmount = lineSubtotal,
                    VatAmount = lineVat,
                    LineTotal = lineTotal
                };
                orderItems.Add(orderItem);

                // Atomic Stock Decrease & Movement Record
                var prevStock = product.Stock.Quantity;
                var newStock = prevStock - qty;
                product.Stock.Quantity = newStock;
                product.Stock.UpdatedAtUtc = DateTime.UtcNow;

                var stockMovement = new StockMovement
                {
                    ProductId = product.Id,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = -qty,
                    PreviousQuantity = prevStock,
                    NewQuantity = newStock,
                    ReferenceType = StockReferenceType.Order,
                    Description = "Order Checkout",
                    CreatedAtUtc = DateTime.UtcNow
                };
                stockMovements.Add(stockMovement);

                // Payloads for Outbox events
                recommendationItemsPayload.Add(new
                {
                    ProductId = product.Id,
                    Quantity = qty
                });

                erpItemsPayload.Add(new
                {
                    ProductId = product.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    UnitPrice = price,
                    VatRate = vatRate,
                    Quantity = qty,
                    LineSubtotal = lineSubtotal,
                    LineVat = lineVat,
                    LineTotal = lineTotal
                });
            }

            var grandTotal = subtotal + vatTotal;
            var correlationId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

            // 3. Payment Simulation
            bool paymentSuccess = request.SimulateSuccess;
            if (!paymentSuccess)
            {
                await transaction.RollbackAsync();
                return new CheckoutResultDto(false, null, null, "Ödeme simülasyonu reddedildi.");
            }

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Amount = grandTotal,
                Method = PaymentMethod.CardSimulation,
                SimulationReference = $"SIM-PAY-{Guid.NewGuid():N}",
                Status = PaymentStatus.Succeeded,
                ProcessedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            // 4. Create Order Address Snapshot
            var orderAddress = new OrderAddress
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
            };

            // 5. Create Order
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
                Currency = "TRY",
                CorrelationId = correlationId,
                PlacedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                AddressSnapshot = orderAddress,
                Payment = payment,
                Items = orderItems
            };

            // 6. Update Cart Status to Converted
            cart.Status = CartStatus.Converted;
            cart.UpdatedAtUtc = DateTime.UtcNow;

            // 7. Write Outbox Messages
            var recEventId = Guid.NewGuid();
            var recEventPayload = new
            {
                EventId = recEventId,
                OccurredAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                OrderId = orderId,
                OrderNumber = orderNumber,
                CustomerId = customerId,
                Items = recommendationItemsPayload
            };

            var outboxRec = new OutboxMessage
            {
                EventId = recEventId,
                EventType = "OrderConfirmedForRecommendationV1",
                Destination = "Recommendation.Api",
                AggregateType = "Order",
                AggregateId = orderId,
                Payload = JsonSerializer.Serialize(recEventPayload),
                Status = OutboxStatus.Pending,
                OccurredAtUtc = DateTime.UtcNow,
                AvailableAtUtc = DateTime.UtcNow,
                AttemptCount = 0,
                CorrelationId = correlationId
            };

            var erpEventId = Guid.NewGuid();
            var erpEventPayload = new
            {
                EventId = erpEventId,
                OccurredAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                OrderId = orderId,
                OrderNumber = orderNumber,
                CustomerId = customerId,
                CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
                CustomerEmail = customer.User?.Email ?? string.Empty,
                ShippingAddress = new
                {
                    Title = address.Title,
                    ContactName = address.ContactName,
                    PhoneNumber = address.PhoneNumber,
                    AddressLine1 = address.AddressLine1,
                    AddressLine2 = address.AddressLine2,
                    District = address.District,
                    City = address.City,
                    CountryCode = address.CountryCode
                },
                Subtotal = subtotal,
                VatTotal = vatTotal,
                GrandTotal = grandTotal,
                Currency = "TRY",
                Items = erpItemsPayload
            };

            var outboxErp = new OutboxMessage
            {
                EventId = erpEventId,
                EventType = "OrderReadyForErpV1",
                Destination = "ErpIntegration.Api",
                AggregateType = "Order",
                AggregateId = orderId,
                Payload = JsonSerializer.Serialize(erpEventPayload),
                Status = OutboxStatus.Pending,
                OccurredAtUtc = DateTime.UtcNow,
                AvailableAtUtc = DateTime.UtcNow,
                AttemptCount = 0,
                CorrelationId = correlationId
            };

            _dbContext.StockMovements.AddRange(stockMovements);
            _dbContext.Orders.Add(order);
            _dbContext.OutboxMessages.Add(outboxRec);
            _dbContext.OutboxMessages.Add(outboxErp);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new CheckoutResultDto(true, orderId, orderNumber, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new CheckoutResultDto(false, null, null, $"Sipariş oluşturulurken hata oluştu: {ex.Message}");
        }
    }
}
