using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class ProductSnapshotConfiguration : IEntityTypeConfiguration<ProductSnapshot>
{
    public void Configure(EntityTypeBuilder<ProductSnapshot> builder)
    {
        builder.ToTable(
            "ProductSnapshots",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ProductSnapshots_Price_NonNegative",
                    "[Price] >= 0");
                table.HasCheckConstraint(
                    "CK_ProductSnapshots_NetContent_Positive",
                    "[NetContent] > 0");
            });

        builder.HasKey(product => product.ProductId)
            .HasName("PK_ProductSnapshots");

        builder.Property(product => product.ProductId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(product => product.Sku)
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(product => product.Name)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnType("nvarchar(2000)")
            .HasMaxLength(2000);

        builder.Property(product => product.CategoryId)
            .HasColumnType("uniqueidentifier");

        builder.Property(product => product.ParentCategoryId)
            .HasColumnType("uniqueidentifier");

        builder.Property(product => product.BrandId)
            .HasColumnType("uniqueidentifier");

        builder.Property(product => product.Price)
            .HasColumnType("decimal(18,2)");

        builder.Property(product => product.NetContent)
            .HasColumnType("decimal(12,3)");

        builder.Property(product => product.UnitType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(product => product.IsActive)
            .HasColumnType("bit");

        builder.Property(product => product.IsInStock)
            .HasColumnType("bit");

        builder.Property(product => product.SourceUpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(product => product.ReceivedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(product => product.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(product => product.Sku)
            .IsUnique()
            .HasDatabaseName("UX_ProductSnapshots_Sku");

        builder.HasIndex(product => new
            {
                product.CategoryId,
                product.IsActive,
                product.IsInStock
            })
            .HasDatabaseName("IX_ProductSnapshots_CategoryId_IsActive_IsInStock");

        builder.HasIndex(product => new
            {
                product.BrandId,
                product.IsActive,
                product.IsInStock
            })
            .HasDatabaseName("IX_ProductSnapshots_BrandId_IsActive_IsInStock");
    }
}
