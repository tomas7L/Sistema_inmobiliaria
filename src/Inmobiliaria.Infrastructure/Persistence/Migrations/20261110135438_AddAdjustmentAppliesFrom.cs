using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M6 (gestion-de-alquileres design §12, D1, D8). Adds the period a confirmed adjustment applies from, nullable because
    /// rent_adjustments is append-only and no existing row can be backfilled, and the two indexes that refuse a double
    /// confirmation. <b>Creating either index fails if production already holds two Regular rows for one due date, or two
    /// corrections of one adjustment</b>: the two H.4 preflight queries are run first, each expecting no rows.
    /// </summary>
    public partial class AddAdjustmentAppliesFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "applies_from",
                table: "rent_adjustments",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_rent_adjustments_one_correction_per_adjustment",
                table: "rent_adjustments",
                column: "corrects_adjustment_id",
                unique: true,
                filter: "corrects_adjustment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rent_adjustments_one_regular_per_due_date",
                table: "rent_adjustments",
                columns: new[] { "contract_id", "effective_date" },
                unique: true,
                filter: "kind = 'Regular'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rent_adjustments_applies_from_first_of_month",
                table: "rent_adjustments",
                sql: "applies_from IS NULL OR EXTRACT(DAY FROM applies_from) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rent_adjustments_one_correction_per_adjustment",
                table: "rent_adjustments");

            migrationBuilder.DropIndex(
                name: "ux_rent_adjustments_one_regular_per_due_date",
                table: "rent_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rent_adjustments_applies_from_first_of_month",
                table: "rent_adjustments");

            migrationBuilder.DropColumn(
                name: "applies_from",
                table: "rent_adjustments");
        }
    }
}
