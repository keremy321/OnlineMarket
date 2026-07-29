using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class CustomerAddressService : ICustomerAddressService
{
    private readonly OnlineMarketDbContext _dbContext;

    public CustomerAddressService(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<CustomerAddressDto>> GetCustomerAddressesAsync(Guid customerId)
    {
        var addresses = await _dbContext.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ToListAsync();

        return addresses.Select(MapToDto).ToList();
    }

    public async Task<CustomerAddressDto?> GetAddressByIdAsync(Guid addressId, Guid customerId)
    {
        var address = await _dbContext.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId && a.IsActive);

        return address == null ? null : MapToDto(address);
    }

    public async Task<CustomerAddressDto> AddAddressAsync(Guid customerId, CreateAddressDto dto)
    {
        var existingCount = await _dbContext.CustomerAddresses
            .CountAsync(a => a.CustomerId == customerId && a.IsActive);

        bool isFirstAddress = existingCount == 0;
        bool isDefault = dto.IsDefault || isFirstAddress;

        if (isDefault)
        {
            await ClearExistingDefaultAsync(customerId);
        }

        var address = new CustomerAddress
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Title = dto.Title,
            ContactName = dto.ContactName,
            PhoneNumber = dto.PhoneNumber,
            AddressLine1 = dto.AddressLine1,
            AddressLine2 = dto.AddressLine2,
            District = dto.District,
            City = dto.City,
            PostalCode = dto.PostalCode,
            CountryCode = "TR",
            IsDefault = isDefault,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.CustomerAddresses.Add(address);
        await _dbContext.SaveChangesAsync();

        return MapToDto(address);
    }

    public async Task<bool> UpdateAddressAsync(Guid addressId, Guid customerId, CreateAddressDto dto)
    {
        var address = await _dbContext.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId && a.IsActive);

        if (address == null) return false;

        if (dto.IsDefault && !address.IsDefault)
        {
            await ClearExistingDefaultAsync(customerId);
            address.IsDefault = true;
        }

        address.Title = dto.Title;
        address.ContactName = dto.ContactName;
        address.PhoneNumber = dto.PhoneNumber;
        address.AddressLine1 = dto.AddressLine1;
        address.AddressLine2 = dto.AddressLine2;
        address.District = dto.District;
        address.City = dto.City;
        address.PostalCode = dto.PostalCode;
        address.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAddressAsync(Guid addressId, Guid customerId)
    {
        var address = await _dbContext.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId && a.IsActive);

        if (address == null) return false;

        address.IsActive = false;
        address.IsDefault = false;
        address.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // If default address was deleted, promote another address if exists
        var hasDefault = await _dbContext.CustomerAddresses
            .AnyAsync(a => a.CustomerId == customerId && a.IsActive && a.IsDefault);

        if (!hasDefault)
        {
            var firstRemaining = await _dbContext.CustomerAddresses
                .FirstOrDefaultAsync(a => a.CustomerId == customerId && a.IsActive);
            if (firstRemaining != null)
            {
                firstRemaining.IsDefault = true;
                firstRemaining.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }

        return true;
    }

    public async Task<bool> SetDefaultAddressAsync(Guid addressId, Guid customerId)
    {
        var address = await _dbContext.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId && a.IsActive);

        if (address == null) return false;

        await ClearExistingDefaultAsync(customerId);
        address.IsDefault = true;
        address.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    private async Task ClearExistingDefaultAsync(Guid customerId)
    {
        var currentDefaults = await _dbContext.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsActive && a.IsDefault)
            .ToListAsync();

        foreach (var def in currentDefaults)
        {
            def.IsDefault = false;
            def.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static CustomerAddressDto MapToDto(CustomerAddress a) => new(
        a.Id,
        a.CustomerId,
        a.Title,
        a.ContactName,
        a.PhoneNumber,
        a.AddressLine1,
        a.AddressLine2,
        a.District,
        a.City,
        a.PostalCode,
        a.CountryCode,
        a.IsDefault,
        a.IsActive
    );
}
