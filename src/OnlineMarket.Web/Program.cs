using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Http;
using OnlineMarket.Web.Infrastructure.Persistence;
using OnlineMarket.Web.Infrastructure.Workers;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext
var connectionString = builder.Configuration.GetConnectionString("OnlineMarketDb")
    ?? "Server=(localdb)\\mssqllocaldb;Database=OnlineMarketDb;Trusted_Connection=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<OnlineMarketDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<OnlineMarketDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Register Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICustomerAddressService, CustomerAddressService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();

// Configure HTTP Clients for External Services with Short Timeouts
var recommendationApiUrl = builder.Configuration["Services:RecommendationApi"] ?? "http://localhost:5001";
builder.Services.AddHttpClient<IRecommendationClient, RecommendationApiClient>(client =>
{
    client.BaseAddress = new Uri(recommendationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

var erpIntegrationApiUrl = builder.Configuration["Services:ErpIntegrationApi"] ?? "http://localhost:5002";
builder.Services.AddHttpClient<IErpIntegrationClient, ErpIntegrationApiClient>(client =>
{
    client.BaseAddress = new Uri(erpIntegrationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Outbox Named HTTP Clients
builder.Services.AddHttpClient("RecommendationApi", client =>
{
    client.BaseAddress = new Uri(recommendationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("ErpIntegrationApi", client =>
{
    client.BaseAddress = new Uri(erpIntegrationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Register Background Worker for Outbox processing
builder.Services.AddHostedService<OutboxBackgroundWorker>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Ensure Database Created, Migrated, and Seeded at Startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    try
    {
        dbContext.Database.Migrate();
        var catalogSeedPath = Path.Combine(app.Environment.ContentRootPath, "catalog-seed.json");
        DatabaseSeeder.SeedAsync(dbContext, userManager, roleManager, catalogSeedPath).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the OnlineMarketDb database.");
    }
}

// Configure HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
