using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// cobranza-y-recibos migration B: the <c>recibo_annulments</c> table. A recibo is never edited or
/// deleted; voiding appends an annulment beside it, with the reason. Who annulled it and when are
/// stamped by the database (design D10), and the same trigger enforces the voiding window and the
/// role limit (D15), so raw SQL cannot sidestep either.
///
/// A property-bag entity for the reason given on <see cref="CollectionConfiguration"/>.
/// </summary>
public static class ReciboAnnulmentConfiguration
{
    public const string EntityName = "recibo_annulments";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                // At least one non-whitespace character. Design 5 writes btrim(reason) <> '', but btrim
                // trims spaces only, so a reason of tabs or newlines would pass while the domain
                // (string.IsNullOrWhiteSpace) refuses it. The regex agrees with the domain.
                t.HasCheckConstraint("ck_recibo_annulments_reason_not_blank", "reason ~ '\\S'");
            });

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("ReciboId").HasColumnName("recibo_id").IsRequired();
            builder.IndexerProperty<string>("Reason").HasColumnName("reason").HasColumnType("text").IsRequired();

            // Stamped by the recibo_annulments_stamp trigger, which overwrites whatever an INSERT supplies.
            builder.IndexerProperty<string>("AnnulledBy")
                .HasColumnName("annulled_by")
                .HasColumnType("name")
                .ValueGeneratedOnAdd()
                .IsRequired();
            builder.IndexerProperty<DateTimeOffset>("AnnulledAt")
                .HasColumnName("annulled_at")
                .HasColumnType("timestamp with time zone")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.HasOne(ReciboConfiguration.EntityName, null)
                .WithMany()
                .HasForeignKey("ReciboId")
                .OnDelete(DeleteBehavior.Restrict);

            // "Never annulled twice".
            builder.HasIndex("ReciboId").IsUnique().HasDatabaseName("ux_recibo_annulments_recibo");

            AppendOnly.Apply(builder);
        });
    }
}
