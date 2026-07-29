using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Status)
            .HasColumnType("tinyint");

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasOne(c => c.Customer)
            .WithMany(cust => cust.Carts)
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(c => c.CustomerId)
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_Carts_ActiveCustomer");

        builder.HasIndex(c => new { c.CustomerId, c.Status, c.UpdatedAtUtc })
            .HasDatabaseName("IX_Carts_CustomerId_Status_UpdatedAtUtc");
    }
}
