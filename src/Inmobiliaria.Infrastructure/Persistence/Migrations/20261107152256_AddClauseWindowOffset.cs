using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClauseWindowOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1, not the 0 EF scaffolds: every clause already there keeps today's window (spec Requirement 13, design D3). The model
            // declares no default on purpose, see AdjustmentClauseConfiguration.
            migrationBuilder.AddColumn<short>(
                name: "window_offset_months",
                table: "adjustment_clauses",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_clauses_window_offset_range",
                table: "adjustment_clauses",
                sql: "window_offset_months BETWEEN 0 AND 3");

            // A clause holds until the contract's first confirmed adjustment (spec Decision 32). In the database, because both roles
            // can write these tables directly, and a confirmed adjustment's window has to stay the one its clause states. The row lock
            // waits out an adjustment still being written, whose foreign key holds a key-share lock on the contract, and each
            // statement of a trigger reads anew, so the check then sees it once committed.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION reject_clause_change_after_adjustment() RETURNS trigger AS $$
                BEGIN
                    PERFORM 1
                    FROM contracts ct
                    WHERE ct.id = NEW.contract_id OR (TG_OP = 'UPDATE' AND ct.id = OLD.contract_id)
                    FOR UPDATE;

                    IF EXISTS (
                        SELECT 1
                        FROM rent_adjustments a
                        WHERE a.contract_id = NEW.contract_id
                           OR (TG_OP = 'UPDATE' AND a.contract_id = OLD.contract_id))
                    THEN
                        RAISE EXCEPTION
                            'The clause of contract % cannot change once it has a confirmed adjustment', NEW.contract_id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'adjustment_clauses_locked';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER adjustment_clauses_lock
                    BEFORE INSERT OR UPDATE ON adjustment_clauses
                    FOR EACH ROW EXECUTE FUNCTION reject_clause_change_after_adjustment();

                CREATE FUNCTION reject_clause_indices_change_after_adjustment() RETURNS trigger AS $$
                BEGIN
                    PERFORM 1
                    FROM contracts ct
                    JOIN adjustment_clauses c ON c.contract_id = ct.id
                    WHERE (TG_OP <> 'DELETE' AND c.id = NEW.adjustment_clause_id)
                       OR (TG_OP <> 'INSERT' AND c.id = OLD.adjustment_clause_id)
                    FOR UPDATE OF ct;

                    IF EXISTS (
                        SELECT 1
                        FROM adjustment_clauses c
                        JOIN rent_adjustments a ON a.contract_id = c.contract_id
                        WHERE (TG_OP <> 'DELETE' AND c.id = NEW.adjustment_clause_id)
                           OR (TG_OP <> 'INSERT' AND c.id = OLD.adjustment_clause_id))
                    THEN
                        RAISE EXCEPTION 'The indices of a clause cannot change once its contract has a confirmed adjustment'
                            USING ERRCODE = 'P0001', CONSTRAINT = 'adjustment_clauses_locked';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER adjustment_clause_indices_lock
                    BEFORE INSERT OR UPDATE OR DELETE ON adjustment_clause_indices
                    FOR EACH ROW EXECUTE FUNCTION reject_clause_indices_change_after_adjustment();
                """);

            // Going from two indices to one needs a removal, and neither role holds DELETE (design D15, Finding F3). This does
            // that one thing, on one clause, and refuses once an adjustment exists.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION app_replace_clause_indices(p_clause_id uuid, p_index_ids uuid[]) RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public, pg_temp AS $$
                BEGIN
                    IF COALESCE(array_length(p_index_ids, 1), 0) = 0
                       OR array_position(p_index_ids, NULL) IS NOT NULL
                       OR (SELECT count(DISTINCT id) FROM unnest(p_index_ids) AS id) <> array_length(p_index_ids, 1)
                    THEN
                        RAISE EXCEPTION 'A clause needs at least one index, none repeated'
                            USING ERRCODE = 'P0001', CONSTRAINT = 'adjustment_clause_indices_invalid';
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM adjustment_clauses WHERE id = p_clause_id) THEN
                        RAISE EXCEPTION 'Clause % does not exist', p_clause_id
                            USING ERRCODE = 'P0001', CONSTRAINT = 'adjustment_clauses_not_found';
                    END IF;

                    -- adjustment_clause_indices_lock refuses this once the contract has a confirmed adjustment.
                    DELETE FROM adjustment_clause_indices WHERE adjustment_clause_id = p_clause_id;

                    INSERT INTO adjustment_clause_indices (adjustment_clause_id, economic_index_id, ordinal)
                    SELECT p_clause_id, chosen.id, chosen.position - 1
                    FROM unnest(p_index_ids) WITH ORDINALITY AS chosen(id, position);
                END;
                $$;

                REVOKE ALL ON FUNCTION app_replace_clause_indices(uuid, uuid[]) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION app_replace_clause_indices(uuid, uuid[]) TO inmobiliaria_admin, inmobiliaria_empleado;
                """);

            // Supabase's default privileges grant every new function to its API roles, which REVOKE FROM PUBLIC does not touch, and
            // this one runs as its owner. Those roles exist only there, so a plain REVOKE would fail on any other database.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    api_role text;
                BEGIN
                    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
                            EXECUTE format('REVOKE ALL ON FUNCTION app_replace_clause_indices(uuid, uuid[]) FROM %I', api_role);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION app_replace_clause_indices(uuid, uuid[]);
                DROP TRIGGER adjustment_clause_indices_lock ON adjustment_clause_indices;
                DROP FUNCTION reject_clause_indices_change_after_adjustment();
                DROP TRIGGER adjustment_clauses_lock ON adjustment_clauses;
                DROP FUNCTION reject_clause_change_after_adjustment();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_clauses_window_offset_range",
                table: "adjustment_clauses");

            migrationBuilder.DropColumn(
                name: "window_offset_months",
                table: "adjustment_clauses");
        }
    }
}
