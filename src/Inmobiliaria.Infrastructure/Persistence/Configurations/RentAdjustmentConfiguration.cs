using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// rent-adjustment: Append-Only Adjustment History. EF's own half of design.md Decision 6 —
/// the domain (no public mutator) is the primary defence and the database's
/// <c>BEFORE UPDATE OR DELETE</c> trigger (added in the migration) is the backstop; every
/// property here throws on a post-save modification attempt instead of surfacing only as an
/// opaque Postgres error.
/// </summary>
public sealed class RentAdjustmentConfiguration : IEntityTypeConfiguration<RentAdjustment>
{
    public void Configure(EntityTypeBuilder<RentAdjustment> builder)
    {
        builder.ToTable("rent_adjustments", t =>
        {
            t.HasCheckConstraint(
                "ck_rent_adjustments_kind_corrects",
                "(kind = 'Correction') = (corrects_adjustment_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_rent_adjustments_effective_date_first_of_month",
                "EXTRACT(DAY FROM effective_date) = 1");
            t.HasCheckConstraint(
                "ck_rent_adjustments_applies_from_first_of_month",
                "applies_from IS NULL OR EXTRACT(DAY FROM applies_from) = 1");

            // The recorded choice (design D6): not offered is both null, declined is an amount alone, charged is both.
            t.HasCheckConstraint(
                "ck_rent_adjustments_difference_amount_positive",
                "difference_amount IS NULL OR difference_amount > 0");
            t.HasCheckConstraint(
                "ck_rent_adjustments_difference_charged_was_offered",
                "difference_movement_id IS NULL OR difference_amount IS NOT NULL");
        });

        builder.HasKey(a => a.Id);

        // Explicit mapping for the read-only (no-setter) properties — every property on this
        // type is get-only, set once via the private Confirm factory — following the same
        // defensive convention ContractConfiguration documents for its own no-setter dates.
        builder.Property(a => a.ContractId).IsRequired();
        builder.Property(a => a.EffectiveDate).HasColumnName("effective_date").IsRequired();
        builder.Property(a => a.ConfirmedAt).HasColumnName("confirmed_at").IsRequired();

        builder.Property(a => a.PreviousCanon)
            .HasColumnName("previous_canon")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(a => a.NewCanon)
            .HasColumnName("new_canon")
            .HasPrecision(14, 2)
            .IsRequired();

        // Coefficient precision is numeric(12,6), not the numeric(9,6) used for
        // share_percentage: a share is bounded by 100 by definition, a variation is not
        // (design.md Decision 3).
        builder.Property(a => a.Coefficient)
            .HasColumnName("coefficient")
            .HasPrecision(12, 6)
            .IsRequired();

        builder.Property(a => a.Kind)
            .HasConversion<string>()
            .HasColumnName("kind")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.CorrectsAdjustmentId).HasColumnName("corrects_adjustment_id");

        // Nullable at the column and at the property: existing rows predate this column and
        // stay null forever under the append-only trigger. RentAdjustment.Confirm's
        // confirmedBy parameter is what stays non-nullable, not this mapped property
        // (design Decision 8).
        builder.Property(a => a.ConfirmedBy).HasColumnName("confirmed_by");

        // Nullable for the same reason as confirmed_by: rows written before it cannot be backfilled
        // (gestion-de-alquileres design D1).
        builder.Property(a => a.AppliesFrom).HasColumnName("applies_from");

        builder.Property(a => a.DifferenceAmount).HasColumnName("difference_amount").HasPrecision(14, 2);
        builder.Property(a => a.DifferenceMovementId).HasColumnName("difference_movement_id");

        // One charge belongs to one adjustment.
        builder.HasIndex(a => a.DifferenceMovementId)
            .IsUnique()
            .HasFilter("difference_movement_id IS NOT NULL")
            .HasDatabaseName("ux_rent_adjustments_difference_movement");

        // A double confirmation, including two operators at once, is refused here and not only by
        // the adapter's re-read, which two concurrent transactions can both pass (design D8).
        builder.HasIndex(a => new { a.ContractId, a.EffectiveDate })
            .IsUnique()
            .HasFilter("kind = 'Regular'")
            .HasDatabaseName("ux_rent_adjustments_one_regular_per_due_date");

        // Declared, or EF would drop the foreign key's own index for the one above, which is partial and serves neither the
        // Include of a contract's adjustments nor the foreign key's checks.
        builder.HasIndex(a => a.ContractId).HasDatabaseName("ix_rent_adjustments_contract_id");

        builder.HasIndex(a => a.CorrectsAdjustmentId)
            .IsUnique()
            .HasFilter("corrects_adjustment_id IS NOT NULL")
            .HasDatabaseName("ux_rent_adjustments_one_correction_per_adjustment");

        builder.Navigation(a => a.IndexValues).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Contract>()
            .WithMany(c => c.Adjustments)
            .HasForeignKey(a => a.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(a => a.ConfirmedBy)
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
