using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");

        builder.HasKey(ca => ca.Id);
        builder.Property(ca => ca.Id).ValueGeneratedNever();

        builder.Property(ca => ca.Title)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ca => ca.ContactName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ca => ca.PhoneNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(ca => ca.AddressLine1)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(ca => ca.AddressLine2)
            .HasMaxLength(250);

        builder.Property(ca => ca.District)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ca => ca.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ca => ca.PostalCode)
            .HasMaxLength(20);

        builder.Property(ca => ca.CountryCode)
            .IsRequired()
            .HasColumnType("char(2)")
            .HasDefaultValue("TR");

        builder.Property(ca => ca.IsDefault)
            .HasDefaultValue(false);

        builder.Property(ca => ca.IsActive)
            .HasDefaultValue(true);

        builder.Property(ca => ca.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(ca => ca.UpdatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(ca => ca.RowVersion)
            .IsRowVersion();

        builder.HasOne(ca => ca.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(ca => ca.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(ca => new { ca.CustomerId, ca.IsActive })
            .HasDatabaseName("IX_CustomerAddresses_CustomerId_IsActive");

        builder.HasIndex(ca => ca.CustomerId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsActive] = 1")
            .HasDatabaseName("UX_CustomerAddresses_Default");
    }
}
