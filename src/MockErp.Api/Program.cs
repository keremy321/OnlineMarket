using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MockErp.Api.Application.Interfaces;
using MockErp.Api.Application.Services;
using MockErp.Api.Contracts;
using MockErp.Api.Infrastructure.Http;
using MockErp.Api.Infrastructure.Persistence;
using MockErp.Api.Infrastructure.Security;

var demoStockWorkbookPath = GetDemoStockWorkbookPath(args);
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MockErpDbContext>((services, options) =>
{
    var connectionString = services
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("MockErpDb")
        ?? throw new InvalidOperationException(
            "Connection string 'MockErpDb' is required.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddScoped<MockErpStockSeeder>();
builder.Services.AddScoped<DemoErpStockExcelSeeder>();
builder.Services.AddScoped<IMockErpStore, SqlServerMockErpStore>();
builder.Services.AddScoped<IMockErpService, MockErpService>();
builder.Services.AddSingleton<MockErpRequestValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyDefaults.Scheme,
        options => options.ApiKey =
            builder.Configuration["Security:ApiKey"] ?? string.Empty);
builder.Services.AddAuthorization();
builder.Services.AddScoped<IdempotencyKeyFilter>();
builder.Services.AddExceptionHandler<GlobalApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers(options =>
        options.Filters.Add<IdempotencyKeyFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling =
            JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(item => item.Value?.Errors.Count > 0)
            .ToDictionary(
                item => item.Key,
                item => item.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The JSON request is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);
        return new BadRequestObjectResult(
            new ApiValidationErrorResponse(
                "Validation.Failed",
                "Request validation failed.",
                false,
                errors));
    };
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(
        "MockErp",
        limiter =>
        {
            limiter.PermitLimit = 120;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
            limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        });
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "RateLimit.Exceeded",
                "The Mock ERP request rate limit was exceeded.",
                true),
            cancellationToken);
    };
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (demoStockWorkbookPath is not null)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "The --seed-demo-stocks command can run only in Development.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider
        .GetRequiredService<DemoErpStockExcelSeeder>();
    var result = await seeder.SeedAsync(demoStockWorkbookPath);
    var status = result.AlreadySeeded ? "AlreadySeeded" : "Seeded";
    Console.WriteLine(
        $"Demo ERP stock seed {status}: " +
        $"{result.ProductCount} workbook products, " +
        $"{result.InsertedCount} inserted stocks.");
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("MockErp");

app.Run();

static string? GetDemoStockWorkbookPath(string[] arguments)
{
    var indexes = arguments
        .Select((value, index) => (value, index))
        .Where(item => string.Equals(
            item.value,
            "--seed-demo-stocks",
            StringComparison.Ordinal))
        .Select(item => item.index)
        .ToArray();

    if (indexes.Length == 0)
    {
        return null;
    }

    if (indexes.Length != 1
        || indexes[0] + 1 >= arguments.Length
        || arguments[indexes[0] + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException(
            "The --seed-demo-stocks command requires exactly one explicit workbook path.");
    }

    return arguments[indexes[0] + 1];
}

public partial class Program;
