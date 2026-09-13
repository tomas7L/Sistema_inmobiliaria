using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>party-registry: Natural Key Uniqueness — DNI and CUIL are unique when present.</summary>
public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.Dni)
            .IsUnique()
            .HasFilter("dni IS NOT NULL")
            .HasDatabaseName("ix_parties_dni");

        builder.HasIndex(p => p.Cuil)
            .IsUnique()
            .HasFilter("cuil IS NOT NULL")
            .HasDatabaseName("ix_parties_cuil");
    }
}
