using Inmobiliaria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// contract-account: The Ledger Is Append-Only; a Correction Is a New Movement. EF's half of the
/// rule — every property throws on a post-save modification attempt — with the migration's
/// <c>BEFORE UPDATE OR DELETE</c> trigger as the backstop that also catches raw SQL. The same
/// two-layer shape <c>rent_adjustments</c> already uses, rather than a second mechanism.
/// </summary>
public sealed class AccountMovementConfiguration : IEntityTypeConfiguration<AccountMovement>
{
    public void Configure(EntityTypeBuilder<AccountMovement> builder)
    {
        builder.ToTable("account_movements", t =>
        {
            // Mirrors ck_rent_adjustments_kind_corrects: a correction names what it corrects, and
            // nothing else does. Both directions, so neither a correction without a target nor a
            // target on a non-correction can be stored.
            t.HasCheckConstraint(
                "ck_account_movements_kind_corrects",
                "(kind = 'Correction') = (corrects_movement_id IS NOT NULL)");

            // A period identifies a month and is stored as its first day, the same convention
            // rent_adjustments uses for effective_date.
            t.HasCheckConstraint(
                "ck_account_movements_period_first_of_month",
                "period IS NULL OR EXTRACT(DAY FROM period) = 1");

            // The kinds the ledger knows, mirroring MovementKind. Built from the enum so the list
            // cannot drift from it: a new member changes this expression, the model then differs
            // from the last migration, and has-pending-model-changes says so.
            t.HasCheckConstraint(
                "ck_account_movements_kind",
                $"kind IN ({string.Join(",", Enum.GetNames<MovementKind>().Select(name => $"'{name}'"))})");

            // Every kind that is about one month carries it: rent, the instalment that rides with
            // it, and the payment and recargo that settle it. An opening balance may be period-less
            // only as a zero (below); a frozen recargo belongs to the day a plan was signed, and a
            // correction carries the period of what it corrects only when that is period-scoped.
            // Replaces ck_account_movements_accrual_has_period, which covered the rent alone.
            t.HasCheckConstraint(
                "ck_account_movements_period_required",
                "kind NOT IN ('RentAccrual','ContractualFeeInstalment','Payment','RecargoCharged') " +
                "OR period IS NOT NULL");

            // A pre-existing debt is loaded month by month. The only figure that may stand without a
            // month is the zero that says "somebody looked and nothing is owed" (design D18).
            t.HasCheckConstraint(
                "ck_account_movements_opening_balance_period",
                "kind <> 'OpeningBalance' OR period IS NOT NULL OR amount = 0");

            // A payment is a credit and a charged recargo is a debit. The sign lives on the amount,
            // so the database says which way each of these two kinds may point.
            t.HasCheckConstraint(
                "ck_account_movements_payment_sign",
                "kind <> 'Payment' OR amount < 0");
            t.HasCheckConstraint(
                "ck_account_movements_recargo_charged_sign",
                "kind <> 'RecargoCharged' OR amount > 0");
        });

        // The id is application-supplied, which InmobiliariaDbContext declares for every
        // single-Guid key in the model — see ApplyApplicationSuppliedKeys for why that matters and
        // what breaks without it. Appending a movement to an account that is ALREADY persisted is
        // the whole point of lazy accrual, so this type depends on it more than most.
        builder.HasKey(m => m.Id);

        // Every property is get-only, set once through a static factory, so each is mapped
        // explicitly: convention skips a property with no setter.
        builder.Property(m => m.Kind)
            .HasConversion<string>()
            .HasColumnName("kind")
            // 32, not the 16 rent_adjustments uses: ContractualFeeInstalment is 24 characters and
            // would be silently truncated at 16.
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.Amount)
            .HasColumnName("amount")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(m => m.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(m => m.Period).HasColumnName("period");
        builder.Property(m => m.CorrectsMovementId).HasColumnName("corrects_movement_id");

        // The owning account's key, as a shadow property: a movement is never handled apart from
        // its account, so the domain type carries no AccountId of its own.
        builder.Property<Guid>("AccountId").HasColumnName("account_id").IsRequired();

        // Design Decision 1: two readers materialising the same period at the same moment. The
        // database settles it — the second insert fails and that caller re-reads. A constraint and
        // a retry, not a lock.
        //
        // Scoped to the two kinds that CHARGE a period (cobranza-y-recibos design section 5): a rent
        // accrual and a monthly opening balance. One charge per period across both, which is also
        // "a period holding an opening balance takes no accrual". It replaces
        // ux_account_movements_accrual_period, which covered the rent alone.
        //
        // Still scoped, and for the same reason as before: a broader index over every kind carrying
        // a period would reject a second correction to a month already corrected once and a second
        // payment against one period, which is what a voided and repeated payment is.
        builder.HasIndex("AccountId", nameof(AccountMovement.Period))
            .IsUnique()
            .HasFilter("kind IN ('RentAccrual','OpeningBalance')")
            .HasDatabaseName("ux_account_movements_period_charge");

        // At most one instalment per period: the stronger form of "never raised twice", for two
        // writers (an early payment and the lazy accrual) that both pass the domain check.
        //
        // A NAMED index (the overload taking the name), because it covers the same two properties as
        // the charge index above and EF allows that only when each index has its own name.
        builder.HasIndex(
                ["AccountId", nameof(AccountMovement.Period)],
                "ux_account_movements_instalment_period")
            .IsUnique()
            .HasFilter("kind = 'ContractualFeeInstalment'")
            .HasDatabaseName("ux_account_movements_instalment_period");

        // At most one period-less opening balance per account: the zero that says nothing is owed.
        builder.HasIndex("AccountId")
            .IsUnique()
            .HasFilter("kind = 'OpeningBalance' AND period IS NULL")
            .HasDatabaseName("ux_account_movements_opening_zero");

        // A correction points at another movement. Restrict rather than Cascade: deleting the
        // corrected row is not a thing that may happen, and the append-only trigger refuses it
        // anyway — this makes the model say so too.
        builder.HasOne<AccountMovement>()
            .WithMany()
            .HasForeignKey(m => m.CorrectsMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every property the domain owns is fixed once written. The shadow AccountId is excluded
        // deliberately, and this is not a relaxation: EF assigns that value itself during
        // relationship fixup, so marking it read-only-after-save rejects the ordinary case of
        // appending a movement to an account that is already persisted — which is every append
        // after the first. RentAdjustment can throw on its ContractId because the domain sets it
        // in the constructor, before the entity is ever tracked; a shadow key has no such moment.
        //
        // Nothing is lost by the exclusion. Re-parenting a movement means an UPDATE of
        // account_id, and two layers still refuse that: the append-only trigger rejects any
        // update to this table whatever the column, and neither application role is granted
        // UPDATE on it at all.
        foreach (var property in builder.Metadata.GetProperties().Where(p => !p.IsShadowProperty()))
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
