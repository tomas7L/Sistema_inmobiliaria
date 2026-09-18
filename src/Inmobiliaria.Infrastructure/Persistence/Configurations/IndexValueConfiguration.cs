using Inmobiliaria.Domain.Indices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// economic-index: Published Value Stored Per Period. Stores the published LEVEL (design.md
/// Decision 1) — <c>numeric(18,6)</c>, never money's <c>numeric(14,2)</c> and never truncated
/// to a whole peso; RIPTE is an amount in pesos but is still a level here.
/// </summary>
public sealed class IndexValueConfiguration : IEntityTypeConfiguration<IndexValue>
{
    public void Configure(EntityTypeBuilder<IndexValue> builder)
    {
        builder.ToTable("index_values", t =>
        {
            t.HasCheckConstraint("ck_index_values_level_positive", "level > 0");
            t.HasCheckConstraint(
                "ck_index_values_period_first_of_month",
                "EXTRACT(DAY FROM period) = 1");
        });

        builder.HasKey(v => v.Id);

        builder.Property(v => v.EconomicIndexId).IsRequired();

        builder.Property(v => v.Period)
            .HasConversion(IndexPeriodValueConverter.NonNullable)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(v => v.Level)
            .HasPrecision(18, 6)
            .IsRequired();

        builder.HasIndex(v => new { v.EconomicIndexId, v.Period })
            .IsUnique()
            .HasDatabaseName("ix_index_values_economic_index_id_period");

        builder.HasOne<EconomicIndex>()
            .WithMany()
            .HasForeignKey(v => v.EconomicIndexId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
