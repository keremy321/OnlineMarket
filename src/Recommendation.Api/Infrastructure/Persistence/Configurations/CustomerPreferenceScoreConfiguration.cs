using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recommendation.Api.Domain.Entities;

namespace Recommendation.Api.Infrastructure.Persistence.Configurations;

public sealed class CustomerPreferenceScoreConfiguration : IEntityTypeConfiguration<CustomerPreferenceScore>
{
    public void Configure(EntityTypeBuilder<CustomerPreferenceScore> builder)
    {
        builder.ToTable(
            "CustomerPreferenceScores",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CustomerPreferenceScores_Counts_NonNegative",
                    "[PurchaseCount] >= 0 AND [TotalQuantity] >= 0");
                table.HasCheckConstraint(
                    "CK_CustomerPreferenceScores_FrequencyScore_Range",
                    "[FrequencyScore] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_CustomerPreferenceScores_RecencyScore_Range",
                    "[RecencyScore] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_CustomerPreferenceScores_Score_Range",
                    "[Score] BETWEEN 0 AND 1");
            });

        builder.HasKey(preference => new
            {
                preference.CustomerId,
                preference.PreferenceType,
                preference.ReferenceId
            })
            .HasName("PK_CustomerPreferenceScores");

        builder.Property(preference => preference.CustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(preference => preference.PreferenceType)
            .HasConversion<byte>()
            .HasColumnType("tinyint");

        builder.Property(preference => preference.ReferenceId)
            .HasColumnType("uniqueidentifier");

        builder.Property(preference => preference.PurchaseCount)
            .HasColumnType("int");

        builder.Property(preference => preference.TotalQuantity)
            .HasColumnType("int");

        builder.Property(preference => preference.LastPurchasedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(preference => preference.FrequencyScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(preference => preference.RecencyScore)
            .HasColumnType("decimal(12,6)");

        builder.Property(preference => preference.Score)
            .HasColumnType("decimal(12,6)");

        builder.Property(preference => preference.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.HasIndex(preference => new
            {
                preference.CustomerId,
                preference.PreferenceType,
                preference.Score
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_CustomerPreferenceScores_CustomerId_PreferenceType_Score");
    }
}
