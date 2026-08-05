using System.Text.Json.Serialization;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Infrastructure.Configuration;
using ErpIntegration.Api.Infrastructure.Health;
using ErpIntegration.Api.Infrastructure.Http;
using ErpIntegration.Api.Infrastructure.Persistence;
using ErpIntegration.Api.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<IntegrationDbContext>((services, options) =>
{
    var connectionString = services
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("IntegrationDb")
        ?? throw new InvalidOperationException(
            "Connection string 'IntegrationDb' is required.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddScoped<IIntegrationOrderStore, SqlServerIntegrationOrderStore>();
builder.Services.AddScoped<IIntegrationStepStore, SqlServerIntegrationStepStore>();
builder.Services.AddScoped<IIntegrationOrderService, IntegrationOrderService>();
builder.Services.AddScoped<
    IIntegrationStepProcessor,
    IntegrationStepProcessor>();
builder.Services.AddScoped<IntegrationRetryPolicy>();
builder.Services.AddSingleton<OrderReadyForErpV1Validator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MockErpCircuitBreaker>();
builder.Services.AddSingleton<
    IValidateOptions<MockErpClientOptions>,
    MockErpClientOptionsValidator>();
builder.Services.AddSingleton<
    IValidateOptions<IntegrationWorkerOptions>,
    IntegrationWorkerOptionsValidator>();
builder.Services
    .AddOptions<MockErpClientOptions>()
    .Bind(builder.Configuration.GetSection(
        MockErpClientOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<IntegrationWorkerOptions>()
    .Bind(builder.Configuration.GetSection(
        IntegrationWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpClient<IMockErpClient, MockErpClient>(
    (services, client) =>
    {
        var options = services
            .GetRequiredService<IOptions<MockErpClientOptions>>()
            .Value;
        client.BaseAddress = new Uri(
            $"{options.BaseAddress.TrimEnd('/')}/",
            UriKind.Absolute);
        client.Timeout = options.Timeout;
    });
builder.Services.AddHostedService<IntegrationWorker>();
builder.Services
    .AddHealthChecks()
    .AddCheck<IntegrationDatabaseHealthCheck>("integration-db")
    .AddCheck<MockErpConfigurationHealthCheck>(
        "mock-erp-configuration");
builder.Services.AddExceptionHandler<GlobalApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling =
            JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Online Market ERP Integration API";
        document.Info.Description =
            "Receives OrderReadyForErp events from the OnlineMarket.Web outbox, "
            + "relays them to the Mock ERP system as customers, orders, stock "
            + "movements and accounting entries, and exposes integration batch "
            + "status and retry operations. This service does not require "
            + "X-Api-Key authentication.";

        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
        options.WithTitle("Online Market ERP Integration API"));
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").WithTags("Health");

app.Run();

public partial class Program;
