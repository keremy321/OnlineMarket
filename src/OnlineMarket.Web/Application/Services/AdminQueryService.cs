using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public sealed class AdminQueryService : IAdminQueryService
{
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IOrderService _orderService;

    public AdminQueryService(
        OnlineMarketDbContext dbContext,
        IOrderService orderService)
    {
        _dbContext = dbContext;
        _orderService = orderService;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var totalProducts = await _dbContext.Products.CountAsync(cancellationToken);
        var outOfStockProducts = await _dbContext.Stocks
            .CountAsync(stock => stock.Quantity <= 0, cancellationToken);
        var totalOrders = await _dbContext.Orders.CountAsync(cancellationToken);
        var pendingOutboxMessages = await _dbContext.OutboxMessages
            .CountAsync(
                message => message.Status == OutboxStatus.Pending,
                cancellationToken);
        var recentOrders = await _orderService.GetAllOrdersForAdminAsync();

        return new AdminDashboardDto(
            totalProducts,
            outOfStockProducts,
            totalOrders,
            pendingOutboxMessages,
            recentOrders.Take(10).ToList());
    }

    public async Task<List<AdminOutboxMessageDto>> GetRecentOutboxMessagesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var boundedCount = Math.Clamp(count, 1, 500);

        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .OrderByDescending(message => message.OccurredAtUtc)
            .Take(boundedCount)
            .Select(message => new AdminOutboxMessageDto(
                message.Id,
                message.EventType,
                message.Destination,
                message.AggregateId,
                message.Status,
                message.AttemptCount,
                message.OccurredAtUtc,
                message.ProcessedAtUtc,
                message.LastErrorCode,
                message.LastError == null
                    ? null
                    : "Delivery failed. See application logs."))
            .ToListAsync(cancellationToken);
    }
}
