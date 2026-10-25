using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written (live smoke H.5, 2026-10-24): completing a forced password change never cleared the
    /// requirement, so it was asked again at every login. No EF model change: generated on the unchanged model,
    /// so the snapshot is untouched and only <c>Up</c> and <c>Down</c> were filled in.
    /// </summary>
    public partial class FixClearMustChangePasswordIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // app_clear_must_change_password is SECURITY DEFINER, and inside a definer function current_user is
            // the function's OWNER, not the caller: the original body (AddUsersAndRoles) matched the owner's
            // name and updated no row. session_user is the role that logged in, which no SET ROLE can change,
            // so the function still touches exactly the caller's own row. CREATE OR REPLACE keeps the owner,
            // SECURITY DEFINER, the pinned search_path and the existing EXECUTE grants.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION app_clear_must_change_password() RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
                BEGIN
                    UPDATE app_users SET must_change_password = false WHERE username = session_user;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION app_clear_must_change_password() RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
                BEGIN
                    UPDATE app_users SET must_change_password = false WHERE username = current_user;
                END
                $$;
                """);
        }
    }
}
