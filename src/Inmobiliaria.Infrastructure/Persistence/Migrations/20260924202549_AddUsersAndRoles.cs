using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndRoles : Migration
    {
        /// <summary>
        /// Hard-coded legacy <c>AppUser</c> id, deliberately fixed (not generated) so <see cref="Down"/>
        /// can find and remove exactly this row (design.md Decision 8). No real agency data exists
        /// yet, so the backfill below is expected to touch zero rows in practice — the path still has
        /// to be right.
        /// </summary>
        private const string LegacyAppUserId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------------------
            // 1) app_users must exist before anything else in this migration can reference
            //    it: the legacy backfill row, the two schema deltas' FKs, and the GRANT set.
            // ---------------------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_users", x => x.id);
                    table.CheckConstraint("ck_app_users_username_lowercase", "username = lower(username)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_username",
                table: "app_users",
                column: "username",
                unique: true);

            // Seeded before the backfill below reads it via COALESCE. inactive so it can
            // never itself authenticate (design.md Decision 8).
            migrationBuilder.Sql(
                $"""
                INSERT INTO app_users (id, username, display_name, is_active, must_change_password)
                VALUES ('{LegacyAppUserId}'::uuid, 'legacy', 'Legacy uploader (pre-users-and-roles)', false, false);
                """);

            // ---------------------------------------------------------------------------
            // 2) contract_documents delta, in the exact order design.md Decision 8 requires:
            //    add nullable -> backfill by username match (or the legacy row) -> NOT NULL
            //    -> drop the old text column -> FK. uploaded_by (text) is still present when
            //    the backfill UPDATE below runs.
            // ---------------------------------------------------------------------------
            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "contract_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                $"""
                UPDATE contract_documents d
                SET uploaded_by_user_id = COALESCE(
                    (SELECT u.id FROM app_users u WHERE u.username = d.uploaded_by),
                    '{LegacyAppUserId}'::uuid);
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "contract_documents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "uploaded_by",
                table: "contract_documents");

            migrationBuilder.CreateIndex(
                name: "ix_contract_documents_uploaded_by_user_id",
                table: "contract_documents",
                column: "uploaded_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_contract_documents_app_users_uploaded_by_user_id",
                table: "contract_documents",
                column: "uploaded_by_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---------------------------------------------------------------------------
            // 3) rent_adjustments delta: nullable column, no UPDATE whatsoever. Every
            //    existing row's confirmed_by stays NULL forever under the append-only
            //    trigger (design.md Decision 8; spec test 32).
            // ---------------------------------------------------------------------------
            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by",
                table: "rent_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_rent_adjustments_confirmed_by",
                table: "rent_adjustments",
                column: "confirmed_by");

            migrationBuilder.AddForeignKey(
                name: "fk_rent_adjustments_app_users_confirmed_by",
                table: "rent_adjustments",
                column: "confirmed_by",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---------------------------------------------------------------------------
            // 4) Roles, idempotently (CREATE ROLE IF NOT EXISTS does not exist), NOLOGIN
            //    group roles (design.md Decision 9). Baseline: USAGE + SELECT on every
            //    table, including tables a later migration adds (ALTER DEFAULT PRIVILEGES,
            //    written WITHOUT FOR ROLE so it binds to the current user — the migration
            //    owner, identical on Supabase and under Testcontainers).
            // ---------------------------------------------------------------------------
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'inmobiliaria_admin') THEN
                        CREATE ROLE inmobiliaria_admin NOLOGIN;
                    END IF;
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'inmobiliaria_empleado') THEN
                        CREATE ROLE inmobiliaria_empleado NOLOGIN;
                    END IF;
                END
                $$;

                GRANT USAGE ON SCHEMA public TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO inmobiliaria_admin, inmobiliaria_empleado;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO inmobiliaria_admin, inmobiliaria_empleado;
                """);

            // Per-table write grants (design.md Decision 9's table). Identical for both
            // roles except app_users — the sole asymmetry, which is user governance.
            // Scope guard: this is the ONLY statement in this migration that names
            // "contracts"; there is no ALTER TABLE contracts anywhere (design Decision 5).
            migrationBuilder.Sql(
                """
                GRANT INSERT, UPDATE ON parties, units, contract_parties, contract_units, contract_documents
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT INSERT, UPDATE ON economic_indices, index_values, adjustment_clauses, adjustment_clause_indices
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT INSERT, UPDATE ON contracts TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT INSERT ON rent_adjustments, rent_adjustment_index_values TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT INSERT, UPDATE ON app_users TO inmobiliaria_admin;
                """);

            // ---------------------------------------------------------------------------
            // 5) Password/provisioning DDL functions (design.md Decision 3 and 6). None
            //    are SECURITY DEFINER except app_clear_must_change_password — the others
            //    run as the caller so PostgreSQL's own privilege check still applies.
            // ---------------------------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE FUNCTION app_set_role_password(target_role name, new_password text) RETURNS void
                LANGUAGE plpgsql AS $$
                BEGIN
                    EXECUTE format('ALTER ROLE %I PASSWORD %L', target_role, new_password);
                END
                $$;

                CREATE FUNCTION app_create_login_role(new_role name, new_password text, group_role name) RETURNS void
                LANGUAGE plpgsql AS $$
                BEGIN
                    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', new_role, new_password);
                    EXECUTE format('GRANT %I TO %I', group_role, new_role);
                END
                $$;

                CREATE FUNCTION app_set_role_login(target_role name, can_login boolean) RETURNS void
                LANGUAGE plpgsql AS $$
                BEGIN
                    EXECUTE format('ALTER ROLE %I %s', target_role, CASE WHEN can_login THEN 'LOGIN' ELSE 'NOLOGIN' END);
                END
                $$;

                -- The only SECURITY DEFINER object in the system (design.md Decision 6):
                -- affects exactly one boolean column of exactly one row, the caller's own,
                -- because current_user is resolved inside the definer's own execution, not
                -- passed as an argument an Empleado could substitute. search_path is pinned,
                -- the standard hardening for definer functions.
                CREATE FUNCTION app_clear_must_change_password() RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
                BEGIN
                    UPDATE app_users SET must_change_password = false WHERE username = current_user;
                END
                $$;

                REVOKE ALL ON FUNCTION app_clear_must_change_password() FROM PUBLIC;

                GRANT EXECUTE ON FUNCTION app_set_role_password(name, text)
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT EXECUTE ON FUNCTION app_clear_must_change_password()
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT EXECUTE ON FUNCTION app_create_login_role(name, text, name)
                    TO inmobiliaria_admin;
                GRANT EXECUTE ON FUNCTION app_set_role_login(name, boolean)
                    TO inmobiliaria_admin;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fixed order (design.md Migration/Rollout): restore uploaded_by as text and
            // write usernames back FIRST, then drop FKs/confirmed_by, then DROP FUNCTION,
            // then REVOKE ALL and DROP ROLE the two group roles only — never an individual
            // login role, whose password exists nowhere else. Rolling back reinstates the
            // stored credential; that is stated in the proposal and not softened here.
            migrationBuilder.AddColumn<string>(
                name: "uploaded_by",
                table: "contract_documents",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE contract_documents d
                SET uploaded_by = (SELECT u.username FROM app_users u WHERE u.id = d.uploaded_by_user_id);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "uploaded_by",
                table: "contract_documents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "fk_contract_documents_app_users_uploaded_by_user_id",
                table: "contract_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_rent_adjustments_app_users_confirmed_by",
                table: "rent_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_contract_documents_uploaded_by_user_id",
                table: "contract_documents");

            migrationBuilder.DropIndex(
                name: "ix_rent_adjustments_confirmed_by",
                table: "rent_adjustments");

            migrationBuilder.DropColumn(
                name: "confirmed_by",
                table: "rent_adjustments");

            migrationBuilder.DropColumn(
                name: "uploaded_by_user_id",
                table: "contract_documents");

            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS app_clear_must_change_password();
                DROP FUNCTION IF EXISTS app_set_role_login(name, boolean);
                DROP FUNCTION IF EXISTS app_create_login_role(name, text, name);
                DROP FUNCTION IF EXISTS app_set_role_password(name, text);
                """);

            migrationBuilder.Sql(
                """
                REVOKE ALL ON ALL TABLES IN SCHEMA public FROM inmobiliaria_admin, inmobiliaria_empleado;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE SELECT ON TABLES FROM inmobiliaria_admin, inmobiliaria_empleado;
                REVOKE USAGE ON SCHEMA public FROM inmobiliaria_admin, inmobiliaria_empleado;

                DROP ROLE IF EXISTS inmobiliaria_admin;
                DROP ROLE IF EXISTS inmobiliaria_empleado;
                """);

            migrationBuilder.DropTable(
                name: "app_users");
        }
    }
}
