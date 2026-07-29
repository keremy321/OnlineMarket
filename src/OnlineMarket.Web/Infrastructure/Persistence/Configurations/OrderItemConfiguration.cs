using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderItems_Amounts_NonNegative", "[UnitPrice] >= 0 AND [VatRate] >= 0 AND [VatRate] <= 100 AND [NetLineAmount] >= 0 AND [VatAmount] >= 0 AND [LineTotal] >= 0");
            t.HasCheckConstraint("CK_OrderItems_LineTotal", "[LineTotal] = [NetLineAmount] + [VatAmount]");
        });

        builder.HasKey(oi => oi.Id);
        builder.Property(oi => oi.Id).ValueGeneratedNever();

        builder.Property(oi => oi.ProductNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oi => oi.SkuSnapshot)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(oi => oi.UnitPrice)
            .HasColumnType("decimal(18,2)");

        builder.Property(oi => oi.VatRate)
            .HasColumnType("decimal(5,2)");

        builder.Property(oi => oi.NetLineAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(oi => oi.VatAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(oi => oi.LineTotal)
            .HasColumnType("decimal(18,2)");

        builder.HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(oi => new { oi.OrderId, oi.ProductId })
            .IsUnique()
            .HasDatabaseName("UX_OrderItems_OrderId_ProductId");

        builder.HasIndex(oi => new { oi.ProductId, oi.OrderId })
            .HasDatabaseName("IX_OrderItems_ProductId_OrderId");
    }
}
