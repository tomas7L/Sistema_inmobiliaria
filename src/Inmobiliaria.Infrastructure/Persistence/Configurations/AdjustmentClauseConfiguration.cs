using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// rent-adjustment: Per-Contract Adjustment Clause. <c>contract_id</c> is UNIQUE — a contract
/// has at most one clause; absence of a clause is the absence of a row, and <see cref="Contract"/>
/// gains no column for it (design.md Decision 5).
/// </summary>
public sealed class AdjustmentClauseConfiguration : IEntityTypeConfiguration<AdjustmentClause>
{
    public void Configure(EntityTypeBuilder<AdjustmentClause> builder)
    {
        builder.ToTable("adjustment_clauses", t => t.HasCheckConstraint(
            "ck_adjustment_clauses_interval_months_range",
            "interval_months BETWEEN 1 AND 60"));

        builder.HasKey(c => c.Id);

        // Explicit mapping for the read-only (no-setter) properties: AdjustmentClause's public
        // constructor also takes an `economicIndexIds` parameter that maps to no scalar
        // property at all, so leaving discovery to convention risks EF attempting to bind that
        // constructor and failing with "no suitable constructor was found" — the exact failure
        // ContractConfiguration's own comment describes for the same shape of problem.
        builder.Property(c => c.ContractId).IsRequired();
        builder.Property(c => c.IntervalMonths).HasColumnName("interval_months").IsRequired();

        builder.Property(c => c.RoundingRule)
            .HasConversion<string>()
            .HasColumnName("rounding_rule")
            .HasMaxLength(32)
            .IsRequired();

        // Combination is a computed getter derived from Indices.Count (Single for one index,
        // Average for two or more) — it has no setter and no backing field of its own, so EF
        // cannot materialize a value into it on load. Mapping it as a stored `combination`
        // column, as design.md Decision 3's table literally lists, would need a backing field
        // added to AdjustmentClause.cs, a domain change outside this slice's EF-only scope.
        // Ignoring it here is not a functional loss: Combination is always correctly
        // re-derived from the loaded Indices collection, with no risk of a stored copy going
        // stale against the rows that are the actual source of truth. Recorded as a deviation.
        builder.Ignore(c => c.Combination);

        builder.Navigation(c => c.Indices).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Contract>()
            .WithOne(c => c.AdjustmentClause)
            .HasForeignKey<AdjustmentClause>(c => c.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
