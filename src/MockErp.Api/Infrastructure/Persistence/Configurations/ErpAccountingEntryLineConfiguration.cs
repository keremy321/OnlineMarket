using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpAccountingEntryLineConfiguration
    : IEntityTypeConfiguration<ErpAccountingEntryLine>
{
    public void Configure(EntityTypeBuilder<ErpAccountingEntryLine> builder)
    {
        builder.ToTable(
            "ErpAccountingEntryLines",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntryLines_Sequence_Range",
                    "[SequenceNumber] BETWEEN 1 AND 3");
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntryLines_Amounts_NonNegative",
                    "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntryLines_OneSided",
                    "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([DebitAmount] = 0 AND [CreditAmount] > 0)");
                table.HasCheckConstraint(
                    "CK_ErpAccountingEntryLines_AccountCode_V1",
                    "[AccountCode] IN ('120','600','391')");
            });

        builder.HasKey(line => line.Id)
            .HasName("PK_ErpAccountingEntryLines");

        builder.Property(line => line.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(line => line.AccountingEntryId)
            .HasColumnType("uniqueidentifier");

        builder.Property(line => line.SequenceNumber)
            .HasColumnType("tinyint");

        builder.Property(line => line.AccountCode)
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(line => line.AccountName)
            .HasColumnType("nvarchar(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(line => line.ErpCustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(line => line.DebitAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);

        builder.Property(line => line.CreditAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);

        builder.Property(line => line.Description)
            .HasColumnType("nvarchar(500)")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(line => line.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasOne(line => line.AccountingEntry)
            .WithMany(entry => entry.Lines)
            .HasForeignKey(line => line.AccountingEntryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(line => line.ErpCustomer)
            .WithMany(customer => customer.AccountingEntryLines)
            .HasForeignKey(line => line.ErpCustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(line => new
            {
                line.AccountingEntryId,
                line.SequenceNumber
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpAccountingEntryLines_Entry_Sequence");

        builder.HasIndex(line => new
            {
                line.AccountingEntryId,
                line.AccountCode
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpAccountingEntryLines_Entry_Account");

        builder.HasIndex(line => new
            {
                line.AccountCode,
                line.CreatedAtUtc
            })
            .HasDatabaseName(
                "IX_ErpAccountingEntryLines_AccountCode_CreatedAtUtc");

        builder.HasIndex(line => new
            {
                line.ErpCustomerId,
                line.CreatedAtUtc
            })
            .HasFilter("[ErpCustomerId] IS NOT NULL")
            .HasDatabaseName(
                "IX_ErpAccountingEntryLines_ErpCustomerId_CreatedAtUtc");
    }
}
