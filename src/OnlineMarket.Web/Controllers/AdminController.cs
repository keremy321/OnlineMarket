using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly IAdminQueryService _adminQueryService;
    private readonly IProductImageService _productImageService;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ICatalogService catalogService,
        IAdminQueryService adminQueryService,
        IProductImageService productImageService,
        IOutboxService outboxService,
        ILogger<AdminController> logger)
    {
        _catalogService = catalogService;
        _adminQueryService = adminQueryService;
        _productImageService = productImageService;
        _outboxService = outboxService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var dashboard = await _adminQueryService.GetDashboardAsync();
            var viewModel = new AdminDashboardViewModel
            {
                TotalProducts = dashboard.TotalProducts,
                OutOfStockProducts = dashboard.OutOfStockProducts,
                TotalOrders = dashboard.TotalOrders,
                PendingOutboxMessages = dashboard.PendingOutboxMessages,
                RecentOrders = dashboard.RecentOrders
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Admin dashboard.");
            return View(new AdminDashboardViewModel());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Products()
    {
        try
        {
            var products = await _catalogService.GetProductsAsync(new ProductFilterDto(null, null, null, null, null, false, "newest"));
            return View(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products in AdminController.");
            return View(new List<ProductDto>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkMatchImages()
    {
        try
        {
            var count = await _productImageService.BulkMatchImagesFromFolderAsync();
            if (count > 0)
            {
                TempData["SuccessMessage"] = $"{count} adet ürün görseli isimlerine göre otomatik eşleştirildi ve güncellendi.";
            }
            else
            {
                TempData["InfoMessage"] = "Klasördeki görsellerle eşleşen yeni bir ürün bulunamadı veya tüm görseller zaten eşleştirilmiş.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk matching of product images.");
            TempData["ErrorMessage"] = "Görseller eşleştirilirken bir hata oluştu: " + ex.Message;
        }

        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> CreateProduct()
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CreateProduct form.");
            return RedirectToAction(nameof(Products));
        }
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

        try
        {
            if (model.ImageFile != null)
            {
                var uploadedUrl = await _productImageService.SaveProductImageAsync(model.ImageFile, model.Name, model.Sku);
                if (!string.IsNullOrEmpty(uploadedUrl))
                {
                    model.ImageUrl = uploadedUrl;
                }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product {Name}.", model.Name);
            ModelState.AddModelError(string.Empty, "Ürün oluşturulurken bir hata oluştu: " + ex.Message);
            model.Categories = await _catalogService.GetCategoriesAsync();
            model.Brands = await _catalogService.GetBrandsAsync();
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditProduct(Guid id)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product {Id} for edit.", id);
            return NotFound();
        }
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

        try
        {
            if (model.ImageFile != null)
            {
                var uploadedUrl = await _productImageService.SaveProductImageAsync(model.ImageFile, model.Name, model.Sku);
                if (!string.IsNullOrEmpty(uploadedUrl))
                {
                    model.ImageUrl = uploadedUrl;
                }
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
            TempData["SuccessMessage"] = "Ürün bilgileri ve görseli başarıyla güncellendi.";
            return RedirectToAction(nameof(Products));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product {Id}.", id);
            ModelState.AddModelError(string.Empty, "Ürün güncellenirken bir hata oluştu: " + ex.Message);
            model.Categories = await _catalogService.GetCategoriesAsync();
            model.Brands = await _catalogService.GetBrandsAsync();
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> AdjustStock(Guid id)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product {Id} for stock adjustment.", id);
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustStock(StockAdjustViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adjusting stock for product {ProductId}.", model.ProductId);
            ModelState.AddModelError(string.Empty, "Stok ayarlanırken bir hata oluştu.");
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Outbox()
    {
        try
        {
            var messages = await _adminQueryService.GetRecentOutboxMessagesAsync(100);
            var viewModel = messages.Select(message => new AdminOutboxMessageViewModel
            {
                Id = message.Id,
                EventType = message.EventType,
                Destination = message.Destination,
                AggregateId = message.AggregateId,
                Status = message.Status,
                AttemptCount = message.AttemptCount,
                OccurredAtUtc = message.OccurredAtUtc,
                ProcessedAtUtc = message.ProcessedAtUtc,
                LastErrorCode = message.LastErrorCode,
                MaskedLastError = message.MaskedLastError
            }).ToList();

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Outbox messages.");
            return View(new List<AdminOutboxMessageViewModel>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessOutboxNow()
    {
        try
        {
            await _outboxService.ProcessPendingMessagesAsync(20);
            TempData["SuccessMessage"] = "Outbox mesajları manuel olarak tetiklendi ve işlendi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending Outbox messages manually.");
            TempData["ErrorMessage"] = "Outbox mesajları işlenirken hata oluştu.";
        }
        return RedirectToAction(nameof(Outbox));
    }
}
