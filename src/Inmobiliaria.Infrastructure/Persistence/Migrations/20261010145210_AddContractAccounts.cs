using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "due_day",
                table: "contracts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)10);

            migrationBuilder.CreateTable(
                name: "contract_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_accounts_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: true),
                    corrects_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_movements", x => x.id);
                    table.CheckConstraint("ck_account_movements_accrual_has_period", "kind <> 'RentAccrual' OR period IS NOT NULL");
                    table.CheckConstraint("ck_account_movements_kind_corrects", "(kind = 'Correction') = (corrects_movement_id IS NOT NULL)");
                    table.CheckConstraint("ck_account_movements_period_first_of_month", "period IS NULL OR EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_account_movements_account_movements_corrects_movement_id",
                        column: x => x.corrects_movement_id,
                        principalTable: "account_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_movements_contract_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "contract_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_due_day_range",
                table: "contracts",
                sql: "due_day BETWEEN 1 AND 31");

            migrationBuilder.CreateIndex(
                name: "ix_account_movements_corrects_movement_id",
                table: "account_movements",
                column: "corrects_movement_id");

            migrationBuilder.CreateIndex(
                name: "ux_account_movements_accrual_period",
                table: "account_movements",
                columns: new[] { "account_id", "period" },
                unique: true,
                filter: "kind = 'RentAccrual'");

            migrationBuilder.CreateIndex(
                name: "ix_contract_accounts_contract_id",
                table: "contract_accounts",
                column: "contract_id",
                unique: true);

            // Hand-written: the database backstop of the append-only ledger. EF's
            // PropertySaveBehavior.Throw catches application code; only this catches raw SQL,
            // which is exactly what the specification's scenario demands ("including by raw
            // SQL"). Copied from rent_adjustments_append_only rather than invented: one rule,
            // one mechanism, so a reader who understands one understands both.
            //
            // A plain BEFORE UPDATE OR DELETE trigger, not a constraint trigger: the rule is per
            // row and needs no commit-time view of the transaction.
            //
            // Known accepted gap, the same one rent_adjustments carries: a superuser can
            // ALTER TABLE ... DISABLE TRIGGER. Named, not closed.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_account_movement_mutation() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'account_movements rows are append-only and cannot be updated or deleted (id %)',
                        OLD.id;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER account_movements_append_only
                    BEFORE UPDATE OR DELETE ON account_movements
                    FOR EACH ROW EXECUTE FUNCTION reject_account_movement_mutation();
                """);

            // Hand-written: the new-table-ships-with-grants convention. A new table whose
            // privileges arrive in a later migration ships broken for both roles the moment
            // either one queries it.
            //
            // Granted explicitly rather than relying on the ALTER DEFAULT PRIVILEGES set by the
            // users-and-roles migration: that binds to whoever ran it, so leaning on it makes
            // these tables' reachability depend on which account applied an earlier migration.
            //
            // INSERT and SELECT only, and deliberately no UPDATE or DELETE on either table. That
            // matches rent_adjustments, and it means the append-only rule is enforced three times
            // over: the domain has no mutator, the trigger refuses the statement, and the role
            // was never granted the privilege to attempt it. An account row is likewise fixed at
            // birth — its id and its contract never change — so it needs no UPDATE either.
            migrationBuilder.Sql(
                """
                GRANT SELECT ON contract_accounts, account_movements
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT INSERT ON contract_accounts, account_movements
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped explicitly and first, the way the rent_adjustments migration does it.
            // Dropping the table would take its trigger with it but leave the function behind,
            // and a Down that leaves debris is a Down nobody can trust to have run.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS account_movements_append_only ON account_movements;
                DROP FUNCTION IF EXISTS reject_account_movement_mutation();
                """);

            migrationBuilder.DropTable(
                name: "account_movements");

            migrationBuilder.DropTable(
                name: "contract_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_due_day_range",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "due_day",
                table: "contracts");
        }
    }
}
