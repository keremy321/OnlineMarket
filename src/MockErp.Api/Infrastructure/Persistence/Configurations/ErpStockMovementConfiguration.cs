using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpStockMovementConfiguration
    : IEntityTypeConfiguration<ErpStockMovement>
{
    public void Configure(EntityTypeBuilder<ErpStockMovement> builder)
    {
        builder.ToTable(
            "ErpStockMovements",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpStockMovements_QuantityChange_NotZero",
                    "[QuantityChange] <> 0");
                table.HasCheckConstraint(
                    "CK_ErpStockMovements_Quantities_NonNegative",
                    "[PreviousQuantity] >= 0 AND [NewQuantity] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpStockMovements_Balance",
                    "[PreviousQuantity] + [QuantityChange] = [NewQuantity]");
            });

        builder.HasKey(movement => movement.Id)
            .HasName("PK_ErpStockMovements");

        builder.Property(movement => movement.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(movement => movement.ErpOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(movement => movement.ExternalOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(movement => movement.ExternalProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(movement => movement.Sku)
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(movement => movement.QuantityChange)
            .HasColumnType("int");

        builder.Property(movement => movement.PreviousQuantity)
            .HasColumnType("int");

        builder.Property(movement => movement.NewQuantity)
            .HasColumnType("int");

        builder.Property(movement => movement.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(movement => movement.ErpOrder)
            .WithMany(order => order.StockMovements)
            .HasForeignKey(movement => movement.ErpOrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(movement => new
            {
                movement.ExternalOrderId,
                movement.ExternalProductId
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpStockMovements_ExternalOrderId_ExternalProductId");

        builder.HasIndex(movement => movement.ErpOrderId)
            .HasDatabaseName("IX_ErpStockMovements_ErpOrderId");

        builder.HasIndex(movement => new
            {
                movement.ExternalProductId,
                movement.CreatedAtUtc
            })
            .HasDatabaseName(
                "IX_ErpStockMovements_ExternalProductId_CreatedAtUtc");
    }
}
