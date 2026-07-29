using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpOrderConfiguration : IEntityTypeConfiguration<ErpOrder>
{
    public void Configure(EntityTypeBuilder<ErpOrder> builder)
    {
        builder.ToTable(
            "ErpOrders",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpOrders_Totals_NonNegative",
                    "[Subtotal] >= 0 AND [VatTotal] >= 0 AND [GrandTotal] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpOrders_GrandTotal",
                    "[GrandTotal] = [Subtotal] + [VatTotal]");
            });

        builder.HasKey(order => order.Id)
            .HasName("PK_ErpOrders");

        builder.Property(order => order.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(order => order.ErpOrderNumber)
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(order => order.ExternalOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(order => order.MarketOrderNumber)
            .HasColumnType("nvarchar(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.ErpCustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(order => order.OrderPlacedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(order => order.PaymentMethod)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(order => order.Subtotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(order => order.VatTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(order => order.GrandTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(order => order.Currency)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(order => order.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(order => order.ErpCustomer)
            .WithMany(customer => customer.Orders)
            .HasForeignKey(order => order.ErpCustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(order => order.ErpOrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_ErpOrders_ErpOrderNumber");

        builder.HasIndex(order => order.ExternalOrderId)
            .IsUnique()
            .HasDatabaseName("UX_ErpOrders_ExternalOrderId");

        builder.HasIndex(order => order.MarketOrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_ErpOrders_MarketOrderNumber");

        builder.HasIndex(order => new
            {
                order.ErpCustomerId,
                order.CreatedAtUtc
            })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ErpOrders_ErpCustomerId_CreatedAtUtc");
    }
}
