using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class OrderSnapshotItemConfiguration : IEntityTypeConfiguration<OrderSnapshotItem>
{
    public void Configure(EntityTypeBuilder<OrderSnapshotItem> builder)
    {
        builder.ToTable(
            "OrderSnapshotItems",
            table => table.HasCheckConstraint(
                "CK_OrderSnapshotItems_Quantity_Positive",
                "[Quantity] > 0"));

        builder.HasKey(item => new
            {
                item.OrderId,
                item.ProductId
            })
            .HasName("PK_OrderSnapshotItems");

        builder.Property(item => item.OrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(item => item.ProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(item => item.Quantity)
            .HasColumnType("int");

        builder.HasOne(item => item.Order)
            .WithMany(order => order.Items)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OrderSnapshotItems_OrderSnapshots_OrderId");

        builder.HasOne(item => item.Product)
            .WithMany(product => product.OrderSnapshotItems)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OrderSnapshotItems_ProductSnapshots_ProductId");

        builder.HasIndex(item => new
            {
                item.ProductId,
                item.OrderId
            })
            .HasDatabaseName("IX_OrderSnapshotItems_ProductId_OrderId");
    }
}
