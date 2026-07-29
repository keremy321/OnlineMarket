using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence;

public sealed class MockErpDbContext(
    DbContextOptions<MockErpDbContext> options)
    : DbContext(options)
{
    public DbSet<ErpCustomer> ErpCustomers => Set<ErpCustomer>();

    public DbSet<ErpOrder> ErpOrders => Set<ErpOrder>();

    public DbSet<ErpOrderAddress> ErpOrderAddresses =>
        Set<ErpOrderAddress>();

    public DbSet<ErpOrderLine> ErpOrderLines => Set<ErpOrderLine>();

    public DbSet<ErpStock> ErpStocks => Set<ErpStock>();

    public DbSet<ErpStockMovement> ErpStockMovements =>
        Set<ErpStockMovement>();

    public DbSet<ErpAccountingEntry> ErpAccountingEntries =>
        Set<ErpAccountingEntry>();

    public DbSet<ErpAccountingEntryLine> ErpAccountingEntryLines =>
        Set<ErpAccountingEntryLine>();

    public DbSet<ErpIdempotencyRecord> ErpIdempotencyRecords =>
        Set<ErpIdempotencyRecord>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(
            typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(MockErpDbContext).Assembly);
    }
}
