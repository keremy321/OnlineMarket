using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", t =>
        {
            t.HasCheckConstraint("CK_StockMovements_QuantityChange_NotZero", "[QuantityChange] <> 0");
            t.HasCheckConstraint("CK_StockMovements_Quantities_NonNegative", "[PreviousQuantity] >= 0 AND [NewQuantity] >= 0");
            t.HasCheckConstraint("CK_StockMovements_Balance", "[PreviousQuantity] + [QuantityChange] = [NewQuantity]");
        });

        builder.HasKey(sm => sm.Id);
        builder.Property(sm => sm.Id).UseIdentityColumn(1, 1);

        builder.Property(sm => sm.MovementType)
            .HasColumnType("tinyint");

        builder.Property(sm => sm.ReferenceType)
            .HasColumnType("tinyint");

        builder.Property(sm => sm.Description)
            .HasMaxLength(500);

        builder.Property(sm => sm.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(sm => sm.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(sm => sm.CreatedByUser)
            .WithMany()
            .HasForeignKey(sm => sm.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(sm => new { sm.ProductId, sm.CreatedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_StockMovements_ProductId_CreatedAtUtc");

        builder.HasIndex(sm => new { sm.ReferenceType, sm.ReferenceId })
            .HasDatabaseName("IX_StockMovements_ReferenceType_ReferenceId");
    }
}
