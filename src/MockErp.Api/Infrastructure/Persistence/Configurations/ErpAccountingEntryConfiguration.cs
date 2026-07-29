using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpAccountingEntryConfiguration
    : IEntityTypeConfiguration<ErpAccountingEntry>
{
    public void Configure(EntityTypeBuilder<ErpAccountingEntry> builder)
    {
        builder.ToTable(
            "ErpAccountingEntries",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntries_Amounts_NonNegative",
                    "[TotalDebit] >= 0 AND [TotalCredit] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntries_Balanced",
                    "[TotalDebit] = [TotalCredit]");
            });

        builder.HasKey(entry => entry.Id)
            .HasName("PK_ErpAccountingEntries");

        builder.Property(entry => entry.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(entry => entry.ErpVoucherNumber)
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(entry => entry.VoucherType)
            .HasConversion<byte>()
            .HasColumnType("tinyint")
            .HasDefaultValue(
                Domain.Enums.AccountingVoucherType.SalesInvoice);

        builder.Property(entry => entry.ErpOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(entry => entry.ExternalOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(entry => entry.ErpCustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(entry => entry.PaymentMethod)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(entry => entry.EntryDateUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(entry => entry.TotalDebit)
            .HasColumnType("decimal(18,2)");

        builder.Property(entry => entry.TotalCredit)
            .HasColumnType("decimal(18,2)");

        builder.Property(entry => entry.Currency)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(entry => entry.Description)
            .HasColumnType("nvarchar(500)")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(entry => entry.ErpOrder)
            .WithOne(order => order.AccountingEntry)
            .HasForeignKey<ErpAccountingEntry>(entry => entry.ErpOrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entry => entry.ErpCustomer)
            .WithMany(customer => customer.AccountingEntries)
            .HasForeignKey(entry => entry.ErpCustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(entry => entry.ErpVoucherNumber)
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpAccountingEntries_ErpVoucherNumber");

        builder.HasIndex(entry => entry.ErpOrderId)
            .IsUnique()
            .HasDatabaseName("UX_ErpAccountingEntries_ErpOrderId");

        builder.HasIndex(entry => entry.ExternalOrderId)
            .IsUnique()
            .HasDatabaseName("UX_ErpAccountingEntries_ExternalOrderId");

        builder.HasIndex(entry => new
            {
                entry.ErpCustomerId,
                entry.EntryDateUtc
            })
            .HasDatabaseName(
                "IX_ErpAccountingEntries_ErpCustomerId_EntryDateUtc");
    }
}
