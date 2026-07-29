using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class RecommendationRunConfiguration : IEntityTypeConfiguration<RecommendationRun>
{
    public void Configure(EntityTypeBuilder<RecommendationRun> builder)
    {
        builder.ToTable(
            "RecommendationRuns",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_RecommendationRuns_ParametersJson_IsJson",
                    "ISJSON([ParametersJson]) = 1");
                table.HasCheckConstraint(
                    "CK_RecommendationRuns_Counts_NonNegative",
                    "[InputRecordCount] >= 0 AND [OutputRecordCount] >= 0");
                table.HasCheckConstraint(
                    "CK_RecommendationRuns_CompletedAfterStarted",
                    "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [StartedAtUtc]");
            });

        builder.HasKey(run => run.Id)
            .HasName("PK_RecommendationRuns");

        builder.Property(run => run.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(run => run.RunType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(run => run.Status)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(run => run.StartedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(run => run.CompletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(run => run.InputRecordCount)
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(run => run.OutputRecordCount)
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(run => run.ParametersJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(run => run.ErrorMessage)
            .HasColumnType("nvarchar(2000)")
            .HasMaxLength(2000);

        builder.Property(run => run.TriggeredByUserId)
            .HasColumnType("uniqueidentifier");

        builder.Property(run => run.CorrelationId)
            .HasColumnType("uniqueidentifier");

        builder.HasIndex(run => new
            {
                run.RunType,
                run.StartedAtUtc
            })
            .HasDatabaseName("IX_RecommendationRuns_RunType_StartedAtUtc");

        builder.HasIndex(run => new
            {
                run.Status,
                run.StartedAtUtc
            })
            .HasDatabaseName("IX_RecommendationRuns_Status_StartedAtUtc");

        builder.HasIndex(run => run.CorrelationId)
            .HasDatabaseName("IX_RecommendationRuns_CorrelationId");
    }
}
