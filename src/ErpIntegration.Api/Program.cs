using System.Text.Json.Serialization;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Services;
using ErpIntegration.Api.Infrastructure.Http;
using ErpIntegration.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("IntegrationDb")
    ?? throw new InvalidOperationException(
        "Connection string 'IntegrationDb' is required.");

builder.Services.AddDbContext<IntegrationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IIntegrationOrderStore, SqlServerIntegrationOrderStore>();
builder.Services.AddScoped<IIntegrationOrderService, IntegrationOrderService>();
builder.Services.AddSingleton<OrderReadyForErpV1Validator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<GlobalApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling =
            JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
