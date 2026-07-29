using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineMarket.Web.Domain.Entities;

namespace OnlineMarket.Web.Infrastructure.Persistence.Configurations;

public class OrderAddressConfiguration : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("OrderAddresses");

        builder.HasKey(oa => oa.OrderId);

        builder.Property(oa => oa.RecipientName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oa => oa.PhoneNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(oa => oa.AddressLine1)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(oa => oa.AddressLine2)
            .HasMaxLength(250);

        builder.Property(oa => oa.District)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(oa => oa.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(oa => oa.PostalCode)
            .HasMaxLength(20);

        builder.Property(oa => oa.CountryCode)
            .IsRequired()
            .HasColumnType("char(2)")
            .HasDefaultValue("TR");

        builder.HasOne(oa => oa.Order)
            .WithOne(o => o.AddressSnapshot)
            .HasForeignKey<OrderAddress>(oa => oa.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
