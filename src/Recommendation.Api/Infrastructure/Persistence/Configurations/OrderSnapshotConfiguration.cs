using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class OrderSnapshotConfiguration : IEntityTypeConfiguration<OrderSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderSnapshot> builder)
    {
        builder.ToTable(
            "OrderSnapshots",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_OrderSnapshots_TotalQuantity_Positive",
                    "[TotalQuantity] > 0");
                table.HasCheckConstraint(
                    "CK_OrderSnapshots_DistinctProductCount_Positive",
                    "[DistinctProductCount] > 0");
            });

        builder.HasKey(order => order.OrderId)
            .HasName("PK_OrderSnapshots");

        builder.Property(order => order.OrderId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(order => order.OrderNumber)
            .HasColumnType("nvarchar(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.CustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(order => order.OccurredAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(order => order.TotalQuantity)
            .HasColumnType("int");

        builder.Property(order => order.DistinctProductCount)
            .HasColumnType("int");

        builder.Property(order => order.CorrelationId)
            .HasColumnType("uniqueidentifier");

        builder.Property(order => order.ReceivedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasIndex(order => order.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_OrderSnapshots_OrderNumber");

        builder.HasIndex(order => order.CorrelationId)
            .IsUnique()
            .HasDatabaseName("UX_OrderSnapshots_CorrelationId");

        builder.HasIndex(order => new
            {
                order.CustomerId,
                order.OccurredAtUtc
            })
            .IsDescending(false, true)
            .HasDatabaseName("IX_OrderSnapshots_CustomerId_OccurredAtUtc");

        builder.HasIndex(order => order.OccurredAtUtc)
            .HasDatabaseName("IX_OrderSnapshots_OccurredAtUtc");
    }
}
