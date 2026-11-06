using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractEventsAndTermsLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "name", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_events", x => x.id);
                    table.CheckConstraint("ck_contract_events_kind", "kind IN ('Cancelled','NoticeUndone','EndUndone','PartyRemoved')");
                    table.CheckConstraint("ck_contract_events_reason_not_blank", "kind = 'PartyRemoved' OR (reason IS NOT NULL AND reason ~ '\\S')");
                    table.ForeignKey(
                        name: "fk_contract_events_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_events_contract",
                table: "contract_events",
                column: "contract_id");

            // Who and when are the database's (design D12), as on recibo_annulments: an INSERT cannot supply them. Not SECURITY
            // DEFINER, so current_user is the logged-in role.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION stamp_contract_event() RETURNS trigger AS $$
                BEGIN
                    NEW.recorded_at := now();
                    NEW.recorded_by := current_user;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contract_events_stamp
                    BEFORE INSERT ON contract_events
                    FOR EACH ROW EXECUTE FUNCTION stamp_contract_event();

                CREATE TRIGGER contract_events_append_only
                    BEFORE UPDATE OR DELETE ON contract_events
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();

                GRANT SELECT, INSERT ON contract_events TO inmobiliaria_admin, inmobiliaria_empleado;
                """);

            // The terms an accrual was computed from, locked the way the fee plan is (design D16). monthly_rent stays out:
            // a confirmed adjustment legitimately changes it, so the port and the domain guard it.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION reject_contract_terms_change_after_movement() RETURNS trigger AS $$
                BEGIN
                    IF (NEW.start_date IS DISTINCT FROM OLD.start_date
                        OR NEW.nominal_end_date IS DISTINCT FROM OLD.nominal_end_date
                        OR NEW.due_day IS DISTINCT FROM OLD.due_day)
                       AND EXISTS (
                            SELECT 1
                            FROM account_movements m
                            JOIN contract_accounts a ON a.id = m.account_id
                            WHERE a.contract_id = NEW.id)
                    THEN
                        RAISE EXCEPTION
                            'The terms of contract % cannot change once its account holds a movement', NEW.id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_terms_locked';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contracts_terms_lock
                    BEFORE UPDATE OF start_date, nominal_end_date, due_day ON contracts
                    FOR EACH ROW EXECUTE FUNCTION reject_contract_terms_change_after_movement();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER contracts_terms_lock ON contracts;
                DROP FUNCTION reject_contract_terms_change_after_movement();
                """);

            migrationBuilder.DropTable(
                name: "contract_events");

            migrationBuilder.Sql("DROP FUNCTION stamp_contract_event();");
        }
    }
}
