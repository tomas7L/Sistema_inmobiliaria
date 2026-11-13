using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// revocar-permisos-api spec Requirements 1 to 3: Supabase's API roles (<c>anon</c>, <c>authenticated</c>, <c>service_role</c>) hold
    /// nothing in public, now or on what a migration creates later, and the operator functions are closed to <c>PUBLIC</c>. Closes
    /// <c>notificaciones/design.md</c> Finding F2: until now every table was granted to those roles and only the disabled Data API kept
    /// them out. After applying it, the F2 query in the SQL editor must return no rows. No model change; the snapshot is unchanged.
    /// </summary>
    public partial class RevokeSupabaseApiRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres finds every table, sequence and function in public at this moment, app_users and __EFMigrationsHistory
            // included, so none is missed by a list (design D2). The default privileges are the migrating role's own, postgres on
            // Supabase, so no FOR ROLE (design D4). Those roles exist only on Supabase, and naming a missing role is an error, so
            // every statement sits inside the guarded loop (design D3). Revoking what is not held is a no-op, so this reaches the same
            // end state whether or not Supabase has already changed its defaults.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    api_role text;
                BEGIN
                    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
                            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I', api_role);
                            EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I', api_role);
                            EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %I', api_role);
                            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM %I', api_role);
                            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM %I', api_role);
                            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM %I', api_role);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);

            // Every role inherits what PUBLIC holds, so an API role could still run these after losing its own grant (spec Decision
            // 9). The group roles' explicit grants (AddUsersAndRoles) stay. Trigger functions keep PUBLIC: they cannot be called
            // directly (design D6).
            migrationBuilder.Sql(
                """
                REVOKE ALL ON FUNCTION app_set_role_password(name, text) FROM PUBLIC;
                REVOKE ALL ON FUNCTION app_create_login_role(name, text, name) FROM PUBLIC;
                REVOKE ALL ON FUNCTION app_set_role_login(name, boolean) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing is restored on purpose: rolling back must not reopen the database to the API roles or to PUBLIC (spec
            // Requirement 5), as HardenClearMustChangePassword already decided.
        }
    }
}
