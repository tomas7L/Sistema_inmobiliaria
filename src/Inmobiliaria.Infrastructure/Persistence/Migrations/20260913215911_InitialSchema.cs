using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    nominal_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notice_given_date = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_move_out_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    monthly_rent = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    honorarios_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.CheckConstraint("ck_contracts_end_reason", "end_reason IS NULL OR end_reason IN ('Expiry', 'EarlyTerminationByTenant', 'TerminationForCause', 'MutualAgreement')");
                    table.CheckConstraint("ck_contracts_honorarios_percentage_range", "honorarios_percentage IS NULL OR (honorarios_percentage >= 0 AND honorarios_percentage <= 100)");
                    table.CheckConstraint("ck_contracts_status", "status IN ('Active', 'PendingTermination', 'Ended')");
                });

            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    dni = table.Column<string>(type: "text", nullable: true),
                    cuil = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    domicile = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_street = table.Column<string>(type: "text", nullable: false),
                    address_number = table.Column<string>(type: "text", nullable: false),
                    address_floor = table.Column<string>(type: "text", nullable: true),
                    address_apartment = table.Column<string>(type: "text", nullable: true),
                    address_city = table.Column<string>(type: "text", nullable: false),
                    address_province = table.Column<string>(type: "text", nullable: false),
                    address_postal_code = table.Column<string>(type: "text", nullable: false),
                    unit_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_path = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_documents", x => x.id);
                    table.CheckConstraint("ck_contract_documents_kind", "kind IN ('Original', 'Addendum', 'TerminationNotice')");
                    table.ForeignKey(
                        name: "fk_contract_documents_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_parties",
                columns: table => new
                {
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_parties", x => new { x.contract_id, x.party_id, x.role });
                    table.CheckConstraint("ck_contract_parties_role", "role IN ('Lessor', 'Tenant', 'Codebtor')");
                    table.ForeignKey(
                        name: "fk_contract_parties_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_parties_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_units",
                columns: table => new
                {
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_percentage = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_units", x => new { x.contract_id, x.unit_id });
                    table.CheckConstraint("ck_contract_units_share_percentage_range", "share_percentage > 0 AND share_percentage <= 100");
                    table.ForeignKey(
                        name: "fk_contract_units_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_units_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_documents_contract_id",
                table: "contract_documents",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_parties_party_id",
                table: "contract_parties",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_units_unit_id",
                table: "contract_units",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_parties_cuil",
                table: "parties",
                column: "cuil",
                unique: true,
                filter: "cuil IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parties_dni",
                table: "parties",
                column: "dni",
                unique: true,
                filter: "dni IS NOT NULL");

            // Hand-written: design.md Decision 3, the cross-row rent-split invariant. A
            // per-row CHECK cannot express SUM(share_percentage) = 100 across the whole
            // contract, so this is a deferred constraint trigger instead.
            //
            // DEFERRABLE INITIALLY DEFERRED is load-bearing: EF Core inserts contract_units
            // rows one at a time within a single transaction, so an IMMEDIATE trigger would
            // fail on the first row of every valid two-or-more-unit contract. Deferring the
            // check to COMMIT is what makes a multi-row split constructible at all.
            //
            // Known accepted gap (design.md Decision 3, "Residual gap, accepted"): a row-level
            // trigger on contract_units can never fire for a contract with ZERO contract_units
            // rows, so the database permits that state. The domain forbids it — a Contract
            // cannot be constructed without at least one unit — and a statement-level guard on
            // contracts was rejected in design as over-engineering for two internal users. This
            // migration intentionally does not attempt to close that gap.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION assert_contract_share_sum() RETURNS trigger AS $$
                DECLARE
                    affected_contract_id uuid;
                    total numeric(9,6);
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        affected_contract_id := OLD.contract_id;
                    ELSE
                        affected_contract_id := NEW.contract_id;
                    END IF;

                    -- The parent contract may have been deleted in the same transaction
                    -- (ON DELETE CASCADE empties contract_units first); nothing to enforce
                    -- once the contract itself is gone.
                    IF NOT EXISTS (SELECT 1 FROM contracts WHERE id = affected_contract_id) THEN
                        RETURN NULL;
                    END IF;

                    SELECT COALESCE(SUM(share_percentage), 0) INTO total
                    FROM contract_units
                    WHERE contract_id = affected_contract_id;

                    IF total <> 100 THEN
                        RAISE EXCEPTION
                            'contract_units.share_percentage for contract % must sum to exactly 100 (got %)',
                            affected_contract_id, total;
                    END IF;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER contract_units_share_sum
                    AFTER INSERT OR UPDATE OR DELETE ON contract_units
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION assert_contract_share_sum();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS contract_units_share_sum ON contract_units;
                DROP FUNCTION IF EXISTS assert_contract_share_sum();
                """);

            migrationBuilder.DropTable(
                name: "contract_documents");

            migrationBuilder.DropTable(
                name: "contract_parties");

            migrationBuilder.DropTable(
                name: "contract_units");

            migrationBuilder.DropTable(
                name: "parties");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "units");
        }
    }
}
