using ErpIntegration.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpIntegration.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpCustomerLinkConfiguration
    : IEntityTypeConfiguration<ErpCustomerLink>
{
    public void Configure(EntityTypeBuilder<ErpCustomerLink> builder)
    {
        builder.ToTable("ErpCustomerLinks");

        builder.HasKey(link => link.Id)
            .HasName("PK_ErpCustomerLinks");

        builder.Property(link => link.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(link => link.CustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(link => link.ErpCustomerCode)
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(link => link.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(link => link.LastVerifiedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(link => link.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(link => link.CustomerId)
            .IsUnique()
            .HasDatabaseName("UX_ErpCustomerLinks_CustomerId");

        builder.HasIndex(link => link.ErpCustomerCode)
            .IsUnique()
            .HasDatabaseName("UX_ErpCustomerLinks_ErpCustomerCode");
    }
}
