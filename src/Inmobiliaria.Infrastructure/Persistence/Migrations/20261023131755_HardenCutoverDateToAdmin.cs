using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written (ui-de-cobranza design D6, section 6.2): the cutover date becomes Admin-only, enforced by
    /// the database. No EF model change: generated on the unchanged model, so the snapshot is untouched and
    /// only <c>Up</c> and <c>Down</c> were filled in.
    /// </summary>
    public partial class HardenCutoverDateToAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replaces only the body of the function behind contracts_cutover_lock; the trigger itself
            // (BEFORE UPDATE OF cutover_date) is left as AddFeePlanCutoverAndLedgerConstraints created it,
            // so a statement that does not name the column never reaches this function, and naming it
            // without changing it is still not a change (spec Requirement 22).
            //
            // The lock is checked first and the role second, so every existing caller sees exactly the
            // refusal it saw before. Deliberately NOT SECURITY DEFINER: current_user must be the logged-in
            // role, as in the annulment trigger. A superuser is a member of every role, so the migration
            // owner and the Testcontainers superuser still pass.
            //
            // A named limit (spec Decision 23): only UPDATE is hardened. An Empleado can still INSERT a
            // contract carrying a cutover date, but cannot change it afterwards.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_cutover_change_after_movement() RETURNS trigger AS $$
                BEGIN
                    IF NEW.cutover_date IS DISTINCT FROM OLD.cutover_date THEN
                        IF EXISTS (
                                SELECT 1
                                FROM account_movements m
                                JOIN contract_accounts a ON a.id = m.account_id
                                WHERE a.contract_id = NEW.id)
                        THEN
                            RAISE EXCEPTION
                                'The cutover date of contract % cannot change once its account holds a movement', NEW.id
                                USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_locked';
                        END IF;

                        IF NOT pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER') THEN
                            RAISE EXCEPTION
                                'Only an Administrator can change the cutover date of contract %', NEW.id
                                USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_admin_only';
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous body, verbatim from AddFeePlanCutoverAndLedgerConstraints. No data changes
            // either way.
            migrationBuilder.Sql(
                """
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
                """);
        }
    }
}
