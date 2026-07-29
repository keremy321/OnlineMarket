using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpStockConfiguration : IEntityTypeConfiguration<ErpStock>
{
    public void Configure(EntityTypeBuilder<ErpStock> builder)
    {
        builder.ToTable(
            "ErpStocks",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpStocks_Quantity_NonNegative",
                    "[Quantity] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpStocks_ReorderLevel_NonNegative",
                    "[ReorderLevel] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpStocks_NetContent_Positive",
                    "[NetContent] > 0");
            });

        builder.HasKey(stock => stock.Id)
            .HasName("PK_ErpStocks");

        builder.Property(stock => stock.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(stock => stock.ExternalProductId)
            .HasColumnType("uniqueidentifier");

        builder.Property(stock => stock.Sku)
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(stock => stock.ProductName)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(stock => stock.UnitType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(stock => stock.NetContent)
            .HasColumnType("decimal(12,3)");

        builder.Property(stock => stock.Quantity)
            .HasColumnType("int");

        builder.Property(stock => stock.ReorderLevel)
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(stock => stock.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(stock => stock.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(stock => stock.ExternalProductId)
            .IsUnique()
            .HasDatabaseName("UX_ErpStocks_ExternalProductId");

        builder.HasIndex(stock => stock.Sku)
            .IsUnique()
            .HasDatabaseName("UX_ErpStocks_Sku");

        builder.HasIndex(stock => stock.Quantity)
            .HasDatabaseName("IX_ErpStocks_Quantity");
    }
}
