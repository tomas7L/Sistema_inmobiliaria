using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// <c>app_clear_must_change_password</c> runs as its owner, and the query of 2026-11-07 found it with a search_path that does not
    /// name pg_temp and executable by Supabase's API roles, on both databases. This applies to it what
    /// <c>AddClauseWindowOffset</c> applied to <c>app_replace_clause_indices</c>. The body, owner and grants to the two roles stay.
    /// </summary>
    public partial class HardenClearMustChangePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A search_path that does not name pg_temp searches it first: a caller's temporary app_users took the update.
            migrationBuilder.Sql(
                "ALTER FUNCTION app_clear_must_change_password() SET search_path = pg_catalog, public, pg_temp;");

            // Supabase's default privileges granted it to its API roles when it was created, which REVOKE FROM PUBLIC does not
            // touch. Those roles exist only there, so a plain REVOKE would fail on any other database.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    api_role text;
                BEGIN
                    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
                            EXECUTE format('REVOKE ALL ON FUNCTION app_clear_must_change_password() FROM %I', api_role);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the search_path goes back. The API roles' grant is not restored: it was never intended, and nothing used it.
            migrationBuilder.Sql(
                "ALTER FUNCTION app_clear_must_change_password() SET search_path = pg_catalog, public;");
        }
    }
}
