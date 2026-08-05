using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Options;
using OnlineMarket.Web.Application.Services;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Infrastructure.Http;
using OnlineMarket.Web.Infrastructure.Importing;
using OnlineMarket.Web.Infrastructure.Persistence;
using OnlineMarket.Web.Infrastructure.Workers;

var seedDevelopmentData = args.Contains(
    "--seed-development-data",
    StringComparer.OrdinalIgnoreCase);
var importDemoExcelPath = GetOptionValue(args, "--import-demo-excel");
var emitHistoricalErpEvents = args.Contains(
    "--emit-historical-erp-events",
    StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(args);

// Add DbContext
var connectionString = builder.Configuration.GetConnectionString("OnlineMarketDb");

builder.Services.AddDbContext<OnlineMarketDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(connectionString) || string.Equals(connectionString, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase("OnlineMarketDb")
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    }
    else
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        });
    }
});

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
builder.Services.AddSingleton<IChatHistoryStore, InMemoryChatHistoryStore>();
builder.Services.AddScoped<IAiSupportService, AiSupportService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddScoped<IStockMutationService, SqlServerStockMutationService>();
builder.Services.AddScoped<IOrderNumberGenerator, SqlServerOrderNumberGenerator>();
builder.Services.AddScoped<IOutboxStore, SqlServerOutboxStore>();
builder.Services.AddScoped<IOutboxDispatcher, HttpOutboxDispatcher>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<DemoExcelImporter>();

// Configure AI Assistant Options & API Client
builder.Services.Configure<AiAssistantOptions>(builder.Configuration.GetSection(AiAssistantOptions.SectionName));
var aiTimeoutSeconds = builder.Configuration.GetValue<int>("AiAssistant:TimeoutSeconds", 10);
builder.Services.AddHttpClient<IAiApiClient, ExternalAiApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(aiTimeoutSeconds);
});

// Configure HTTP Clients for External Services with Short Timeouts
var recommendationApiUrl = builder.Configuration["Services:RecommendationApi"] ?? "http://localhost:5008";
builder.Services.AddHttpClient<IRecommendationClient, RecommendationApiClient>(client =>
{
    client.BaseAddress = new Uri(recommendationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddHttpMessageHandler<RecommendationApiKeyHandler>();

var erpIntegrationApiUrl = builder.Configuration["Services:ErpIntegrationApi"] ?? "http://localhost:5046";
builder.Services.AddHttpClient<IErpIntegrationClient, ErpIntegrationApiClient>(client =>
{
    client.BaseAddress = new Uri(erpIntegrationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Outbox Named HTTP Clients
builder.Services
    .AddOptions<RecommendationOutboxOptions>()
    .Bind(builder.Configuration.GetSection(
        RecommendationOutboxOptions.SectionName))
    .Configure(options =>
        options.RecommendationApiBaseAddress = recommendationApiUrl)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        $"Configuration value '{RecommendationOutboxOptions.SectionName}:ApiKey' " +
        "is required when Recommendation service requests are enabled.")
    .ValidateOnStart();
builder.Services.AddTransient<RecommendationApiKeyHandler>();
builder.Services.AddHttpClient("RecommendationApi", client =>
{
    client.BaseAddress = new Uri(recommendationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddHttpMessageHandler<RecommendationApiKeyHandler>();

builder.Services.AddHttpClient("ErpIntegrationApi", client =>
{
    client.BaseAddress = new Uri(erpIntegrationApiUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Register Background Worker for Outbox processing
builder.Services.AddHostedService<OutboxBackgroundWorker>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (importDemoExcelPath is not null)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "The demo Excel import command is available only in the Development environment.");
    }

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
    if (!dbContext.Database.IsSqlServer())
    {
        throw new InvalidOperationException(
            "The demo Excel import command requires an OnlineMarketDb SQL Server connection.");
    }

    if ((await dbContext.Database.GetPendingMigrationsAsync()).Any())
    {
        throw new InvalidOperationException(
            "Apply OnlineMarketDb migrations with the controlled database tooling before running the demo Excel import command.");
    }

    var customerPassword = app.Configuration["DemoImport:CustomerPassword"];
    if (string.IsNullOrWhiteSpace(customerPassword))
    {
        throw new InvalidOperationException(
            "DemoImport:CustomerPassword must be configured for the demo Excel import command.");
    }

    var workbookPath = Path.GetFullPath(
        importDemoExcelPath,
        Directory.GetCurrentDirectory());
    var importer = scope.ServiceProvider.GetRequiredService<DemoExcelImporter>();
    await importer.ImportAsync(
        workbookPath,
        customerPassword,
        emitHistoricalErpEvents);
    return;
}

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

// Auto-seed InMemory database for local preview
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OnlineMarketDbContext>();
    if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
    {
        dbContext.Database.EnsureCreated();
        var catalogSeedPath = Path.Combine(AppContext.BaseDirectory, "Seed", "catalog.v1.json");
        if (File.Exists(catalogSeedPath))
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            var credentials = new SeedAdminCredentials("admin@onlinemarket.com", "Admin123!");
            await seeder.SeedAsync(catalogSeedPath, credentials);
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Failed to auto-seed database on startup.");
}

app.Run();

static string? GetOptionValue(string[] commandLineArguments, string optionName)
{
    var indexes = commandLineArguments
        .Select((value, index) => (value, index))
        .Where(candidate => string.Equals(
            candidate.value,
            optionName,
            StringComparison.OrdinalIgnoreCase))
        .Select(candidate => candidate.index)
        .ToArray();

    if (indexes.Length == 0)
    {
        return null;
    }

    if (indexes.Length > 1)
    {
        throw new ArgumentException($"Command option '{optionName}' may be supplied only once.");
    }

    var valueIndex = indexes[0] + 1;
    if (valueIndex >= commandLineArguments.Length
        || commandLineArguments[valueIndex].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"Command option '{optionName}' requires a workbook path.");
    }

    return commandLineArguments[valueIndex];
}
