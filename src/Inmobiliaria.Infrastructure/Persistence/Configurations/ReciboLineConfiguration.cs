using Inmobiliaria.Domain.Collection;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// cobranza-y-recibos migration B: the <c>recibo_lines</c> table, the printed lines of one recibo in
/// print order. The kind is stored as text with a CHECK list built from <see cref="ReciboLineKind"/>, so
/// the list cannot drift from the enum: a new member changes this expression, the model then differs from
/// the last migration, and has-pending-model-changes says so.
///
/// A property-bag entity for the reason given on <see cref="CollectionConfiguration"/>.
/// </summary>
public static class ReciboLineConfiguration
{
    public const string EntityName = "recibo_lines";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                t.HasCheckConstraint(
                    "ck_recibo_lines_kind",
                    $"kind IN ({string.Join(",", Enum.GetNames<ReciboLineKind>().Select(name => $"'{name}'"))})");

                // Sign rules by kind (design D11). Seguro and Otros Conceptos may be negative; the
                // municipal rate never; honorarios is a deduction, so never positive.
                t.HasCheckConstraint(
                    "ck_recibo_lines_tasa_not_negative",
                    "kind <> 'TasaMunicipal' OR amount >= 0");
                t.HasCheckConstraint(
                    "ck_recibo_lines_honorarios_not_positive",
                    "kind <> 'HonorariosAdministracion' OR amount <= 0");
            });

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("ReciboId").HasColumnName("recibo_id").IsRequired();
            builder.IndexerProperty<short>("Position").HasColumnName("position").IsRequired();

            // 32, as in account_movements: the longest member, HonorariosAdministracion, is 24 characters.
            builder.IndexerProperty<string>("Kind").HasColumnName("kind").HasMaxLength(32).IsRequired();
            builder.IndexerProperty<decimal>("Amount").HasColumnName("amount").HasPrecision(14, 2).IsRequired();

            builder.HasOne(ReciboConfiguration.EntityName, null)
                .WithMany()
                .HasForeignKey("ReciboId")
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex("ReciboId", "Position").IsUnique().HasDatabaseName("ux_recibo_lines_position");
            builder.HasIndex("ReciboId", "Kind").IsUnique().HasDatabaseName("ux_recibo_lines_kind");

            AppendOnly.Apply(builder);
        });
    }
}
