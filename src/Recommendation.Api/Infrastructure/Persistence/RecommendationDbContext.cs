using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence;

public sealed class RecommendationDbContext(DbContextOptions<RecommendationDbContext> options)
    : DbContext(options)
{
    public DbSet<ProductSnapshot> ProductSnapshots => Set<ProductSnapshot>();

    public DbSet<OrderSnapshot> OrderSnapshots => Set<OrderSnapshot>();

    public DbSet<OrderSnapshotItem> OrderSnapshotItems => Set<OrderSnapshotItem>();

    public DbSet<ProductAffinity> ProductAffinities => Set<ProductAffinity>();

    public DbSet<ProductSimilarity> ProductSimilarities => Set<ProductSimilarity>();

    public DbSet<ProductPopularity> ProductPopularity => Set<ProductPopularity>();

    public DbSet<CustomerPreferenceScore> CustomerPreferenceScores => Set<CustomerPreferenceScore>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public DbSet<RecommendationRun> RecommendationRuns => Set<RecommendationRun>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecommendationDbContext).Assembly);
    }
}
