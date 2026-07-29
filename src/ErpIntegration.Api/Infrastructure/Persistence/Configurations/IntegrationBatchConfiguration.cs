using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class IntegrationBatchConfiguration
    : IEntityTypeConfiguration<IntegrationBatch>
{
    public void Configure(EntityTypeBuilder<IntegrationBatch> builder)
    {
        builder.ToTable("IntegrationBatches");

        builder.HasKey(batch => batch.Id)
            .HasName("PK_IntegrationBatches");

        builder.Property(batch => batch.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(batch => batch.EventId)
            .HasColumnType("uniqueidentifier");

        builder.Property(batch => batch.MarketOrderId)
            .HasColumnType("uniqueidentifier");

        builder.Property(batch => batch.OrderNumber)
            .HasColumnType("nvarchar(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(batch => batch.CustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(batch => batch.Status)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(batch => batch.CurrentStepType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(batch => batch.CorrelationId)
            .HasColumnType("uniqueidentifier");

        builder.Property(batch => batch.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(batch => batch.StartedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(batch => batch.CompletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(batch => batch.LastErrorCode)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(batch => batch.LastErrorMessage)
            .HasColumnType("nvarchar(1000)")
            .HasMaxLength(1000);

        builder.Property(batch => batch.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasOne(batch => batch.ProcessedEvent)
            .WithOne(processedEvent => processedEvent.Batch)
            .HasForeignKey<IntegrationBatch>(batch => batch.EventId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_IntegrationBatches_ProcessedEvents_EventId");

        builder.HasIndex(batch => batch.EventId)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationBatches_EventId");

        builder.HasIndex(batch => batch.MarketOrderId)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationBatches_MarketOrderId");

        builder.HasIndex(batch => batch.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationBatches_OrderNumber");

        builder.HasIndex(batch => batch.CorrelationId)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationBatches_CorrelationId");

        builder.HasIndex(batch => new
            {
                batch.Status,
                batch.CreatedAtUtc
            })
            .IsDescending(false, true)
            .HasDatabaseName("IX_IntegrationBatches_Status_CreatedAtUtc");

        builder.HasIndex(batch => new
            {
                batch.CustomerId,
                batch.CreatedAtUtc
            })
            .IsDescending(false, true)
            .HasDatabaseName("IX_IntegrationBatches_CustomerId_CreatedAtUtc");
    }
}
