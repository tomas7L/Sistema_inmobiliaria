using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// notificaciones T1 (design §3.1, §3.2, D4): the sent record. Unique per contract, kind and month; append-only by its own
    /// trigger, as the recibo tables are; who sent it and when stamped by the database; SELECT and INSERT for both roles, and
    /// revoked from Supabase's API roles wherever they exist. Additive: no existing table or row changes.
    /// </summary>
    public partial class AddTenantNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notice_month = table.Column<DateOnly>(type: "date", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    figures_on = table.Column<DateOnly>(type: "date", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    recargo = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "name", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_notices_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_notices_contract_kind_month",
                table: "tenant_notices",
                columns: new[] { "contract_id", "kind", "notice_month" },
                unique: true);

            // Append-only, the recibo tables' shape (AddCollectionsAndRecibos): P0001 carrying CONSTRAINT =
            // 'tenant_notices_append_only'. Its own function, since the recibo one says recibo in its name. A superuser can still
            // ALTER TABLE ... DISABLE TRIGGER: the same accepted gap every append-only table here carries.
            //
            // The stamp: recorded_at and recorded_by are overwritten, so a supplied value is ignored (spec Requirement 7). Not
            // SECURITY DEFINER: inside one, current_user would be the function's owner, never the operator who sent the notice.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_tenant_notice_mutation() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'tenant_notices rows are append-only and cannot be updated or deleted (id %)', OLD.id
                        USING ERRCODE = 'P0001', CONSTRAINT = 'tenant_notices_append_only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER tenant_notices_append_only
                    BEFORE UPDATE OR DELETE ON tenant_notices
                    FOR EACH ROW EXECUTE FUNCTION reject_tenant_notice_mutation();

                CREATE OR REPLACE FUNCTION stamp_tenant_notice() RETURNS trigger AS $$
                BEGIN
                    NEW.recorded_at := now();
                    NEW.recorded_by := current_user;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER tenant_notices_stamp
                    BEFORE INSERT ON tenant_notices
                    FOR EACH ROW EXECUTE FUNCTION stamp_tenant_notice();

                GRANT SELECT, INSERT ON tenant_notices TO inmobiliaria_admin, inmobiliaria_empleado;
                """);

            // Supabase's default privileges grant every new table in public to its API roles (design F2), and the Data API would
            // expose it to whoever holds the project's public key. Those roles exist only there, so a plain REVOKE would fail on any
            // other database, as in AddClauseWindowOffset.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    api_role text;
                BEGIN
                    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
                            EXECUTE format('REVOKE ALL ON TABLE tenant_notices FROM %I', api_role);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers and functions first, explicitly: dropping the table would take its triggers but leave the functions
            // behind. The grants go with the table, and so do the sent rows.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tenant_notices_stamp ON tenant_notices;
                DROP FUNCTION IF EXISTS stamp_tenant_notice();
                DROP TRIGGER IF EXISTS tenant_notices_append_only ON tenant_notices;
                DROP FUNCTION IF EXISTS reject_tenant_notice_mutation();
                """);

            migrationBuilder.DropTable(
                name: "tenant_notices");
        }
    }
}
