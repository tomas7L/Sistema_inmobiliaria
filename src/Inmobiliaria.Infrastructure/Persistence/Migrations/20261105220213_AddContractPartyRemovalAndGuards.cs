using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPartyRemovalAndGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "contract_parties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_contract_parties_one_tenant",
                table: "contract_parties",
                column: "contract_id",
                unique: true,
                filter: "role = 'Tenant' AND removed_at IS NULL");

            // Closes the named limit at access-control/spec.md:177-180 (spec Requirement 21, design D17). Deliberately NOT
            // SECURITY DEFINER: current_user must be the logged-in role, as in reject_cutover_change_after_movement. A
            // superuser is a member of every role, so the migration owner and fixtures still pass. An insert with no date
            // never reaches the function. pg_has_role is qualified so that a login's search_path cannot shadow it.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION reject_cutover_insert_unless_admin() RETURNS trigger AS $$
                BEGIN
                    IF NOT pg_catalog.pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER') THEN
                        RAISE EXCEPTION
                            'Only an Administrator can insert contract % with a cutover date', NEW.id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_insert_admin_only';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contracts_cutover_insert_guard
                    BEFORE INSERT ON contracts
                    FOR EACH ROW
                    WHEN (NEW.cutover_date IS NOT NULL)
                    EXECUTE FUNCTION reject_cutover_insert_unless_admin();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER contracts_cutover_insert_guard ON contracts;
                DROP FUNCTION reject_cutover_insert_unless_admin();
                """);

            migrationBuilder.DropIndex(
                name: "ux_contract_parties_one_tenant",
                table: "contract_parties");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "contract_parties");
        }
    }
}
