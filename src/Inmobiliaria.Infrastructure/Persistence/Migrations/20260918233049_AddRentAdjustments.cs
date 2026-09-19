using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRentAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adjustment_clauses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    rounding_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_clauses", x => x.id);
                    table.CheckConstraint("ck_adjustment_clauses_interval_months_range", "interval_months BETWEEN 1 AND 60");
                    table.ForeignKey(
                        name: "fk_adjustment_clauses_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "economic_indices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    discontinued_from = table.Column<DateOnly>(type: "date", nullable: true),
                    successor_index_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economic_indices", x => x.id);
                    table.CheckConstraint("ck_economic_indices_successor_not_self", "successor_index_id <> id");
                });

            migrationBuilder.CreateTable(
                name: "rent_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previous_canon = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    new_canon = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    coefficient = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    corrects_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_adjustments", x => x.id);
                    table.CheckConstraint("ck_rent_adjustments_effective_date_first_of_month", "EXTRACT(DAY FROM effective_date) = 1");
                    table.CheckConstraint("ck_rent_adjustments_kind_corrects", "(kind = 'Correction') = (corrects_adjustment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_rent_adjustments_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_clause_indices",
                columns: table => new
                {
                    adjustment_clause_id = table.Column<Guid>(type: "uuid", nullable: false),
                    economic_index_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_clause_indices", x => new { x.adjustment_clause_id, x.economic_index_id });
                    table.ForeignKey(
                        name: "fk_adjustment_clause_indices_adjustment_clauses_adjustment_cla",
                        column: x => x.adjustment_clause_id,
                        principalTable: "adjustment_clauses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_adjustment_clause_indices_economic_indices_economic_index_id",
                        column: x => x.economic_index_id,
                        principalTable: "economic_indices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "index_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    economic_index_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_values", x => x.id);
                    table.CheckConstraint("ck_index_values_level_positive", "level > 0");
                    table.CheckConstraint("ck_index_values_period_first_of_month", "EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_index_values_economic_indices_economic_index_id",
                        column: x => x.economic_index_id,
                        principalTable: "economic_indices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rent_adjustment_index_values",
                columns: table => new
                {
                    referenced_index_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rent_adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_index_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_period = table.Column<DateOnly>(type: "date", nullable: false),
                    base_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    end_period = table.Column<DateOnly>(type: "date", nullable: false),
                    end_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    variation = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_adjustment_index_values", x => new { x.rent_adjustment_id, x.referenced_index_id });
                    table.ForeignKey(
                        name: "fk_rent_adjustment_index_values_rent_adjustments_rent_adjustme",
                        column: x => x.rent_adjustment_id,
                        principalTable: "rent_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_clause_indices_economic_index_id",
                table: "adjustment_clause_indices",
                column: "economic_index_id");

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_clauses_contract_id",
                table: "adjustment_clauses",
                column: "contract_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_economic_indices_name_active",
                table: "economic_indices",
                column: "name",
                unique: true,
                filter: "discontinued_from IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_index_values_economic_index_id_period",
                table: "index_values",
                columns: new[] { "economic_index_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rent_adjustments_contract_id",
                table: "rent_adjustments",
                column: "contract_id");

            // Hand-written: design.md Decision 6, the database backstop of append-only
            // adjustment history. Unlike contract_units_share_sum (a DEFERRABLE CONSTRAINT
            // TRIGGER, needed because that rule is cross-row and EF inserts rows one at a
            // time within a transaction), this rule is per row and needs no commit-time view:
            // a confirmed rent_adjustments row must never change, full stop, regardless of
            // what else happens in the same transaction. A plain BEFORE UPDATE OR DELETE
            // trigger is therefore the correct, simpler shape — not a constraint trigger.
            //
            // Known accepted gap (design.md Decision 6, "Residual gap, accepted"): a
            // superuser can ALTER TABLE ... DISABLE TRIGGER. Named, not closed.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_rent_adjustment_mutation() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'rent_adjustments rows are append-only and cannot be updated or deleted (id %)',
                        OLD.id;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER rent_adjustments_append_only
                    BEFORE UPDATE OR DELETE ON rent_adjustments
                    FOR EACH ROW EXECUTE FUNCTION reject_rent_adjustment_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS rent_adjustments_append_only ON rent_adjustments;
                DROP FUNCTION IF EXISTS reject_rent_adjustment_mutation();
                """);

            migrationBuilder.DropTable(
                name: "adjustment_clause_indices");

            migrationBuilder.DropTable(
                name: "index_values");

            migrationBuilder.DropTable(
                name: "rent_adjustment_index_values");

            migrationBuilder.DropTable(
                name: "adjustment_clauses");

            migrationBuilder.DropTable(
                name: "economic_indices");

            migrationBuilder.DropTable(
                name: "rent_adjustments");
        }
    }
}
