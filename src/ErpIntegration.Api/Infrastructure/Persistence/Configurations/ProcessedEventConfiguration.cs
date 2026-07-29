using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class ProcessedEventConfiguration
    : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");

        builder.HasKey(processedEvent => processedEvent.EventId)
            .HasName("PK_ProcessedEvents");

        builder.Property(processedEvent => processedEvent.EventId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(processedEvent => processedEvent.EventType)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(processedEvent => processedEvent.PayloadHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(processedEvent => processedEvent.CorrelationId)
            .HasColumnType("uniqueidentifier");

        builder.Property(processedEvent => processedEvent.ReceivedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(processedEvent => processedEvent.ProcessedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasIndex(processedEvent => processedEvent.CorrelationId)
            .HasDatabaseName("IX_ProcessedEvents_CorrelationId");
    }
}
