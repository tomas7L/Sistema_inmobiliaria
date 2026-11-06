using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>contract_events</c>, append-only (gestion-de-alquileres design D12): the reason, who and when of what a contract's own
/// row cannot hold, since undoing clears its columns. Who and when are the database's, as on <c>recibo_annulments</c>.
/// </summary>
public static class ContractEventConfiguration
{
    public const string EntityName = "contract_events";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                t.HasCheckConstraint(
                    "ck_contract_events_kind",
                    $"kind IN ({string.Join(",", Enum.GetNames<ContractEventKind>().Select(name => $"'{name}'"))})");

                // Removing a party needs no written reason (spec Decision 33). Everything else does: the regex agrees with
                // string.IsNullOrWhiteSpace, as on recibo_annulments. The column is nullable and a CHECK passes on NULL, so it says so.
                t.HasCheckConstraint(
                    "ck_contract_events_reason_not_blank",
                    $"kind = '{nameof(ContractEventKind.PartyRemoved)}' OR (reason IS NOT NULL AND reason ~ '\\S')");
            });

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("ContractId").HasColumnName("contract_id").IsRequired();

            // 16: the longest member, PartyRemoved, is 12 characters.
            builder.IndexerProperty<string>("Kind").HasColumnName("kind").HasMaxLength(16).IsRequired();
            builder.IndexerProperty<string?>("Reason").HasColumnName("reason").HasColumnType("text");
            builder.IndexerProperty<string?>("Detail").HasColumnName("detail").HasColumnType("text");

            // Stamped by the contract_events_stamp trigger, which overwrites whatever an INSERT supplies.
            builder.IndexerProperty<string>("RecordedBy")
                .HasColumnName("recorded_by")
                .HasColumnType("name")
                .ValueGeneratedOnAdd()
                .IsRequired();
            builder.IndexerProperty<DateTimeOffset>("RecordedAt")
                .HasColumnName("recorded_at")
                .HasColumnType("timestamp with time zone")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.HasOne(typeof(Contract), null)
                .WithMany()
                .HasForeignKey("ContractId")
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex("ContractId").HasDatabaseName("ix_contract_events_contract");

            AppendOnly.Apply(builder);
        });
    }
}
