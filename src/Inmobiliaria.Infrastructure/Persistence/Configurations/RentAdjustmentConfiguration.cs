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

        builder.Navigation(a => a.IndexValues).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Contract>()
            .WithMany(c => c.Adjustments)
            .HasForeignKey(a => a.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
