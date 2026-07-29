using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpOrderLineConfiguration
    : IEntityTypeConfiguration<ErpOrderLine>
{
    public void Configure(EntityTypeBuilder<ErpOrderLine> builder)
    {
        builder.ToTable(
            "ErpOrderLines",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpOrderLines_Quantity_Positive",
                    "[Quantity] > 0");
                table.HasCheckConstraint(
                    "CK_ErpOrderLines_VatRate_Range",
                    "[VatRate] BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "CK_ErpOrderLines_Amounts_NonNegative",
                    "[UnitPrice] >= 0 AND [NetLineAmount] >= 0 AND [VatAmount] >= 0 AND [LineTotal] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpOrderLines_LineTotal",
                    "[LineTotal] = [NetLineAmount] + [VatAmount]");
            });

        builder.HasKey(line => line.Id)
            .HasName("PK_ErpOrderLines");

        builder.Property(line => line.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(line => line.ErpOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(line => line.ExternalProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(line => line.Sku)
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(line => line.ProductName)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasColumnType("int");

        builder.Property(line => line.UnitPrice)
            .HasColumnType("decimal(18,2)");

        builder.Property(line => line.VatRate)
            .HasColumnType("decimal(5,2)");

        builder.Property(line => line.NetLineAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(line => line.VatAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(line => line.LineTotal)
            .HasColumnType("decimal(18,2)");

        builder.HasOne(line => line.ErpOrder)
            .WithMany(order => order.Lines)
            .HasForeignKey(line => line.ErpOrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(line => new
            {
                line.ErpOrderId,
                line.ExternalProductId
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpOrderLines_ErpOrderId_ExternalProductId");

        builder.HasIndex(line => line.ExternalProductId)
            .HasDatabaseName("IX_ErpOrderLines_ExternalProductId");
    }
}
