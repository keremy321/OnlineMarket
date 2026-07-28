using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Amount_NonNegative", "[Amount] >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Method)
            .HasColumnType("tinyint");

        builder.Property(p => p.Status)
            .HasColumnType("tinyint");

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.SimulationReference)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.ProcessedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(p => p.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_Payments_OrderId");

        builder.HasIndex(p => p.SimulationReference)
            .IsUnique()
            .HasDatabaseName("UX_Payments_SimulationReference");
    }
}
