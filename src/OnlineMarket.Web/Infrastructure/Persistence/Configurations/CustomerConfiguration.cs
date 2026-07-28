using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasOne(c => c.User)
            .WithOne(u => u.Customer)
            .HasForeignKey<Customer>(c => c.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasDatabaseName("UX_Customers_UserId");

        builder.HasIndex(c => c.IsActive)
            .HasDatabaseName("IX_Customers_IsActive");
    }
}
