using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class ProductPopularityConfiguration : IEntityTypeConfiguration<ProductPopularity>
{
    public void Configure(EntityTypeBuilder<ProductPopularity> builder)
    {
        builder.ToTable(
            "ProductPopularity",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ProductPopularity_Window",
                    "[WindowStartUtc] < [WindowEndUtc]");
                table.HasCheckConstraint(
                    "CK_ProductPopularity_Counts_NonNegative",
                    "[SoldQuantity] >= 0 AND [OrderCount] >= 0");
                table.HasCheckConstraint(
                    "CK_ProductPopularity_Score_Range",
                    "[Score] BETWEEN 0 AND 1");
            });

        builder.HasKey(popularity => popularity.ProductId)
            .HasName("PK_ProductPopularity");

        builder.Property(popularity => popularity.ProductId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(popularity => popularity.WindowStartUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(popularity => popularity.WindowEndUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(popularity => popularity.SoldQuantity)
            .HasColumnType("int");

        builder.Property(popularity => popularity.OrderCount)
            .HasColumnType("int");

        builder.Property(popularity => popularity.Score)
            .HasColumnType("decimal(12,6)");

        builder.Property(popularity => popularity.RunId)
            .HasColumnType("uniqueidentifier");

        builder.Property(popularity => popularity.CalculatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(popularity => popularity.Product)
            .WithOne(product => product.Popularity)
            .HasForeignKey<ProductPopularity>(popularity => popularity.ProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductPopularity_ProductSnapshots_ProductId");

        builder.HasOne(popularity => popularity.Run)
            .WithMany(run => run.ProductPopularity)
            .HasForeignKey(popularity => popularity.RunId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductPopularity_RecommendationRuns_RunId");

        builder.HasIndex(popularity => popularity.Score)
            .IsDescending()
            .IncludeProperties(popularity => new
            {
                popularity.ProductId,
                popularity.SoldQuantity,
                popularity.OrderCount
            })
            .HasDatabaseName("IX_ProductPopularity_Score");
    }
}
