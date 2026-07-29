using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class IntegrationOrderLineConfiguration
    : IEntityTypeConfiguration<IntegrationOrderLine>
{
    public void Configure(EntityTypeBuilder<IntegrationOrderLine> builder)
    {
        builder.ToTable(
            "IntegrationOrderLines",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntegrationOrderLines_Quantity_Positive",
                    "[Quantity] > 0");
                table.HasCheckConstraint(
                    "CK_IntegrationOrderLines_VatRate_Range",
                    "[VatRate] BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "CK_IntegrationOrderLines_Amounts_NonNegative",
                    "[UnitPrice] >= 0 AND [NetLineAmount] >= 0 AND [VatAmount] >= 0 AND [LineTotal] >= 0");
                table.HasCheckConstraint(
                    "CK_IntegrationOrderLines_LineTotal",
                    "[LineTotal] = [NetLineAmount] + [VatAmount]");
            });

        builder.HasKey(line => line.Id)
            .HasName("PK_IntegrationOrderLines");

        builder.Property(line => line.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(line => line.BatchId)
            .HasColumnType("uniqueidentifier");

        builder.Property(line => line.ProductId)
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

        builder.HasOne(line => line.Batch)
            .WithMany(batch => batch.OrderLines)
            .HasForeignKey(line => line.BatchId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_IntegrationOrderLines_IntegrationBatches_BatchId");

        builder.HasIndex(line => new
            {
                line.BatchId,
                line.ProductId
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_IntegrationOrderLines_BatchId_ProductId");
    }
}
