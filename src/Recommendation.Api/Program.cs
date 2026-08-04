using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Http;
using Recommendation.Api.Infrastructure.Persistence;
using Recommendation.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RecommendationDbContext>((services, options) =>
{
    var connectionString = services
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("RecommendationDb")
        ?? throw new InvalidOperationException(
            "Connection string 'RecommendationDb' is required.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddScoped<
    IRecommendationEventStore,
    SqlServerRecommendationEventStore>();
builder.Services.AddScoped<
    IRecommendationEventIngestionService,
    RecommendationEventIngestionService>();
builder.Services.AddSingleton<RecommendationEventValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyDefaults.Scheme,
        options => options.ApiKey =
            builder.Configuration["Security:ApiKey"] ?? string.Empty);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(
            ApiKeyDefaults.Scheme)
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddExceptionHandler<GlobalApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
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
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
