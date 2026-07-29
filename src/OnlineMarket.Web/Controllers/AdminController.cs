using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Infrastructure.Persistence;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly IOrderService _orderService;
    private readonly OnlineMarketDbContext _dbContext;
    private readonly IOutboxService _outboxService;

    public AdminController(
        ICatalogService catalogService,
        IOrderService orderService,
        OnlineMarketDbContext dbContext,
        IOutboxService outboxService)
    {
        _catalogService = catalogService;
        _orderService = orderService;
        _dbContext = dbContext;
        _outboxService = outboxService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var totalProducts = await _dbContext.Products.CountAsync();
        var outOfStock = await _dbContext.Stocks.CountAsync(s => s.Quantity <= 0);
        var totalOrders = await _dbContext.Orders.CountAsync();
        var pendingOutbox = await _dbContext.OutboxMessages.CountAsync(m => m.Status == Domain.Enums.OutboxStatus.Pending);

        var recentOrders = await _orderService.GetAllOrdersForAdminAsync();

        var viewModel = new AdminDashboardViewModel
        {
            TotalProducts = totalProducts,
            OutOfStockProducts = outOfStock,
            TotalOrders = totalOrders,
            PendingOutboxMessages = pendingOutbox,
            RecentOrders = recentOrders.Take(10).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Products()
    {
        var products = await _catalogService.GetProductsAsync(new ProductFilterDto(null, null, null, null, null, false, "newest"));
        return View(products);
    }

    [HttpGet]
    public async Task<IActionResult> CreateProduct()
    {
        var categories = await _catalogService.GetCategoriesAsync();
        var brands = await _catalogService.GetBrandsAsync();

        var viewModel = new ProductFormViewModel
        {
            Categories = categories,
            Brands = brands
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(ProductFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await _catalogService.GetCategoriesAsync();
            model.Brands = await _catalogService.GetBrandsAsync();
            return View(model);
        }

        var dto = new ProductDto(
            Guid.Empty,
            model.Sku,
            model.Name,
            model.Name.ToLower().Replace(" ", "-"),
            model.Description,
            model.CategoryId,
            string.Empty,
            model.BrandId,
            string.Empty,
            model.Price,
            model.VatRate,
            model.NetContent,
            model.UnitType,
            model.ImageUrl,
            model.IsActive,
            model.InitialStock,
            model.InitialStock > 0
        );

        await _catalogService.CreateProductAsync(dto, model.InitialStock);
        TempData["SuccessMessage"] = "Ürün ve stok başarıyla oluşturuldu.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> EditProduct(Guid id)
    {
        var product = await _catalogService.GetProductByIdAsync(id);
        if (product == null) return NotFound();

        var categories = await _catalogService.GetCategoriesAsync();
        var brands = await _catalogService.GetBrandsAsync();

        var viewModel = new ProductFormViewModel
        {
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            Price = product.Price,
            VatRate = product.VatRate,
            NetContent = product.NetContent,
            UnitType = product.UnitType,
            ImageUrl = product.ImageUrl,
            IsActive = product.IsActive,
            InitialStock = product.StockQuantity,
            Categories = categories,
            Brands = brands
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(Guid id, ProductFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await _catalogService.GetCategoriesAsync();
            model.Brands = await _catalogService.GetBrandsAsync();
            return View(model);
        }

        var dto = new ProductDto(
            id,
            model.Sku,
            model.Name,
            model.Name.ToLower().Replace(" ", "-"),
            model.Description,
            model.CategoryId,
            string.Empty,
            model.BrandId,
            string.Empty,
            model.Price,
            model.VatRate,
            model.NetContent,
            model.UnitType,
            model.ImageUrl,
            model.IsActive,
            model.InitialStock,
            model.InitialStock > 0
        );

        await _catalogService.UpdateProductAsync(id, dto);
        TempData["SuccessMessage"] = "Ürün bilgileri ve snapshot olayları başarıyla güncellendi.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> AdjustStock(Guid id)
    {
        var product = await _catalogService.GetProductByIdAsync(id);
        if (product == null) return NotFound();

        var viewModel = new StockAdjustViewModel
        {
            ProductId = product.Id,
            ProductName = product.Name,
            CurrentStock = product.StockQuantity
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustStock(StockAdjustViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(userIdStr, out var userId);

        var success = await _catalogService.AdjustStockAsync(model.ProductId, model.QuantityChange, model.Reason, userId);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, "Stok güncellenemedi. Miktar eksiye düşemez.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Stok hareketi başarıyla işlendi.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> Outbox()
    {
        var messages = await _dbContext.OutboxMessages
            .OrderByDescending(m => m.OccurredAtUtc)
            .Take(100)
            .ToListAsync();

        return View(messages);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessOutboxNow()
    {
        await _outboxService.ProcessPendingMessagesAsync(20);
        TempData["SuccessMessage"] = "Outbox mesajları manuel olarak tetiklendi ve işlendi.";
        return RedirectToAction(nameof(Outbox));
    }
}
