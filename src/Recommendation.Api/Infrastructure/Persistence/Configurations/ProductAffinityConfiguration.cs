using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class ProductAffinityConfiguration : IEntityTypeConfiguration<ProductAffinity>
{
    public void Configure(EntityTypeBuilder<ProductAffinity> builder)
    {
        builder.ToTable(
            "ProductAffinities",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ProductAffinities_DifferentProducts",
                    "[SourceProductId] <> [RecommendedProductId]");
                table.HasCheckConstraint(
                    "CK_ProductAffinities_Counts_NonNegative",
                    "[CoOccurrenceCount] >= 0 AND [SourceOrderCount] >= 0 AND [RecommendedOrderCount] >= 0 AND [TotalOrderCount] >= 0");
                table.HasCheckConstraint(
                    "CK_ProductAffinities_Support_Range",
                    "[Support] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_ProductAffinities_Confidence_Range",
                    "[Confidence] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_ProductAffinities_Lift_Positive",
                    "[Lift] > 0");
            });

        builder.HasKey(affinity => new
            {
                affinity.SourceProductId,
                affinity.RecommendedProductId
            })
            .HasName("PK_ProductAffinities");

        builder.Property(affinity => affinity.SourceProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(affinity => affinity.RecommendedProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(affinity => affinity.CoOccurrenceCount)
            .HasColumnType("int");

        builder.Property(affinity => affinity.SourceOrderCount)
            .HasColumnType("int");

        builder.Property(affinity => affinity.RecommendedOrderCount)
            .HasColumnType("int");

        builder.Property(affinity => affinity.TotalOrderCount)
            .HasColumnType("int");

        builder.Property(affinity => affinity.Support)
            .HasColumnType("decimal(12,6)");

        builder.Property(affinity => affinity.Confidence)
            .HasColumnType("decimal(12,6)");

        builder.Property(affinity => affinity.Lift)
            .HasColumnType("decimal(12,6)");

        builder.Property(affinity => affinity.Score)
            .HasColumnType("decimal(12,6)");

        builder.Property(affinity => affinity.RunId)
            .HasColumnType("uniqueidentifier");

        builder.Property(affinity => affinity.CalculatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(affinity => affinity.SourceProduct)
            .WithMany(product => product.SourceAffinities)
            .HasForeignKey(affinity => affinity.SourceProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductAffinities_ProductSnapshots_SourceProductId");

        builder.HasOne(affinity => affinity.RecommendedProduct)
            .WithMany(product => product.RecommendedAffinities)
            .HasForeignKey(affinity => affinity.RecommendedProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductAffinities_ProductSnapshots_RecommendedProductId");

        builder.HasOne(affinity => affinity.Run)
            .WithMany(run => run.ProductAffinities)
            .HasForeignKey(affinity => affinity.RunId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductAffinities_RecommendationRuns_RunId");

        builder.HasIndex(affinity => new
            {
                affinity.SourceProductId,
                affinity.Score
            })
            .IsDescending(false, true)
            .IncludeProperties(affinity => new
            {
                affinity.RecommendedProductId,
                affinity.Confidence,
                affinity.Lift
            })
            .HasDatabaseName("IX_ProductAffinities_SourceProductId_Score");
    }
}
