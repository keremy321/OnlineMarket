using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class IntegrationAttemptConfiguration
    : IEntityTypeConfiguration<IntegrationAttempt>
{
    public void Configure(EntityTypeBuilder<IntegrationAttempt> builder)
    {
        builder.ToTable(
            "IntegrationAttempts",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntegrationAttempts_AttemptNumber_Positive",
                    "[AttemptNumber] > 0");
                table.HasCheckConstraint(
                    "CK_IntegrationAttempts_Duration_NonNegative",
                    "[DurationMs] IS NULL OR [DurationMs] >= 0");
                table.HasCheckConstraint(
                    "CK_IntegrationAttempts_RequestPayloadMasked_IsJson",
                    "[RequestPayloadMasked] IS NULL OR ISJSON([RequestPayloadMasked]) = 1");
                table.HasCheckConstraint(
                    "CK_IntegrationAttempts_ResponsePayloadMasked_IsJson",
                    "[ResponsePayloadMasked] IS NULL OR ISJSON([ResponsePayloadMasked]) = 1");
            });

        builder.HasKey(attempt => attempt.Id)
            .HasName("PK_IntegrationAttempts");

        builder.Property(attempt => attempt.Id)
            .HasColumnType("bigint")
            .UseIdentityColumn();

        builder.Property(attempt => attempt.StepId)
            .HasColumnType("uniqueidentifier");

        builder.Property(attempt => attempt.AttemptNumber)
            .HasColumnType("int");

        builder.Property(attempt => attempt.StartedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(attempt => attempt.CompletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(attempt => attempt.DurationMs)
            .HasColumnType("int");

        builder.Property(attempt => attempt.ResultType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(attempt => attempt.HttpStatusCode)
            .HasColumnType("smallint");

        builder.Property(attempt => attempt.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(attempt => attempt.RequestPayloadMasked)
            .HasColumnType("nvarchar(max)");

        builder.Property(attempt => attempt.ResponsePayloadMasked)
            .HasColumnType("nvarchar(max)");

        builder.Property(attempt => attempt.ErrorCode)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(attempt => attempt.ErrorMessage)
            .HasColumnType("nvarchar(1000)")
            .HasMaxLength(1000);

        builder.Property(attempt => attempt.CorrelationId)
            .HasColumnType("uniqueidentifier");

        builder.HasOne(attempt => attempt.Step)
            .WithMany(step => step.Attempts)
            .HasForeignKey(attempt => attempt.StepId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_IntegrationAttempts_IntegrationSteps_StepId");

        builder.HasIndex(attempt => new
            {
                attempt.StepId,
                attempt.AttemptNumber
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_IntegrationAttempts_StepId_AttemptNumber");

        builder.HasIndex(attempt => new
            {
                attempt.StepId,
                attempt.StartedAtUtc
            })
            .IsDescending(false, true)
            .HasDatabaseName(
                "IX_IntegrationAttempts_StepId_StartedAtUtc");

        builder.HasIndex(attempt => attempt.CorrelationId)
            .HasDatabaseName("IX_IntegrationAttempts_CorrelationId");
    }
}
