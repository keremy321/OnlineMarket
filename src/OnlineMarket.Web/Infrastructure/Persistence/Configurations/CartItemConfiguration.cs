using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", t =>
        {
            t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_CartItems_Price_NonNegative", "[LastKnownUnitPrice] >= 0");
        });

        builder.HasKey(ci => ci.Id);
        builder.Property(ci => ci.Id).ValueGeneratedNever();

        builder.Property(ci => ci.LastKnownUnitPrice)
            .HasColumnType("decimal(18,2)");

        builder.Property(ci => ci.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(ci => ci.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(ci => ci.RowVersion)
            .IsRowVersion();

        builder.HasOne(ci => ci.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ci => ci.Product)
            .WithMany(p => p.CartItems)
            .HasForeignKey(ci => ci.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(ci => new { ci.CartId, ci.ProductId })
            .IsUnique()
            .HasDatabaseName("UX_CartItems_CartId_ProductId");
    }
}
