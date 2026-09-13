using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Row-level half of the rent-split invariant (design.md Decision 3). The cross-row sum
/// check is a deferred trigger added in the migration, not here.
/// </summary>
public sealed class ContractUnitConfiguration : IEntityTypeConfiguration<ContractUnit>
{
    public void Configure(EntityTypeBuilder<ContractUnit> builder)
    {
        builder.ToTable("contract_units", t => t.HasCheckConstraint(
            "ck_contract_units_share_percentage_range",
            "share_percentage > 0 AND share_percentage <= 100"));

        builder.HasKey(cu => new { cu.ContractId, cu.UnitId });

        builder.Property(cu => cu.SharePercentage)
            .HasColumnName("share_percentage")
            .HasPrecision(9, 6)
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany(c => c.Units)
            .HasForeignKey(cu => cu.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(cu => cu.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
