using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// rent-adjustment: Per-Contract Adjustment Clause, the join half. Holds only the referenced
/// index's id and its display order — never an <see cref="EconomicIndex"/> reference, identical
/// in shape to <c>ContractUnit</c> holding a unit id (design.md Decision 3).
/// </summary>
public sealed class AdjustmentClauseIndexConfiguration : IEntityTypeConfiguration<AdjustmentClauseIndex>
{
    public void Configure(EntityTypeBuilder<AdjustmentClauseIndex> builder)
    {
        builder.ToTable("adjustment_clause_indices");

        builder.HasKey(i => new { i.AdjustmentClauseId, i.EconomicIndexId });

        builder.Property(i => i.Ordinal).IsRequired();

        builder.HasOne<AdjustmentClause>()
            .WithMany(c => c.Indices)
            .HasForeignKey(i => i.AdjustmentClauseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EconomicIndex>()
            .WithMany()
            .HasForeignKey(i => i.EconomicIndexId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
