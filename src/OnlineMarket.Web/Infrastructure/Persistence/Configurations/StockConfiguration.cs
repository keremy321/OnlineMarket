using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stocks", t =>
        {
            t.HasCheckConstraint("CK_Stocks_Quantity_NonNegative", "[Quantity] >= 0");
            t.HasCheckConstraint("CK_Stocks_ReorderLevel_NonNegative", "[ReorderLevel] >= 0");
        });

        builder.HasKey(s => s.ProductId);

        builder.Property(s => s.ReorderLevel)
            .HasDefaultValue(0);

        builder.Property(s => s.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.HasOne(s => s.Product)
            .WithOne(p => p.Stock)
            .HasForeignKey<Stock>(s => s.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => s.Quantity)
            .IncludeProperties(s => new { s.ProductId, s.ReorderLevel })
            .HasDatabaseName("IX_Stocks_Quantity");
    }
}
