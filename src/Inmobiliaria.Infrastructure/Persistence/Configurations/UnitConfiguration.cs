using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// TPH root for <see cref="PropertyUnit"/> / <see cref="ParkingUnit"/>, both currently
/// field-free. The subtype hierarchy already encodes <see cref="UnitType"/>, so the CLR
/// enum property is ignored in favor of a dedicated shadow discriminator column — mapping
/// both would duplicate the same fact in two columns.
/// </summary>
public sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units");

        builder.HasKey(u => u.Id);

        builder.Ignore(u => u.UnitType);

        builder.OwnsOne(u => u.Address, address =>
        {
            address.Property(a => a.Street).HasColumnName("address_street").IsRequired();
            address.Property(a => a.Number).HasColumnName("address_number").IsRequired();
            address.Property(a => a.Floor).HasColumnName("address_floor");
            address.Property(a => a.Apartment).HasColumnName("address_apartment");
            address.Property(a => a.City).HasColumnName("address_city").IsRequired();
            address.Property(a => a.Province).HasColumnName("address_province").IsRequired();
            address.Property(a => a.PostalCode).HasColumnName("address_postal_code").IsRequired();
        });

        builder.Navigation(u => u.Address).IsRequired();

        builder.HasDiscriminator<string>("unit_type")
            .HasValue<PropertyUnit>("property")
            .HasValue<ParkingUnit>("parking");
    }
}
