using Microsoft.AspNetCore.Identity;

namespace OnlineMarket.Web.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public Customer? Customer { get; set; }
}
