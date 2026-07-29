using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_Totals_NonNegative", "[Subtotal] >= 0 AND [VatTotal] >= 0 AND [GrandTotal] >= 0");
            t.HasCheckConstraint("CK_Orders_GrandTotal", "[GrandTotal] = [Subtotal] + [VatTotal]");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(o => o.Status)
            .HasColumnType("tinyint");

        builder.Property(o => o.Subtotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.VatTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.GrandTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.Currency)
            .IsRequired()
            .HasColumnType("char(3)")
            .HasDefaultValue("TRY");

        builder.Property(o => o.PlacedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(o => o.RowVersion)
            .IsRowVersion();

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(o => o.SourceCart)
            .WithOne(c => c.ConvertedOrder)
            .HasForeignKey<Order>(o => o.SourceCartId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_Orders_OrderNumber");

        builder.HasIndex(o => o.SourceCartId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_SourceCartId");

        builder.HasIndex(o => o.CorrelationId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_CorrelationId");

        builder.HasIndex(o => new { o.CustomerId, o.PlacedAtUtc })
            .IsDescending(false, true)
            .IncludeProperties(o => new { o.OrderNumber, o.GrandTotal, o.Status })
            .HasDatabaseName("IX_Orders_CustomerId_PlacedAtUtc");

        builder.HasIndex(o => new { o.Status, o.PlacedAtUtc })
            .HasDatabaseName("IX_Orders_Status_PlacedAtUtc");
    }
}
