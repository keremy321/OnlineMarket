using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MockErp.Api.Domain.Entities;

namespace MockErp.Api.Infrastructure.Persistence.Configurations;

public sealed class ErpOrderAddressConfiguration
    : IEntityTypeConfiguration<ErpOrderAddress>
{
    public void Configure(EntityTypeBuilder<ErpOrderAddress> builder)
    {
        builder.ToTable("ErpOrderAddresses");

        builder.HasKey(address => address.ErpOrderId)
            .HasName("PK_ErpOrderAddresses");

        builder.Property(address => address.ErpOrderId)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(address => address.RecipientName)
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(address => address.PhoneNumber)
            .HasColumnType("nvarchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(address => address.AddressLine1)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(address => address.AddressLine2)
            .HasColumnType("nvarchar(250)")
            .HasMaxLength(250);

        builder.Property(address => address.District)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(address => address.City)
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(address => address.PostalCode)
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20);

        builder.Property(address => address.CountryCode)
            .HasColumnType("char(2)")
            .HasMaxLength(2)
            .IsUnicode(false)
            .IsFixedLength()
            .HasDefaultValue("TR")
            .IsRequired();

        builder.HasOne(address => address.ErpOrder)
            .WithOne(order => order.Address)
            .HasForeignKey<ErpOrderAddress>(address => address.ErpOrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
