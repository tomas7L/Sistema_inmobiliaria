using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// rent-adjustment: one index's contribution to a confirmed <see cref="RentAdjustment"/>,
/// snapshotted BY VALUE at confirmation time (design.md Decision 3) — periods and levels are
/// copies, never a foreign key to the live <c>index_values</c> row, so a later correction of the
/// source value cannot change history. This type carries no id of its own; <c>rent_adjustment_id</c>
/// is a shadow property supplying its half of the composite key.
/// </summary>
public sealed class RentAdjustmentIndexValueConfiguration : IEntityTypeConfiguration<RentAdjustmentIndexValue>
{
    public void Configure(EntityTypeBuilder<RentAdjustmentIndexValue> builder)
    {
        builder.ToTable("rent_adjustment_index_values");

        builder.Property<Guid>("RentAdjustmentId");
        builder.HasKey("RentAdjustmentId", nameof(RentAdjustmentIndexValue.ReferencedIndexId));

        builder.Property(v => v.ResolvedIndexId)
            .HasColumnName("resolved_index_id")
            .IsRequired();

        builder.Property(v => v.BasePeriod)
            .HasConversion(IndexPeriodValueConverter.NonNullable)
            .HasColumnName("base_period")
            .HasColumnType("date")
            .IsRequired();

        // Levels are index levels, not money: numeric(18,6), the same precision as
        // index_values.level — never money's numeric(14,2) and never truncated.
        builder.Property(v => v.BaseLevel)
            .HasColumnName("base_level")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(v => v.EndPeriod)
            .HasConversion(IndexPeriodValueConverter.NonNullable)
            .HasColumnName("end_period")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(v => v.EndLevel)
            .HasColumnName("end_level")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(v => v.Variation)
            .HasColumnName("variation")
            .HasPrecision(12, 6)
            .IsRequired();

        builder.HasOne<RentAdjustment>()
            .WithMany(a => a.IndexValues)
            .HasForeignKey("RentAdjustmentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
