using Microsoft.EntityFrameworkCore;
using MockErp.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("MockErpDb")
    ?? throw new InvalidOperationException(
        "Connection string 'MockErpDb' is required.");

builder.Services.AddDbContext<MockErpDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<MockErpStockSeeder>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
