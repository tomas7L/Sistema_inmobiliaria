using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// cobranza-y-recibos migration B: the <c>recibos</c> table, one row per printed copy. A payment issues
/// two (one per <c>copy</c>), numbered consecutively, the locatario taking the lower number. A recibo
/// stores what was printed, including the tenant, lessor and unit text as a snapshot (design D12) and
/// its total as issued; nothing recomputes any of it.
///
/// A property-bag entity for the reason given on <see cref="CollectionConfiguration"/>: the domain
/// <c>Recibo</c> is an immutable document built from a draft and a header, with no constructor EF can
/// bind and no id.
/// </summary>
public static class ReciboConfiguration
{
    public const string EntityName = "recibos";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName, t =>
            {
                t.HasCheckConstraint("ck_recibos_copy", "copy IN ('Locatario','Propietario')");
                t.HasCheckConstraint("ck_recibos_number_positive", "number > 0");
            });

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("CollectionId").HasColumnName("collection_id").IsRequired();
            builder.IndexerProperty<int>("Number").HasColumnName("number").IsRequired();
            builder.IndexerProperty<string>("Copy").HasColumnName("copy").HasMaxLength(16).IsRequired();

            // The contract is a snapshot like the text below it: no foreign key, because a recibo is a
            // document that outlives any later change to the row it was issued against.
            builder.IndexerProperty<Guid>("ContractId").HasColumnName("contract_id").IsRequired();
            builder.IndexerProperty<DateOnly>("Period").HasColumnName("period").IsRequired();
            builder.IndexerProperty<DateOnly>("ReceivedOn").HasColumnName("received_on").IsRequired();
            builder.IndexerProperty<string>("TenantName").HasColumnName("tenant_name").HasColumnType("text").IsRequired();
            builder.IndexerProperty<string>("LessorNames").HasColumnName("lessor_names").HasColumnType("text").IsRequired();
            builder.IndexerProperty<string>("UnitsDescription").HasColumnName("units_description").HasColumnType("text").IsRequired();
            builder.IndexerProperty<string>("Legend").HasColumnName("legend").HasColumnType("text").IsRequired();
            builder.IndexerProperty<decimal>("Total").HasColumnName("total").HasPrecision(14, 2).IsRequired();

            builder.HasOne(CollectionConfiguration.EntityName, null)
                .WithMany()
                .HasForeignKey("CollectionId")
                .OnDelete(DeleteBehavior.Restrict);

            // A recibo number is unique and never reused. Also the backstop for a counter somebody set
            // back by hand (spec Test 66): the next issuance fails here, loudly, instead of duplicating.
            builder.HasIndex("Number").IsUnique().HasDatabaseName("ux_recibos_number");
            builder.HasIndex("CollectionId", "Copy").IsUnique().HasDatabaseName("ux_recibos_collection_copy");

            AppendOnly.Apply(builder);
        });
    }
}
