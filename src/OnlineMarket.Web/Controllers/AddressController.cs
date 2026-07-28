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

    public AddressController(ICustomerAddressService addressService, IAuthService authService)
    {
        _addressService = addressService;
        _authService = authService;
    }

    private async Task<Guid?> GetCurrentCustomerIdAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdStr, out var userId))
        {
            var customer = await _authService.GetCustomerByUserIdAsync(userId);
            return customer?.Id;
        }
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        var addresses = await _addressService.GetCustomerAddressesAsync(customerId.Value);
        return View(addresses);
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

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AddressFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        await _addressService.DeleteAddressAsync(id, customerId.Value);
        TempData["SuccessMessage"] = "Adres silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        var customerId = await GetCurrentCustomerIdAsync();
        if (!customerId.HasValue) return RedirectToAction("Login", "Account");

        await _addressService.SetDefaultAddressAsync(id, customerId.Value);
        TempData["SuccessMessage"] = "Varsayılan adres güncellendi.";
        return RedirectToAction(nameof(Index));
    }
}
