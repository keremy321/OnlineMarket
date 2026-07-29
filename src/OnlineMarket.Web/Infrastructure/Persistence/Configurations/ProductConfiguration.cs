using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_Price_NonNegative", "[Price] >= 0");
            t.HasCheckConstraint("CK_Products_VatRate_Range", "[VatRate] >= 0 AND [VatRate] <= 100");
            t.HasCheckConstraint("CK_Products_NetContent_Positive", "[NetContent] > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Slug)
            .IsRequired()
            .HasMaxLength(240);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.Price)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.VatRate)
            .HasColumnType("decimal(5,2)");

        builder.Property(p => p.NetContent)
            .HasColumnType("decimal(12,3)");

        builder.Property(p => p.UnitType)
            .HasColumnType("tinyint");

        builder.Property(p => p.ImageUrl)
            .HasMaxLength(500);

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(p => p.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasDatabaseName("UX_Products_Sku");

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Products_Slug");

        builder.HasIndex(p => new { p.CategoryId, p.IsActive, p.Name })
            .HasDatabaseName("IX_Products_CategoryId_IsActive_Name");

        builder.HasIndex(p => new { p.BrandId, p.IsActive, p.Name })
            .HasDatabaseName("IX_Products_BrandId_IsActive_Name");

        builder.HasIndex(p => new { p.IsActive, p.Price })
            .HasDatabaseName("IX_Products_IsActive_Price");

        builder.HasIndex(p => new { p.CategoryId, p.IsActive, p.Name })
            .IncludeProperties(p => new { p.Price, p.BrandId, p.ImageUrl })
            .HasDatabaseName("IX_Products_CategoryId_IsActive_Name_Covering");
    }
}
