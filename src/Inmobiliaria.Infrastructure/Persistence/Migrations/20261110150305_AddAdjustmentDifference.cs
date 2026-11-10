using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M7 (gestion-de-alquileres design §12, D5, D6, D7). Admits the AdjustmentDifference kind on the ledger and on recibo lines,
    /// requires its period and a positive amount, and records on each adjustment the difference offered and the charge written,
    /// one charge per adjustment. No unique index per period on the new kind: two late steps may land on one period (D7).
    /// Additive: no existing row changes, and every existing row satisfies the widened checks.
    /// </summary>
    public partial class AddAdjustmentDifference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recibo_lines_kind",
                table: "recibo_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements");

            migrationBuilder.AddColumn<decimal>(
                name: "difference_amount",
                table: "rent_adjustments",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "difference_movement_id",
                table: "rent_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_rent_adjustments_difference_movement",
                table: "rent_adjustments",
                column: "difference_movement_id",
                unique: true,
                filter: "difference_movement_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rent_adjustments_difference_amount_positive",
                table: "rent_adjustments",
                sql: "difference_amount IS NULL OR difference_amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rent_adjustments_difference_charged_was_offered",
                table: "rent_adjustments",
                sql: "difference_movement_id IS NULL OR difference_amount IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recibo_lines_kind",
                table: "recibo_lines",
                sql: "kind IN ('Rent','ContractualFeeInstalment','Recargo','AdjustmentDifference','TasaMunicipal','Seguro','OtrosConceptos','SaldoDeudor','HonorariosAdministracion')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_adjustment_difference_sign",
                table: "account_movements",
                sql: "kind <> 'AdjustmentDifference' OR amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements",
                sql: "kind IN ('OpeningBalance','RentAccrual','ContractualFeeInstalment','RecargoFrozen','Correction','Payment','RecargoCharged','AdjustmentDifference')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements",
                sql: "kind NOT IN ('RentAccrual','ContractualFeeInstalment','Payment','RecargoCharged','AdjustmentDifference') OR period IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Once a difference is on the ledger or a recibo, those append-only rows cannot be removed and the narrower checks
            // below could not be restored: refuse before changing anything, so the only way back is forward.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM account_movements WHERE kind = 'AdjustmentDifference')
                       OR EXISTS (SELECT 1 FROM recibo_lines WHERE kind = 'AdjustmentDifference') THEN
                        RAISE EXCEPTION 'AddAdjustmentDifference cannot be rolled back: adjustment differences are already charged';
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_rent_adjustments_difference_movement",
                table: "rent_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rent_adjustments_difference_amount_positive",
                table: "rent_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rent_adjustments_difference_charged_was_offered",
                table: "rent_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recibo_lines_kind",
                table: "recibo_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_adjustment_difference_sign",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements");

            migrationBuilder.DropColumn(
                name: "difference_amount",
                table: "rent_adjustments");

            migrationBuilder.DropColumn(
                name: "difference_movement_id",
                table: "rent_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recibo_lines_kind",
                table: "recibo_lines",
                sql: "kind IN ('Rent','ContractualFeeInstalment','Recargo','TasaMunicipal','Seguro','OtrosConceptos','SaldoDeudor','HonorariosAdministracion')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_kind",
                table: "account_movements",
                sql: "kind IN ('OpeningBalance','RentAccrual','ContractualFeeInstalment','RecargoFrozen','Correction','Payment','RecargoCharged')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_movements_period_required",
                table: "account_movements",
                sql: "kind NOT IN ('RentAccrual','ContractualFeeInstalment','Payment','RecargoCharged') OR period IS NOT NULL");
        }
    }
}
