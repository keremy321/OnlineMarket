using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Http;
using OnlineMarket.Web.Infrastructure.Persistence;
using OnlineMarket.Web.Infrastructure.Workers;

var seedDevelopmentData = args.Contains(
    "--seed-development-data",
    StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(args);

// Add DbContext
var connectionString = builder.Configuration.GetConnectionString("OnlineMarketDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:OnlineMarketDb must be supplied through approved configuration.");

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
builder.Services.AddScoped<IAdminQueryService, AdminQueryService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddScoped<IStockMutationService, SqlServerStockMutationService>();
builder.Services.AddScoped<IOrderNumberGenerator, SqlServerOrderNumberGenerator>();
builder.Services.AddScoped<IOutboxStore, SqlServerOutboxStore>();
builder.Services.AddScoped<IOutboxDispatcher, HttpOutboxDispatcher>();
builder.Services.AddScoped<DatabaseSeeder>();

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

if (seedDevelopmentData)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "The explicit database seed operation is available only in the Development environment.");
    }

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
    if ((await dbContext.Database.GetPendingMigrationsAsync()).Any())
    {
        throw new InvalidOperationException(
            "Apply OnlineMarketDb migrations with the controlled database tooling before running the seed command.");
    }

    var catalogSeedPath = Path.Combine(
        AppContext.BaseDirectory,
        "Seed",
        "catalog.v1.json");
    var credentials = new SeedAdminCredentials(
        app.Configuration["SeedAdmin:Email"],
        app.Configuration["SeedAdmin:Password"]);
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync(catalogSeedPath, credentials);
    return;
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
