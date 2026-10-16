using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// cobranza-y-recibos migration B: <c>recibo_numbering</c>, the single row that numbers every recibo.
/// <c>starting_number</c> is the cutover parameter an Admin sets once; <c>next_number</c> is the live
/// counter, advanced by two inside the payment transaction with one raw
/// <c>UPDATE ... RETURNING</c> (design section 3). The row itself is created by the migration, not by
/// the application, and the migration's trigger keeps the two columns consistent.
///
/// There is no domain type for it: the counter is infrastructure, advanced by SQL, so it is a
/// property-bag entity (see <see cref="CollectionConfiguration"/>). Unlike the other four tables it is
/// updated by design, so the append-only loop does not apply to it.
/// </summary>
public static class ReciboNumberingConfiguration
{
    public const string EntityName = "recibo_numbering";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                // One row, ever: the key can only be 1.
                t.HasCheckConstraint("ck_recibo_numbering_single_row", "id = 1");
                t.HasCheckConstraint("ck_recibo_numbering_starting_positive", "starting_number IS NULL OR starting_number > 0");
            });

            // Never generated: left alone, a smallint key would become an identity column.
            builder.IndexerProperty<short>("Id").HasColumnName("id").ValueGeneratedNever();
            builder.HasKey("Id");

            builder.IndexerProperty<int?>("StartingNumber").HasColumnName("starting_number");
            builder.IndexerProperty<int?>("NextNumber").HasColumnName("next_number");
        });
    }
}
