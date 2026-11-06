using Inmobiliaria.Domain.Indices;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>index_value_corrections</c>, append-only (gestion-de-alquileres spec Decision 42, design D26): what a change to an index
/// level overwrote, since <c>index_values</c> keeps only the latest. The database's own trigger writes each row, and who and
/// when are the database's, as on <c>contract_events</c>. Nothing here adds a row through EF.
/// </summary>
public static class IndexValueCorrectionConfiguration
{
    public const string EntityName = "index_value_corrections";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable(EntityName);

            builder.IndexerProperty<Guid>("Id").HasColumnName("id");
            builder.HasKey("Id");

            builder.IndexerProperty<Guid>("IndexValueId").HasColumnName("index_value_id").IsRequired();

            // The same precision as index_values.level, so a record holds exactly what it overwrote.
            builder.IndexerProperty<decimal>("PreviousLevel").HasColumnName("previous_level").HasPrecision(18, 6).IsRequired();
            builder.IndexerProperty<decimal>("NewLevel").HasColumnName("new_level").HasPrecision(18, 6).IsRequired();

            // Stamped by the index_value_corrections_stamp trigger, which overwrites whatever an INSERT supplies.
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

            builder.HasOne(typeof(IndexValue), null)
                .WithMany()
                .HasForeignKey("IndexValueId")
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex("IndexValueId").HasDatabaseName("ix_index_value_corrections_index_value");

            AppendOnly.Apply(builder);
        });
    }
}
