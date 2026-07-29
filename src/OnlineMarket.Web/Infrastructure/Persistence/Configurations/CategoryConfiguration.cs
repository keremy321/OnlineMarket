using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", t =>
        {
            t.HasCheckConstraint("CK_Categories_NotSelfParent", "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(c => c.Slug)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.DisplayOrder)
            .HasDefaultValue(0);

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Categories_Slug");

        builder.HasIndex(c => new { c.ParentCategoryId, c.IsActive, c.DisplayOrder })
            .HasDatabaseName("IX_Categories_ParentCategoryId_IsActive_DisplayOrder");
    }
}
