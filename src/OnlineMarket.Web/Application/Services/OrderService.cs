using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class OrderService : IOrderService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IErpIntegrationClient _erpIntegrationClient;

    public OrderService(OnlineMarketDbContext dbContext, IErpIntegrationClient erpIntegrationClient)
    {
        _dbContext = dbContext;
        _erpIntegrationClient = erpIntegrationClient;
    }

    public async Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId)
    {
        var orders = await _dbContext.Orders
            .Include(o => o.AddressSnapshot)
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAtUtc)
            .ToListAsync();

        var result = new List<OrderDto>();
        foreach (var order in orders)
        {
            result.Add(MapToDto(order, null));
        }
        return result;
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid orderId, Guid customerId)
    {
        var order = await _dbContext.Orders
            .Include(o => o.AddressSnapshot)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);

        if (order == null) return null;

        ErpOrderTransferStatusDto? erpStatus = null;
        try
        {
            erpStatus = await _erpIntegrationClient.GetErpOrderTransferStatusAsync(orderId);
        }
        catch
        {
            // Fallback gracefully
        }

        return MapToDto(order, erpStatus);
    }

    public async Task<List<OrderDto>> GetAllOrdersForAdminAsync()
    {
        var orders = await _dbContext.Orders
            .Include(o => o.AddressSnapshot)
            .Include(o => o.Items)
            .OrderByDescending(o => o.PlacedAtUtc)
            .ToListAsync();

        return orders.Select(o => MapToDto(o, null)).ToList();
    }

    private static OrderDto MapToDto(Order o, ErpOrderTransferStatusDto? erpStatus)
    {
        var addressDto = o.AddressSnapshot == null ? null : new OrderAddressDto(
            "Sipariş Adresi",
            o.AddressSnapshot.RecipientName,
            o.AddressSnapshot.PhoneNumber,
            o.AddressSnapshot.AddressLine1,
            o.AddressSnapshot.AddressLine2,
            o.AddressSnapshot.District,
            o.AddressSnapshot.City,
            o.AddressSnapshot.CountryCode
        );

        var itemDtos = o.Items.Select(i => new OrderItemDto(
            i.Id,
            i.ProductId,
            i.SkuSnapshot,
            i.ProductNameSnapshot,
            i.UnitPrice,
            i.VatRate,
            i.Quantity,
            i.NetLineAmount,
            i.VatAmount,
            i.LineTotal
        )).ToList();

        return new OrderDto(
            o.Id,
            o.OrderNumber,
            o.CustomerId,
            o.SourceCartId,
            o.Status,
            o.Subtotal,
            o.VatTotal,
            o.GrandTotal,
            o.Currency,
            o.CorrelationId,
            o.PlacedAtUtc,
            addressDto,
            itemDtos,
            erpStatus
        );
    }
}
