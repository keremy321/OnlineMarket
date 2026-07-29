using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class IntegrationOrderSnapshotConfiguration
    : IEntityTypeConfiguration<IntegrationOrderSnapshot>
{
    public void Configure(EntityTypeBuilder<IntegrationOrderSnapshot> builder)
    {
        builder.ToTable(
            "IntegrationOrderSnapshots",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntegrationOrderSnapshots_Totals_NonNegative",
                    "[Subtotal] >= 0 AND [VatTotal] >= 0 AND [GrandTotal] >= 0");
                table.HasCheckConstraint(
                    "CK_IntegrationOrderSnapshots_GrandTotal",
                    "[GrandTotal] = [Subtotal] + [VatTotal]");
            });

        builder.HasKey(snapshot => snapshot.BatchId)
            .HasName("PK_IntegrationOrderSnapshots");

        builder.Property(snapshot => snapshot.BatchId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(snapshot => snapshot.CustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(snapshot => snapshot.FirstName)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(snapshot => snapshot.LastName)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(snapshot => snapshot.RecipientName)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(snapshot => snapshot.Email)
            .HasColumnType("nvarchar(256)")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(snapshot => snapshot.PhoneNumber)
            .HasColumnType("nvarchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(snapshot => snapshot.AddressLine1)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(snapshot => snapshot.AddressLine2)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250);

        builder.Property(snapshot => snapshot.District)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(snapshot => snapshot.City)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(snapshot => snapshot.PostalCode)
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20);

        builder.Property(snapshot => snapshot.CountryCode)
            .HasColumnType("char(2)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(snapshot => snapshot.OrderPlacedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(snapshot => snapshot.PaymentMethod)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(snapshot => snapshot.Subtotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(snapshot => snapshot.VatTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(snapshot => snapshot.GrandTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(snapshot => snapshot.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3)
            .IsRequired();

        builder.HasOne(snapshot => snapshot.Batch)
            .WithOne(batch => batch.OrderSnapshot)
            .HasForeignKey<IntegrationOrderSnapshot>(
                snapshot => snapshot.BatchId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_IntegrationOrderSnapshots_IntegrationBatches_BatchId");
    }
}
