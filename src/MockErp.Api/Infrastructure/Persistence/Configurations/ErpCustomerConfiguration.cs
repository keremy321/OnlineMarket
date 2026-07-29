using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpCustomerConfiguration
    : IEntityTypeConfiguration<ErpCustomer>
{
    public void Configure(EntityTypeBuilder<ErpCustomer> builder)
    {
        builder.ToTable("ErpCustomers");

        builder.HasKey(customer => customer.Id)
            .HasName("PK_ErpCustomers");

        builder.Property(customer => customer.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(customer => customer.ErpCustomerCode)
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(customer => customer.ExternalCustomerId)
            .HasColumnType("uniqueidentifier");

        builder.Property(customer => customer.FirstName)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.LastName)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.Email)
            .HasColumnType("nvarchar(256)")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(customer => customer.PhoneNumber)
            .HasColumnType("nvarchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(customer => customer.AddressLine1)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(customer => customer.AddressLine2)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250);

        builder.Property(customer => customer.District)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.City)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.PostalCode)
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20);

        builder.Property(customer => customer.CountryCode)
            .HasColumnType("char(2)")
            .HasMaxLength(2)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(customer => customer.IsActive)
            .HasColumnType("bit")
            .HasDefaultValue(true);

        builder.Property(customer => customer.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(customer => customer.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(customer => customer.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(customer => customer.ErpCustomerCode)
            .IsUnique()
            .HasDatabaseName("UX_ErpCustomers_ErpCustomerCode");

        builder.HasIndex(customer => customer.ExternalCustomerId)
            .IsUnique()
            .HasDatabaseName("UX_ErpCustomers_ExternalCustomerId");

        builder.HasIndex(customer => customer.Email)
            .HasDatabaseName("IX_ErpCustomers_Email");
    }
}
