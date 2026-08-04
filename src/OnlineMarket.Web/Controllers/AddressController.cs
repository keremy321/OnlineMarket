using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Models;

namespace OnlineMarket.Web.Controllers;

[Authorize]
public class AddressController : Controller
{
    private readonly ICustomerAddressService _addressService;
    private readonly IAuthService _authService;
    private readonly ILogger<AddressController> _logger;

    public AddressController(
        ICustomerAddressService addressService,
        IAuthService authService,
        ILogger<AddressController> logger)
    {
        _addressService = addressService;
        _authService = authService;
        _logger = logger;
    }

    private async Task<Guid?> GetCurrentCustomerIdAsync()
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdStr, out var userId))
            {
                var customer = await _authService.GetCustomerByUserIdAsync(userId);
                return customer?.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve current customer ID.");
        }
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue) return RedirectToAction("Login", "Account");

            var addresses = await _addressService.GetCustomerAddressesAsync(customerId.Value);
            return View(addresses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching addresses in AddressController.Index.");
            return View(new List<CustomerAddressDto>());
        }
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new AddressFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AddressFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            var dto = new CreateAddressDto(
                model.Title,
                model.ContactName,
                model.PhoneNumber,
                model.AddressLine1,
                model.AddressLine2,
                model.District,
                model.City,
                model.PostalCode,
                model.IsDefault
            );

            await _addressService.AddAddressAsync(customerId.Value, dto);
            TempData["SuccessMessage"] = "Adres başarıyla eklendi.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating address for customer {CustomerId}.", customerId);
            ModelState.AddModelError(string.Empty, "Adres eklenirken bir hata oluştu.");
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        try
        {
            var customerId = await GetCurrentCustomerIdAsync();
            if (!customerId.HasValue) return RedirectToAction("Login", "Account");

            var address = await _addressService.GetAddressByIdAsync(id, customerId.Value);
            if (address == null) return NotFound();

            var model = new AddressFormViewModel
            {
                Id = address.Id,
                Title = address.Title,
                ContactName = address.ContactName,
                PhoneNumber = address.PhoneNumber,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                District = address.District,
                City = address.City,
                PostalCode = address.PostalCode,
                IsDefault = address.IsDefault
            };

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching address {AddressId} for edit.", id);
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AddressFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            var dto = new CreateAddressDto(
                model.Title,
                model.ContactName,
                model.PhoneNumber,
                model.AddressLine1,
                model.AddressLine2,
                model.District,
                model.City,
                model.PostalCode,
                model.IsDefault
            );

            var success = await _addressService.UpdateAddressAsync(id, customerId.Value, dto);
            if (!success) return NotFound();

            TempData["SuccessMessage"] = "Adres başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating address {AddressId} for customer {CustomerId}.", id, customerId);
            ModelState.AddModelError(string.Empty, "Adres güncellenirken bir hata oluştu.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            await _addressService.DeleteAddressAsync(id, customerId.Value);
            TempData["SuccessMessage"] = "Adres silindi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting address {AddressId} for customer {CustomerId}.", id, customerId);
            TempData["ErrorMessage"] = "Adres silinirken bir hata oluştu.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        try
        {
            await _addressService.SetDefaultAddressAsync(id, customerId.Value);
            TempData["SuccessMessage"] = "Varsayılan adres güncellendi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default address {AddressId} for customer {CustomerId}.", id, customerId);
            TempData["ErrorMessage"] = "Varsayılan adres ayarlanırken bir hata oluştu.";
        }
        return RedirectToAction(nameof(Index));
    }
}
