using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class CartService : ICartService
{
    private readonly OnlineMarketDbContext _dbContext;

    public CartService(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CartDto> GetOrCreateActiveCartAsync(Guid customerId)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p!.Stock)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active);

        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Status = CartStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Carts.Add(cart);
            await _dbContext.SaveChangesAsync();
        }

        return MapToDto(cart);
    }

    public async Task<CartDto> AddItemToCartAsync(Guid customerId, Guid productId, int quantity)
    {
        if (quantity <= 0) quantity = 1;

        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active);

        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Status = CartStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Carts.Add(cart);
            await _dbContext.SaveChangesAsync();
        }

        var product = await _dbContext.Products
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);

        if (product == null)
        {
            throw new InvalidOperationException("Ürün bulunamadı veya pasif.");
        }

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
            existingItem.LastKnownUnitPrice = product.Price;
            existingItem.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var cartItem = new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = productId,
                Quantity = quantity,
                LastKnownUnitPrice = product.Price,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            cart.Items.Add(cartItem);
        }

        cart.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return await GetOrCreateActiveCartAsync(customerId);
    }

    public async Task<CartDto> UpdateItemQuantityAsync(Guid customerId, Guid cartItemId, int quantity)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active);

        if (cart == null)
        {
            throw new InvalidOperationException("Aktif sepet bulunamadı.");
        }

        var item = cart.Items.FirstOrDefault(i => i.Id == cartItemId);
        if (item != null)
        {
            if (quantity <= 0)
            {
                _dbContext.CartItems.Remove(item);
            }
            else
            {
                item.Quantity = quantity;
                item.UpdatedAtUtc = DateTime.UtcNow;
            }

            cart.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return await GetOrCreateActiveCartAsync(customerId);
    }

    public async Task<CartDto> RemoveItemFromCartAsync(Guid customerId, Guid cartItemId)
    {
        return await UpdateItemQuantityAsync(customerId, cartItemId, 0);
    }

    public async Task<CartDto> ClearCartAsync(Guid customerId)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active);

        if (cart != null && cart.Items.Any())
        {
            _dbContext.CartItems.RemoveRange(cart.Items);
            cart.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return await GetOrCreateActiveCartAsync(customerId);
    }

    public static CartDto MapToDto(Cart cart)
    {
        var itemDtos = new List<CartItemDto>();
        decimal subtotal = 0;
        decimal vatTotal = 0;

        foreach (var i in cart.Items)
        {
            var product = i.Product;
            var price = i.LastKnownUnitPrice;
            var vatRate = product?.VatRate ?? 20.00m;
            var qty = i.Quantity;
            var availableStock = product?.Stock?.Quantity ?? 0;

            var lineSubtotal = Math.Round(price * qty, 2, MidpointRounding.AwayFromZero);
            var lineVat = Math.Round(lineSubtotal * (vatRate / 100m), 2, MidpointRounding.AwayFromZero);
            var lineTotal = lineSubtotal + lineVat;

            subtotal += lineSubtotal;
            vatTotal += lineVat;

            itemDtos.Add(new CartItemDto(
                i.Id,
                i.ProductId,
                product?.Name ?? "Ürün",
                product?.Sku ?? string.Empty,
                product?.ImageUrl,
                price,
                vatRate,
                qty,
                availableStock,
                lineSubtotal,
                lineVat,
                lineTotal
            ));
        }

        var grandTotal = subtotal + vatTotal;

        return new CartDto(
            cart.Id,
            cart.CustomerId,
            cart.Status,
            itemDtos,
            subtotal,
            vatTotal,
            grandTotal
        );
    }
}
