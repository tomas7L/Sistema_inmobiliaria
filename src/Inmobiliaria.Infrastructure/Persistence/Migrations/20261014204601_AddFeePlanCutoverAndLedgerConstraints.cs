using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeePlanCutoverAndLedgerConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Generated, then kept as generated: the two objects this migration REPLACES
            // (ux_account_movements_accrual_period becomes ux_account_movements_period_charge, and
            // ck_account_movements_accrual_has_period becomes ck_account_movements_period_required),
            // the three contracts columns with their checks, and the new ledger checks and indexes.
            // Each is dropped before its replacement is created, and the old ones come back in Down.
            migrationBuilder.DropIndex(
                name: "ux_account_movements_accrual_period",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_accrual_has_period",
                table: "account_movements");

            migrationBuilder.AddColumn<decimal>(
                name: "contractual_fee_amount",
                table: "contracts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "contractual_fee_instalments",
                table: "contracts",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cutover_date",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_contractual_fee_amount_positive",
                table: "contracts",
                sql: "contractual_fee_amount IS NULL OR contractual_fee_amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_contractual_fee_both_or_neither",
                table: "contracts",
                sql: "(contractual_fee_amount IS NULL) = (contractual_fee_instalments IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_contractual_fee_instalments_range",
                table: "contracts",
                sql: "contractual_fee_instalments IS NULL OR contractual_fee_instalments BETWEEN 1 AND 6");

            migrationBuilder.CreateIndex(
                name: "ux_account_movements_instalment_period",
                table: "account_movements",
                columns: new[] { "account_id", "period" },
                unique: true,
                filter: "kind = 'ContractualFeeInstalment'");

            migrationBuilder.CreateIndex(
                name: "ux_account_movements_opening_zero",
                table: "account_movements",
                column: "account_id",
                unique: true,
                filter: "kind = 'OpeningBalance' AND period IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_account_movements_period_charge",
                table: "account_movements",
                columns: new[] { "account_id", "period" },
                unique: true,
                filter: "kind IN ('RentAccrual','OpeningBalance')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements",
                sql: "kind IN ('OpeningBalance','RentAccrual','ContractualFeeInstalment','RecargoFrozen','Correction','Payment','RecargoCharged')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_opening_balance_period",
                table: "account_movements",
                sql: "kind <> 'OpeningBalance' OR period IS NOT NULL OR amount = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_payment_sign",
                table: "account_movements",
                sql: "kind <> 'Payment' OR amount < 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements",
                sql: "kind NOT IN ('RentAccrual','ContractualFeeInstalment','Payment','RecargoCharged') OR period IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_recargo_charged_sign",
                table: "account_movements",
                sql: "kind <> 'RecargoCharged' OR amount > 0");

            // Hand-written: the database half of two one-way rules (cobranza-y-recibos design
            // section 5). The domain refuses both first (Contract.ChangeContractualFeePlan and
            // Contract.SetCutoverDate); these triggers are what survives somebody opening a query
            // window, the same two-layer shape the append-only ledger uses.
            //
            // Two triggers, one rule each, both keyed on ANY movement. The plan lock is stricter than
            // spec Decision 32 ("not after any instalment"), per review finding
            // R3-fee-plan-after-rent-silently-dropped: the materialiser skips an already-charged
            // period as a whole, so a plan entered after rent was written would never raise its
            // instalments. The function keeps its original name; renaming it would only churn Down.
            //
            // BEFORE UPDATE OF <columns> fires when a statement names a column, even to set the
            // value it already has, so each function compares OLD and NEW and refuses a real change
            // only. Setting the same value is not a change.
            //
            // Raised as P0001 with CONSTRAINT = '<name>' (design section 2), so the adapter reads
            // PostgresException.ConstraintName and never parses message text.
            //
            // Deliberately NOT SECURITY DEFINER: these run with the caller's privileges, and both
            // application roles already hold SELECT on the two tables they read. No new grants.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_fee_plan_change_after_movement() RETURNS trigger AS $$
                BEGIN
                    IF (NEW.contractual_fee_amount IS DISTINCT FROM OLD.contractual_fee_amount
                        OR NEW.contractual_fee_instalments IS DISTINCT FROM OLD.contractual_fee_instalments)
                       AND EXISTS (
                            SELECT 1
                            FROM account_movements m
                            JOIN contract_accounts a ON a.id = m.account_id
                            WHERE a.contract_id = NEW.id)
                    THEN
                        RAISE EXCEPTION
                            'The fee plan of contract % cannot change once its account holds a movement', NEW.id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_fee_plan_locked';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contracts_fee_plan_lock
                    BEFORE UPDATE OF contractual_fee_amount, contractual_fee_instalments ON contracts
                    FOR EACH ROW EXECUTE FUNCTION reject_fee_plan_change_after_movement();

                CREATE OR REPLACE FUNCTION reject_cutover_change_after_movement() RETURNS trigger AS $$
                BEGIN
                    IF NEW.cutover_date IS DISTINCT FROM OLD.cutover_date
                       AND EXISTS (
                            SELECT 1
                            FROM account_movements m
                            JOIN contract_accounts a ON a.id = m.account_id
                            WHERE a.contract_id = NEW.id)
                    THEN
                        RAISE EXCEPTION
                            'The cutover date of contract % cannot change once its account holds a movement', NEW.id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_locked';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contracts_cutover_lock
                    BEFORE UPDATE OF cutover_date ON contracts
                    FOR EACH ROW EXECUTE FUNCTION reject_cutover_change_after_movement();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers and functions first, explicitly, the way AddContractAccounts does it: dropping
            // the columns would take the triggers with them but leave the functions behind, and a
            // Down that leaves debris is a Down nobody can trust to have run.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS contracts_cutover_lock ON contracts;
                DROP FUNCTION IF EXISTS reject_cutover_change_after_movement();
                DROP TRIGGER IF EXISTS contracts_fee_plan_lock ON contracts;
                DROP FUNCTION IF EXISTS reject_fee_plan_change_after_movement();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_contractual_fee_amount_positive",
                table: "contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_contractual_fee_both_or_neither",
                table: "contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_contractual_fee_instalments_range",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ux_account_movements_instalment_period",
                table: "account_movements");

            migrationBuilder.DropIndex(
                name: "ux_account_movements_opening_zero",
                table: "account_movements");

            migrationBuilder.DropIndex(
                name: "ux_account_movements_period_charge",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_opening_balance_period",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_payment_sign",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_recargo_charged_sign",
                table: "account_movements");

            migrationBuilder.DropColumn(
                name: "contractual_fee_amount",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "contractual_fee_instalments",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "cutover_date",
                table: "contracts");

            migrationBuilder.CreateIndex(
                name: "ux_account_movements_accrual_period",
                table: "account_movements",
                columns: new[] { "account_id", "period" },
                unique: true,
                filter: "kind = 'RentAccrual'");

            // The two objects replaced in Up come back as they were. Rows written under the new
            // rules (a Payment, a period-less zero) stay: the old rules never forbade those kinds,
            // they simply did not know them.
            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_accrual_has_period",
                table: "account_movements",
                sql: "kind <> 'RentAccrual' OR period IS NOT NULL");
        }
    }
}
