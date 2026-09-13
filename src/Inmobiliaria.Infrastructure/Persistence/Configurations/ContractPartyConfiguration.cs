using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// lease-contract: Party Role Multiplicity, Role-Scoped Uniqueness. Composite key includes
/// <c>role</c> so "same party in two roles on one contract" stays a domain validation
/// question (see design.md open question), not a schema rule.
/// </summary>
public sealed class ContractPartyConfiguration : IEntityTypeConfiguration<ContractParty>
{
    public void Configure(EntityTypeBuilder<ContractParty> builder)
    {
        builder.ToTable("contract_parties", t => t.HasCheckConstraint(
            "ck_contract_parties_role",
            "role IN ('Lessor', 'Tenant', 'Codebtor')"));

        builder.HasKey(cp => new { cp.ContractId, cp.PartyId, cp.Role });

        builder.Property(cp => cp.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany(c => c.Parties)
            .HasForeignKey(cp => cp.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Party>()
            .WithMany()
            .HasForeignKey(cp => cp.PartyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
