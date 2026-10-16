using Inmobiliaria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// cobranza-y-recibos migration B: the <c>collections</c> table, one row per committed payment. It ties
/// a period of an account to the <c>Payment</c> (and the <c>RecargoCharged</c>, when there was one) it
/// wrote, and carries the two things only the database may say: when the payment was recorded and by
/// which login.
///
/// Mapped as a property-bag (shared-type) entity named after its table, not onto
/// <c>CollectionRecord</c>. That domain record is a read model for the voiding planner: it carries
/// <c>IsAnnulled</c>, which is no column, and has no attempt, stamp or constructor EF can bind, so
/// mapping it would mean changing a domain type for the sake of persistence. The tables and the
/// migration are what this change owns; the desk that writes them (a later slice) reads and writes the
/// bag and builds the domain types from it.
///
/// Like every other table here, the rows are append-only: the migration's trigger refuses UPDATE and
/// DELETE, and EF throws on a post-save modification of any property.
/// </summary>
public static class CollectionConfiguration
{
    public const string EntityName = "collections";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                // A period identifies a month and is stored as its first day (the ledger's convention).
                t.HasCheckConstraint("ck_collections_period_first_of_month", "EXTRACT(DAY FROM period) = 1");
                t.HasCheckConstraint("ck_collections_attempt_positive", "attempt >= 1");
            });

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("AccountId").HasColumnName("account_id").IsRequired();
            builder.IndexerProperty<DateOnly>("Period").HasColumnName("period").IsRequired();
            builder.IndexerProperty<short>("Attempt").HasColumnName("attempt").IsRequired();
            builder.IndexerProperty<DateOnly>("ReceivedOn").HasColumnName("received_on").IsRequired();

            // Stamped by the collections_stamp trigger, which overwrites whatever an INSERT supplies, so
            // EF leaves both out of the INSERT and reads them back (spec Decision 12; design D10).
            builder.IndexerProperty<DateTimeOffset>("RecordedAt")
                .HasColumnName("recorded_at")
                .HasColumnType("timestamp with time zone")
                .ValueGeneratedOnAdd()
                .IsRequired();
            builder.IndexerProperty<string>("RecordedBy")
                .HasColumnName("recorded_by")
                .HasColumnType("name")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.IndexerProperty<Guid>("PaymentMovementId").HasColumnName("payment_movement_id").IsRequired();
            builder.IndexerProperty<Guid?>("RecargoMovementId").HasColumnName("recargo_movement_id");

            builder.HasOne<ContractAccount>()
                .WithMany()
                .HasForeignKey("AccountId")
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<AccountMovement>()
                .WithMany()
                .HasForeignKey("PaymentMovementId")
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<AccountMovement>()
                .WithMany()
                .HasForeignKey("RecargoMovementId")
                .OnDelete(DeleteBehavior.Restrict);

            // One payment per (account, period, attempt). The attempt is what lets a voided period take a
            // new payment while two live payments for one period stay impossible (design section 4): the
            // trigger refuses an attempt that is not the previous one plus one, and this index is what two
            // operators racing for the same attempt collide on.
            builder.HasIndex("AccountId", "Period", "Attempt")
                .IsUnique()
                .HasDatabaseName("ux_collections_live_attempt");

            // A movement belongs to at most one collection.
            builder.HasIndex("PaymentMovementId").IsUnique().HasDatabaseName("ux_collections_payment_movement");
            builder.HasIndex("RecargoMovementId").IsUnique().HasDatabaseName("ux_collections_recargo_movement");

            AppendOnly.Apply(builder);
        });
    }
}

/// <summary>
/// The EF half of the append-only rule shared by the four recibo-side tables: every property throws on
/// a post-save modification attempt. The key is excluded by EF itself; the foreign keys are set by the
/// caller in these property-bag entities (there are no navigations to fix them up), so nothing here
/// needs the shadow-key exemption <c>AccountMovementConfiguration</c> makes.
/// </summary>
internal static class AppendOnly
{
    public static void Apply(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder builder)
    {
        foreach (var property in builder.Metadata.GetProperties().Where(p => !p.IsPrimaryKey()))
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
