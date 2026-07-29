using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Application.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly OnlineMarketDbContext _dbContext;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        OnlineMarketDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
    }

    public async Task<AuthResultDto> RegisterAsync(string email, string password, string firstName, string lastName)
    {
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            return new AuthResultDto(false, "E-posta adresi zaten kullanımda.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return new AuthResultDto(false, errors);
        }

        await _userManager.AddToRoleAsync(user, "Customer");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();

        await _signInManager.SignInAsync(user, isPersistent: false);

        return new AuthResultDto(true, UserId: user.Id, CustomerId: customer.Id);
    }

    public async Task<AuthResultDto> LoginAsync(string email, string password, bool rememberMe)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return new AuthResultDto(false, "Geçersiz e-posta veya şifre.");
        }

        var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return new AuthResultDto(false, "Geçersiz e-posta veya şifre.");
        }

        var customer = await GetCustomerByUserIdAsync(user.Id);
        return new AuthResultDto(true, UserId: user.Id, CustomerId: customer?.Id);
    }

    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<Customer?> GetCustomerByUserIdAsync(Guid userId)
    {
        return await _dbContext.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.UserId == userId);
    }

    public async Task<Customer?> GetCustomerByEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return null;
        return await GetCustomerByUserIdAsync(user.Id);
    }
}
