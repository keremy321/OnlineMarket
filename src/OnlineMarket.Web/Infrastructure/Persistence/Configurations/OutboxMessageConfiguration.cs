using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", t =>
        {
            t.HasCheckConstraint("CK_OutboxMessages_Payload_IsJson", "ISJSON([Payload]) = 1");
            t.HasCheckConstraint("CK_OutboxMessages_AttemptCount_NonNegative", "[AttemptCount] >= 0");
        });

        builder.HasKey(om => om.Id);
        builder.Property(om => om.Id).UseIdentityColumn(1, 1);

        builder.Property(om => om.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(om => om.Destination)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(om => om.AggregateType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(om => om.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(om => om.Status)
            .HasColumnType("tinyint")
            .HasDefaultValue(Domain.Enums.OutboxStatus.Pending);

        builder.Property(om => om.OccurredAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(om => om.AvailableAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(om => om.ProcessedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(om => om.AttemptCount)
            .HasDefaultValue(0);

        builder.Property(om => om.NextAttemptAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(om => om.LockedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(om => om.LockedBy)
            .HasMaxLength(100);

        builder.Property(om => om.LastErrorCode)
            .HasMaxLength(100);

        builder.Property(om => om.LastError)
            .HasMaxLength(2000);

        builder.Property(om => om.RowVersion)
            .IsRowVersion();

        builder.HasIndex(om => om.EventId)
            .IsUnique()
            .HasDatabaseName("UX_OutboxMessages_EventId");

        builder.HasIndex(om => new { om.Status, om.AvailableAtUtc, om.NextAttemptAtUtc })
            .IncludeProperties(om => new { om.EventId, om.Destination, om.AttemptCount })
            .HasDatabaseName("IX_OutboxMessages_Status_AvailableAtUtc_NextAttemptAtUtc");

        builder.HasIndex(om => new { om.AggregateType, om.AggregateId })
            .HasDatabaseName("IX_OutboxMessages_AggregateType_AggregateId");

        builder.HasIndex(om => om.CorrelationId)
            .HasDatabaseName("IX_OutboxMessages_CorrelationId");
    }
}
