using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedEconomicIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw rather than HasData: the test database already holds "ICL", and HasData would collide with it (design §4).
            // "Exists" means active, as ix_economic_indices_name_active does: a discontinued index frees its name.
            migrationBuilder.Sql(
                """
                INSERT INTO economic_indices (id, name)
                SELECT gen_random_uuid(), seeded.name
                FROM (VALUES ('IPC'), ('RIPTE'), ('ICL')) AS seeded(name)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM economic_indices existing
                    WHERE existing.name = seeded.name AND existing.discontinued_from IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes nothing: values and clauses may reference these rows, and the seed cannot tell which it added.
        }
    }
}
