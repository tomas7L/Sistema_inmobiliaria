using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexValueCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "index_value_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    previous_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "name", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_value_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_index_value_corrections_index_values_index_value_id",
                        column: x => x.index_value_id,
                        principalTable: "index_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_index_value_corrections_index_value",
                table: "index_value_corrections",
                column: "index_value_id");

            // Reuses what contract_events does (design D26): who and when are the database's, and rows only accumulate. Not
            // SECURITY DEFINER, so current_user is the logged-in role, which is why both roles hold INSERT.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER index_value_corrections_stamp
                    BEFORE INSERT ON index_value_corrections
                    FOR EACH ROW EXECUTE FUNCTION stamp_contract_event();

                CREATE TRIGGER index_value_corrections_append_only
                    BEFORE UPDATE OR DELETE ON index_value_corrections
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();

                GRANT SELECT, INSERT ON index_value_corrections TO inmobiliaria_admin, inmobiliaria_empleado;
                """);

            // The database writes the record, not the port: OLD.level is what was really overwritten, even when two people
            // correct at once, and no code path can skip it (spec Decision 42).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION record_index_value_correction() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO index_value_corrections (id, index_value_id, previous_level, new_level)
                    VALUES (gen_random_uuid(), NEW.id, OLD.level, NEW.level);

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER index_values_record_correction
                    AFTER UPDATE OF level ON index_values
                    FOR EACH ROW
                    WHEN (OLD.level IS DISTINCT FROM NEW.level)
                    EXECUTE FUNCTION record_index_value_correction();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER index_values_record_correction ON index_values;
                DROP FUNCTION record_index_value_correction();
                """);

            migrationBuilder.DropTable(
                name: "index_value_corrections");
        }
    }
}
