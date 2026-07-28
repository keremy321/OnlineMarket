using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("Brands");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(b => b.Slug)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(b => b.LogoUrl)
            .HasMaxLength(500);

        builder.Property(b => b.IsActive)
            .HasDefaultValue(true);

        builder.Property(b => b.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(b => b.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(b => b.RowVersion)
            .IsRowVersion();

        builder.HasIndex(b => b.Name)
            .IsUnique()
            .HasDatabaseName("UX_Brands_Name");

        builder.HasIndex(b => b.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Brands_Slug");

        builder.HasIndex(b => new { b.IsActive, b.Name })
            .HasDatabaseName("IX_Brands_IsActive_Name");
    }
}
