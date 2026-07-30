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

public partial class Program;
