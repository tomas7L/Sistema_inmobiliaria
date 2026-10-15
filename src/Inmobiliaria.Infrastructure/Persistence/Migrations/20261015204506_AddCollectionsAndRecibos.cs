using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionsAndRecibos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<short>(type: "smallint", nullable: false),
                    payment_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    recargo_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "name", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collections", x => x.id);
                    table.CheckConstraint("ck_collections_attempt_positive", "attempt >= 1");
                    table.CheckConstraint("ck_collections_period_first_of_month", "EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_collections_account_movements_payment_movement_id",
                        column: x => x.payment_movement_id,
                        principalTable: "account_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collections_account_movements_recargo_movement_id",
                        column: x => x.recargo_movement_id,
                        principalTable: "account_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collections_contract_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "contract_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recibo_numbering",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: true),
                    starting_number = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibo_numbering", x => x.id);
                    table.CheckConstraint("ck_recibo_numbering_single_row", "id = 1");
                    table.CheckConstraint("ck_recibo_numbering_starting_positive", "starting_number IS NULL OR starting_number > 0");
                });

            migrationBuilder.CreateTable(
                name: "recibos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    copy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    legend = table.Column<string>(type: "text", nullable: false),
                    lessor_names = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_name = table.Column<string>(type: "text", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    units_description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibos", x => x.id);
                    table.CheckConstraint("ck_recibos_copy", "copy IN ('Locatario','Propietario')");
                    table.CheckConstraint("ck_recibos_number_positive", "number > 0");
                    table.ForeignKey(
                        name: "fk_recibos_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recibo_annulments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    annulled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    annulled_by = table.Column<string>(type: "name", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    recibo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibo_annulments", x => x.id);
                    table.CheckConstraint("ck_recibo_annulments_reason_not_blank", "reason ~ '\\S'");
                    table.ForeignKey(
                        name: "fk_recibo_annulments_recibos_recibo_id",
                        column: x => x.recibo_id,
                        principalTable: "recibos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recibo_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    recibo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibo_lines", x => x.id);
                    table.CheckConstraint("ck_recibo_lines_honorarios_not_positive", "kind <> 'HonorariosAdministracion' OR amount <= 0");
                    table.CheckConstraint("ck_recibo_lines_kind", "kind IN ('Rent','ContractualFeeInstalment','Recargo','TasaMunicipal','Seguro','OtrosConceptos','SaldoDeudor','HonorariosAdministracion')");
                    table.CheckConstraint("ck_recibo_lines_tasa_not_negative", "kind <> 'TasaMunicipal' OR amount >= 0");
                    table.ForeignKey(
                        name: "fk_recibo_lines_recibos_recibo_id",
                        column: x => x.recibo_id,
                        principalTable: "recibos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_collections_live_attempt",
                table: "collections",
                columns: new[] { "account_id", "period", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collections_payment_movement",
                table: "collections",
                column: "payment_movement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collections_recargo_movement",
                table: "collections",
                column: "recargo_movement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recibo_annulments_recibo",
                table: "recibo_annulments",
                column: "recibo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recibo_lines_kind",
                table: "recibo_lines",
                columns: new[] { "recibo_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recibo_lines_position",
                table: "recibo_lines",
                columns: new[] { "recibo_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recibos_collection_copy",
                table: "recibos",
                columns: new[] { "collection_id", "copy" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recibos_number",
                table: "recibos",
                column: "number",
                unique: true);

            // Hand-written from here (cobranza-y-recibos design sections 3, 5 and 6). The generated part
            // above is kept as generated.
            //
            // The single counter row exists from this migration, with both numbers NULL until an Admin
            // sets the cutover number (design D5, D7). It is created here, not by the application, so
            // there is exactly one row and nothing can race to create it.
            migrationBuilder.Sql("INSERT INTO recibo_numbering (id) VALUES (1);");

            // The append-only rule on the four document tables: one shared function, the shape
            // account_movements uses, with the refusal raised as P0001 carrying CONSTRAINT =
            // '<table>_append_only' so the adapter reads PostgresException.ConstraintName and never parses
            // message text (design section 2). Per row, plain BEFORE UPDATE OR DELETE triggers: the rule
            // needs no commit-time view of the transaction.
            //
            // Known accepted gap, the same one every append-only table here carries: a superuser can
            // ALTER TABLE ... DISABLE TRIGGER. Named, not closed.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reject_recibo_table_mutation() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        '% rows are append-only and cannot be updated or deleted (id %)', TG_TABLE_NAME, OLD.id
                        USING ERRCODE = 'P0001', CONSTRAINT = TG_TABLE_NAME || '_append_only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER collections_append_only
                    BEFORE UPDATE OR DELETE ON collections
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();
                CREATE TRIGGER recibos_append_only
                    BEFORE UPDATE OR DELETE ON recibos
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();
                CREATE TRIGGER recibo_lines_append_only
                    BEFORE UPDATE OR DELETE ON recibo_lines
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();
                CREATE TRIGGER recibo_annulments_append_only
                    BEFORE UPDATE OR DELETE ON recibo_annulments
                    FOR EACH ROW EXECUTE FUNCTION reject_recibo_table_mutation();
                """);

            // collections_stamp. What the database says and the caller cannot:
            //  - recorded_at and recorded_by are overwritten, so a supplied value is ignored (D10, spec
            //    Test 14). current_user is the connected login role, which is why none of the functions in
            //    this migration is SECURITY DEFINER: inside one, current_user would be the function's
            //    owner and every stamp and the voiding role check below would name the wrong person.
            //  - the received date may not be after the agency-local today, nor more than 7 days before it
            //    (D14, D16). "Local today" is the Buenos Aires date, from now(), the transaction start, so
            //    one payment carries one consistent instant. The 7 is CollectionRules.BackDateBoundDays;
            //    a test probes the boundary so the two cannot drift.
            //  - the attempt rule (design section 4): an attempt must be the number of collections the
            //    period already has plus one, and nothing may be inserted while an earlier collection of
            //    the period is live, meaning none of its recibos carries an annulment. Two operators who
            //    both compute attempt 1 pass this together and collide on ux_collections_live_attempt.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION stamp_collection() RETURNS trigger AS $$
                DECLARE
                    local_today date := (now() AT TIME ZONE 'America/Argentina/Buenos_Aires')::date;
                    earlier integer;
                BEGIN
                    NEW.recorded_at := now();
                    NEW.recorded_by := current_user;

                    IF NEW.received_on > local_today THEN
                        RAISE EXCEPTION 'The received date % is after today (%)', NEW.received_on, local_today
                            USING ERRCODE = 'P0001', CONSTRAINT = 'collections_received_on_future';
                    END IF;

                    IF NEW.received_on < local_today - 7 THEN
                        RAISE EXCEPTION 'The received date % is more than 7 days before today (%)', NEW.received_on, local_today
                            USING ERRCODE = 'P0001', CONSTRAINT = 'collections_received_on_bound';
                    END IF;

                    SELECT count(*) INTO earlier
                    FROM collections c
                    WHERE c.account_id = NEW.account_id AND c.period = NEW.period;

                    IF NEW.attempt <> earlier + 1
                       OR EXISTS (
                            SELECT 1
                            FROM collections c
                            WHERE c.account_id = NEW.account_id
                              AND c.period = NEW.period
                              AND NOT EXISTS (
                                    SELECT 1
                                    FROM recibos r
                                    JOIN recibo_annulments a ON a.recibo_id = r.id
                                    WHERE r.collection_id = c.id))
                    THEN
                        RAISE EXCEPTION 'Attempt % is not the next attempt for period % of account %', NEW.attempt, NEW.period, NEW.account_id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'collections_attempt_rule';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER collections_stamp
                    BEFORE INSERT ON collections
                    FOR EACH ROW EXECUTE FUNCTION stamp_collection();
                """);

            // recibo_annulments_stamp: who and when are the database's (D10), and so is the voiding window
            // (D15), measured from the day the collection was recorded, agency-local. Both application
            // roles keep INSERT on this table, so the limit lives here and also stops raw SQL:
            //  - more than 30 days after the payment was recorded: nobody (recibo_annulments_window_closed);
            //  - more than 7 days: Admin only, tested with pg_has_role on current_user
            //    (recibo_annulments_admin_only). A superuser is a member of every role, so a test that
            //    wants to prove the Empleado limit must connect as a real login role (spec Test 72).
            // The 7 and the 30 are CollectionRules.VoidingFreeWindowDays and VoidingAdminWindowDays, and a
            // test probes both boundaries. An unknown recibo is left to the foreign key to refuse.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION stamp_recibo_annulment() RETURNS trigger AS $$
                DECLARE
                    local_today date := (now() AT TIME ZONE 'America/Argentina/Buenos_Aires')::date;
                    recorded_on date;
                    age integer;
                BEGIN
                    NEW.annulled_at := now();
                    NEW.annulled_by := current_user;

                    SELECT (c.recorded_at AT TIME ZONE 'America/Argentina/Buenos_Aires')::date
                    INTO recorded_on
                    FROM recibos r
                    JOIN collections c ON c.id = r.collection_id
                    WHERE r.id = NEW.recibo_id;

                    IF recorded_on IS NULL THEN
                        RETURN NEW;
                    END IF;

                    age := local_today - recorded_on;

                    IF age > 30 THEN
                        RAISE EXCEPTION 'A payment recorded % days ago can no longer be voided', age
                            USING ERRCODE = 'P0001', CONSTRAINT = 'recibo_annulments_window_closed';
                    END IF;

                    IF age > 7 AND NOT pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER') THEN
                        RAISE EXCEPTION 'A payment recorded % days ago can be voided by an Admin only', age
                            USING ERRCODE = 'P0001', CONSTRAINT = 'recibo_annulments_admin_only';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER recibo_annulments_stamp
                    BEFORE INSERT ON recibo_annulments
                    FOR EACH ROW EXECUTE FUNCTION stamp_recibo_annulment();
                """);

            // recibo_numbering_cutover (design section 3, D7). Setting or changing starting_number sets
            // next_number to it, and is refused once any recibo exists; so the number may be set and
            // corrected freely until the first recibo, and the first recibo carries exactly that number.
            // next_number cannot move while there is no starting number: no issuance before cutover.
            //
            // The EXISTS is a new statement under READ COMMITTED, so an Admin correcting the number while
            // the first issuance is in flight waits on the row lock, then sees the committed recibo and is
            // refused. Naming starting_number without changing it is not a change.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION guard_recibo_numbering() RETURNS trigger AS $$
                BEGIN
                    IF NEW.starting_number IS DISTINCT FROM OLD.starting_number THEN
                        IF EXISTS (SELECT 1 FROM recibos) THEN
                            RAISE EXCEPTION 'The starting recibo number cannot change once a recibo exists'
                                USING ERRCODE = 'P0001', CONSTRAINT = 'recibo_numbering_cutover_locked';
                        END IF;

                        NEW.next_number := NEW.starting_number;
                    ELSIF NEW.next_number IS DISTINCT FROM OLD.next_number AND NEW.starting_number IS NULL THEN
                        RAISE EXCEPTION 'No recibo can be numbered before the starting number is set'
                            USING ERRCODE = 'P0001', CONSTRAINT = 'recibo_numbering_not_started';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER recibo_numbering_cutover
                    BEFORE UPDATE ON recibo_numbering
                    FOR EACH ROW EXECUTE FUNCTION guard_recibo_numbering();
                """);

            // Grants, in the same migration (convention new-table-ships-with-grants), explicit rather than
            // leaning on the default privileges of an earlier migration, which bind to whoever ran it.
            //
            // The four document tables are insert-only: SELECT and INSERT, no UPDATE, no DELETE, so the
            // append-only rule is enforced three times over (no mutator in the model, the trigger, and a
            // privilege never granted). The counter is the one table that is updated by design, and only in
            // its two columns: both roles advance next_number, and only Admin may set starting_number. A
            // column-level UPDATE grant is how the database, not the application, says "only Admin sets the
            // cutover" while both roles keep issuing recibos. Neither role can insert, delete or touch id.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT ON collections, recibos, recibo_lines, recibo_annulments
                    TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT SELECT ON recibo_numbering TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT UPDATE (next_number) ON recibo_numbering TO inmobiliaria_admin, inmobiliaria_empleado;
                GRANT UPDATE (starting_number) ON recibo_numbering TO inmobiliaria_admin;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers and functions first, explicitly, the way AddContractAccounts does it: dropping the
            // tables would take their triggers with them but leave the functions behind, and a Down that
            // leaves debris is a Down nobody can trust to have run. The grants go with the tables.
            //
            // Lossless only before the first real recibo: after that, Down destroys the document history.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS recibo_numbering_cutover ON recibo_numbering;
                DROP FUNCTION IF EXISTS guard_recibo_numbering();
                DROP TRIGGER IF EXISTS recibo_annulments_stamp ON recibo_annulments;
                DROP FUNCTION IF EXISTS stamp_recibo_annulment();
                DROP TRIGGER IF EXISTS collections_stamp ON collections;
                DROP FUNCTION IF EXISTS stamp_collection();
                DROP TRIGGER IF EXISTS recibo_annulments_append_only ON recibo_annulments;
                DROP TRIGGER IF EXISTS recibo_lines_append_only ON recibo_lines;
                DROP TRIGGER IF EXISTS recibos_append_only ON recibos;
                DROP TRIGGER IF EXISTS collections_append_only ON collections;
                DROP FUNCTION IF EXISTS reject_recibo_table_mutation();
                """);

            migrationBuilder.DropTable(
                name: "recibo_annulments");

            migrationBuilder.DropTable(
                name: "recibo_lines");

            migrationBuilder.DropTable(
                name: "recibo_numbering");

            migrationBuilder.DropTable(
                name: "recibos");

            migrationBuilder.DropTable(
                name: "collections");
        }
    }
}
