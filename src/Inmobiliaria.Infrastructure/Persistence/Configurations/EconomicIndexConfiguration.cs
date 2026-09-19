using Inmobiliaria.Domain.Indices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// economic-index: Index Identity, Discontinuation Names a Successor. The unique index on
/// <c>name</c> is PARTIAL — <c>WHERE discontinued_from IS NULL</c> — so a rebased index can
/// legally succeed a discontinued one under the same display name (design.md Decision 3).
/// </summary>
public sealed class EconomicIndexConfiguration : IEntityTypeConfiguration<EconomicIndex>
{
    public void Configure(EntityTypeBuilder<EconomicIndex> builder)
    {
        builder.ToTable("economic_indices", t => t.HasCheckConstraint(
            "ck_economic_indices_successor_not_self",
            "successor_index_id <> id"));

        builder.HasKey(i => i.Id);

        // Explicit mapping for the read-only (no-setter) Name property: without it, EF's
        // constructor-binding discovery can misidentify the mapped model shape, exactly as
        // ContractConfiguration documents for its own no-setter properties.
        builder.Property(i => i.Name).IsRequired();

        builder.Property(i => i.DiscontinuedFrom)
            .HasConversion(IndexPeriodValueConverter.Nullable)
            .HasColumnName("discontinued_from")
            .HasColumnType("date");

        builder.Property(i => i.SuccessorIndexId).HasColumnName("successor_index_id");

        builder.HasIndex(i => i.Name)
            .IsUnique()
            .HasFilter("discontinued_from IS NULL")
            .HasDatabaseName("ix_economic_indices_name_active");
    }
}
