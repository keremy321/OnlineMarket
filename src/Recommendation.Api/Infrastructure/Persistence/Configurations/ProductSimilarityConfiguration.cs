using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class ProductSimilarityConfiguration : IEntityTypeConfiguration<ProductSimilarity>
{
    public void Configure(EntityTypeBuilder<ProductSimilarity> builder)
    {
        builder.ToTable(
            "ProductSimilarities",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_DifferentProducts",
                    "[SourceProductId] <> [RecommendedProductId]");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_CategoryScore",
                    "[CategoryScore] IN (0, 0.20, 0.65)");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_BrandScore",
                    "[BrandScore] IN (0, 0.10)");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_PriceScore_Range",
                    "[PriceScore] BETWEEN 0 AND 0.15");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_AmountScore_Range",
                    "[AmountScore] BETWEEN 0 AND 0.10");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_Total_Range",
                    "[SimilarityScore] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_ProductSimilarities_Total_EqualsComponents",
                    "[SimilarityScore] = [CategoryScore] + [BrandScore] + [PriceScore] + [AmountScore]");
            });

        builder.HasKey(similarity => new
            {
                similarity.SourceProductId,
                similarity.RecommendedProductId
            })
            .HasName("PK_ProductSimilarities");

        builder.Property(similarity => similarity.SourceProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(similarity => similarity.RecommendedProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(similarity => similarity.CategoryScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(similarity => similarity.BrandScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(similarity => similarity.PriceScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(similarity => similarity.AmountScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(similarity => similarity.SimilarityScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(similarity => similarity.RunId)
            .HasColumnType("uniqueidentifier");

        builder.Property(similarity => similarity.CalculatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(similarity => similarity.SourceProduct)
            .WithMany(product => product.SourceSimilarities)
            .HasForeignKey(similarity => similarity.SourceProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductSimilarities_ProductSnapshots_SourceProductId");

        builder.HasOne(similarity => similarity.RecommendedProduct)
            .WithMany(product => product.RecommendedSimilarities)
            .HasForeignKey(similarity => similarity.RecommendedProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductSimilarities_ProductSnapshots_RecommendedProductId");

        builder.HasOne(similarity => similarity.Run)
            .WithMany(run => run.ProductSimilarities)
            .HasForeignKey(similarity => similarity.RunId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductSimilarities_RecommendationRuns_RunId");

        builder.HasIndex(similarity => new
            {
                similarity.SourceProductId,
                similarity.SimilarityScore
            })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ProductSimilarities_SourceProductId_SimilarityScore");
    }
}
