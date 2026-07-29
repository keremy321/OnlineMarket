using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<ErpIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ErpIdempotencyRecord> builder)
    {
        builder.ToTable(
            "ErpIdempotencyRecords",
            table => table.HasCheckConstraint(
                "CK_ErpIdempotencyRecords_ResponseBody_IsJson",
                "ISJSON([ResponseBody]) = 1"));

        builder.HasKey(record => record.Id)
            .HasName("PK_ErpIdempotencyRecords");

        builder.Property(record => record.Id)
            .HasColumnType("bigint")
            .UseIdentityColumn();

        builder.Property(record => record.IdempotencyKey)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record => record.OperationType)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(record => record.RequestHash)
            .HasColumnType("char(64)")
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(record => record.ResponseStatusCode)
            .HasColumnType("smallint");

        builder.Property(record => record.ResponseBody)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(record => record.ResourceType)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(record => record.ResourceId)
            .HasColumnType("uniqueidentifier");

        builder.Property(record => record.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(record => record.LastAccessedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasIndex(record => record.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName(
                "UX_ErpIdempotencyRecords_IdempotencyKey");

        builder.HasIndex(record => new
            {
                record.OperationType,
                record.CreatedAtUtc
            })
            .HasDatabaseName(
                "IX_ErpIdempotencyRecords_OperationType_CreatedAtUtc");

        builder.HasIndex(record => new
            {
                record.ResourceType,
                record.ResourceId
            })
            .HasDatabaseName(
                "IX_ErpIdempotencyRecords_ResourceType_ResourceId");
    }
}
