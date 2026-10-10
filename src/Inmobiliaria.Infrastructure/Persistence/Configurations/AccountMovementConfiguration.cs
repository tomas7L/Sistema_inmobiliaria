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

            // A rent accrual always covers a period. The other kinds may legitimately carry none:
            // an opening balance is a single assertion, a frozen recargo belongs to the day a plan
            // was signed, and a contractual fee instalment has its own due date rather than a
            // month of rent.
            t.HasCheckConstraint(
                "ck_account_movements_accrual_has_period",
                "kind <> 'RentAccrual' OR period IS NOT NULL");
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
        // Scoped to accruals, exactly as the design words it ("for accrual movements"). A broader
        // index over every kind carrying a period would forbid two things the business does: it
        // would reject a second correction to a month already corrected once, and — the worse of
        // the two — it would reject a second payment against one period, which is precisely what
        // a payment plan is. Collection adds that kind, and this index must not be waiting to
        // break it.
        builder.HasIndex("AccountId", nameof(AccountMovement.Period))
            .IsUnique()
            .HasFilter("kind = 'RentAccrual'")
            .HasDatabaseName("ux_account_movements_accrual_period");

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
