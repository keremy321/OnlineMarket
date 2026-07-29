using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class IntegrationStepConfiguration
    : IEntityTypeConfiguration<IntegrationStep>
{
    public void Configure(EntityTypeBuilder<IntegrationStep> builder)
    {
        builder.ToTable(
            "IntegrationSteps",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntegrationSteps_Sequence_Range",
                    "[SequenceNumber] BETWEEN 1 AND 4");
                table.HasCheckConstraint(
                    "CK_IntegrationSteps_Attempts_Range",
                    "[AttemptCount] >= 0 AND [MaxAttempts] > 0 AND [AttemptCount] <= [MaxAttempts]");
            });

        builder.HasKey(step => step.Id)
            .HasName("PK_IntegrationSteps");

        builder.Property(step => step.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(step => step.BatchId)
            .HasColumnType("uniqueidentifier");

        builder.Property(step => step.StepType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(step => step.SequenceNumber)
            .HasColumnType("tinyint");

        builder.Property(step => step.Status)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(step => step.IdempotencyKey)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(step => step.AttemptCount)
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(step => step.MaxAttempts)
            .HasColumnType("int")
            .HasDefaultValue(5);

        builder.Property(step => step.NextAttemptAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(step => step.LockedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(step => step.LockedBy)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(step => step.StartedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(step => step.CompletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(step => step.ExternalReference)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(step => step.LastHttpStatusCode)
            .HasColumnType("smallint");

        builder.Property(step => step.LastErrorType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(step => step.LastErrorCode)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(step => step.LastErrorMessage)
            .HasColumnType("nvarchar(1000)")
            .HasMaxLength(1000);

        builder.Property(step => step.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasOne(step => step.Batch)
            .WithMany(batch => batch.Steps)
            .HasForeignKey(step => step.BatchId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_IntegrationSteps_IntegrationBatches_BatchId");

        builder.HasIndex(step => new
            {
                step.BatchId,
                step.StepType
            })
            .IsUnique()
            .HasDatabaseName("UX_IntegrationSteps_BatchId_StepType");

        builder.HasIndex(step => new
            {
                step.BatchId,
                step.SequenceNumber
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_IntegrationSteps_BatchId_SequenceNumber");

        builder.HasIndex(step => step.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationSteps_IdempotencyKey");

        builder.HasIndex(step => new
            {
                step.Status,
                step.NextAttemptAtUtc,
                step.SequenceNumber
            })
            .HasDatabaseName(
                "IX_IntegrationSteps_Status_NextAttemptAtUtc_SequenceNumber");
    }
}
