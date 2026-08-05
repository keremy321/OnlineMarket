using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Infrastructure.Identity;

public sealed class CustomerIdentityResolver(
    OnlineMarketDbContext dbContext) : ICustomerIdentityResolver
{
    public Task<Guid?> GetActiveCustomerIdByUserIdAsync(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult<Guid?>(null);
        }

        return dbContext.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.UserId == userId
                && customer.IsActive)
            .Select(customer => (Guid?)customer.Id)
            .SingleOrDefaultAsync();
    }
}
