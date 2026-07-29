using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace ErpIntegration.Api.Infrastructure.Persistence;

public sealed class IntegrationDbContext(
    DbContextOptions<IntegrationDbContext> options)
    : DbContext(options)
{
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public DbSet<IntegrationBatch> IntegrationBatches =>
        Set<IntegrationBatch>();

    public DbSet<IntegrationOrderSnapshot> IntegrationOrderSnapshots =>
        Set<IntegrationOrderSnapshot>();

    public DbSet<IntegrationOrderLine> IntegrationOrderLines =>
        Set<IntegrationOrderLine>();

    public DbSet<IntegrationStep> IntegrationSteps => Set<IntegrationStep>();

    public DbSet<IntegrationAttempt> IntegrationAttempts =>
        Set<IntegrationAttempt>();

    public DbSet<ErpCustomerLink> ErpCustomerLinks => Set<ErpCustomerLink>();

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
            typeof(IntegrationDbContext).Assembly);
    }
}
