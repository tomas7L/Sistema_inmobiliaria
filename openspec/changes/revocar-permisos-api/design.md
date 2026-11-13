# Design: revocar-permisos-api (closing Supabase's API roles out of the database)

Technical design for the `revocar-permisos-api` change, from `spec.md` (Requirements 1 to 5, Decisions P1 to P5 and 6 to
12, Tests 1 to 10, merged as PR #120 on 2026-11-13) and `proposal.md` in this folder. Written 2026-11-13. Nothing here is
code; nothing here edits the spec. Every existing file named was read or mapped and is cited `path:line`; anything new is
marked **(new)**. Paths use `Infra` = `src/Inmobiliaria.Infrastructure/` and `ITests` = `tests/Inmobiliaria.Infrastructure.Tests/`.

**How complete the audit was.** Read: `ITests/PostgresFixture.cs`, `ITests/ReciboSchemaTests.cs` (its isolated database,
lines 945-975), `ITests/AdjustmentDifferenceMigrationTests.cs` (migrating to a named migration, lines 70-120),
`ITests/TenantNoticeRecordTests.cs`, the three migrations with the guarded loop, and every `CREATE FUNCTION` in
`Infra/Persistence/Migrations/` with its return type. External facts verified against their sources, cited where used.
**Not run:** no build and no test.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** rows are where you can change my mind |
| **§6 Threats and rollback** | What could go wrong when you apply it to production, and what to do |

**Skip** §1 to §5 unless you want the detail: they are for whoever implements and for the second inspection.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | **One migration `RevokeSupabaseApiRoles` (new), SQL only**, created with `dotnet ef migrations add` so it carries its Designer file; the model snapshot does not change | Strong |
| D2 | **Tables are found by Postgres, not listed**: `REVOKE ALL ON ALL TABLES IN SCHEMA public FROM <role>` covers every table at that moment, `app_users` and `__EFMigrationsHistory` included (spec Decision 6). The same for `ALL SEQUENCES` and `ALL FUNCTIONS`, which removes the API roles' own grants everywhere | Strong |
| D3 | **Each API role is handled inside the existing guarded loop**, so a role that does not exist is skipped (`Infra/.../20261112160850_AddTenantNotices.cs:88-101`). `ALTER DEFAULT PRIVILEGES` also sits inside it, because naming a missing role there is an error | Strong |
| D4 | **`ALTER DEFAULT PRIVILEGES IN SCHEMA public` without `FOR ROLE`**: it binds to the migrating role, `postgres` on Supabase and `inmobiliaria_test` in the container, which has no `postgres` role (`ITests/PostgresFixture.cs:40`). `AddUsersAndRoles.cs:148` already relies on that binding | Strong |
| D5 | **`PUBLIC` loses `EXECUTE` on exactly the three open operator functions, by signature**: `app_set_role_password(name, text)`, `app_create_login_role(name, text, name)`, `app_set_role_login(name, boolean)`. Their grants to the group roles are explicit (`AddUsersAndRoles.cs:209-216`) and untouched | Strong |
| D6 | **Trigger functions are not closed to `PUBLIC`** (P4). Losing the API roles' own grant on them (D2) is harmless: a trigger function cannot be called directly | Medium |
| D7 | **`USAGE` on schema `public` is not revoked** (P5). Once nothing in it is granted, `USAGE` reaches nothing that matters, and Supabase's pages never suggest revoking it | Medium |
| D8 | **`Down` is empty, with a comment saying why** (P3, spec Requirement 5), as `HardenClearMustChangePassword.cs:42` already decided | Strong |
| D9 | **The migration tests run on an isolated database per test**, migrated to the migration before this one, checked, then migrated up (and down for Test 10), as `AdjustmentDifferenceMigrationTests` does. The fixture's shared database is not used to prove the before state | Strong |
| D10 | **The fixture also imitates Supabase's default on sequences**, as production showed (`rwU`), so Test 6 proves a later sequence too | Medium |
| D11 | **Test 5, a database without the API roles, runs on a second, plain container** started by that test alone, because the roles belong to the server and every other test needs them | Medium |

## Table of Contents

1. Technical approach
2. The migration
3. The test fixture
4. Tests
5. Findings
6. Threats and rollback
7. Slice, size and preconditions
8. Parameters the spec left to design

## 1. Technical approach

One SQL-only migration does four things, in one transaction (EF wraps each migration in one):

1. For each API role that exists: revoke everything it holds on every table, sequence and function in `public` (D2).
2. For each API role that exists: revoke the migrating role's default privileges on tables, sequences and functions in
   `public` from it (D4).
3. Revoke `EXECUTE` from `PUBLIC` on the three open operator functions (D5).
4. Nothing else. The group roles, their default `SELECT` on new tables, and every trigger are left as they are.

Postgres treats revoking a privilege that is not held as a no-op, so the migration reaches the same end state whether
production still has Supabase's old defaults or Supabase has changed them by the time it runs (spec Finding F1).

## 2. The migration

`Infra/Persistence/Migrations/<timestamp>_RevokeSupabaseApiRoles.cs` **(new)**, `Up`:

```sql
DO $$
DECLARE
    api_role text;
BEGIN
    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I', api_role);
            EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I', api_role);
            EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %I', api_role);
            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM %I', api_role);
            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM %I', api_role);
            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM %I', api_role);
        END IF;
    END LOOP;
END;
$$;

REVOKE ALL ON FUNCTION app_set_role_password(name, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION app_create_login_role(name, text, name) FROM PUBLIC;
REVOKE ALL ON FUNCTION app_set_role_login(name, boolean) FROM PUBLIC;
```

The signatures were read from the migrations (`AddUsersAndRoles.cs:173-188`). `Down`: no statement, and a comment that
rolling back must not reopen anything (D8).

The comment above `Up` cites spec Requirements 1 to 3 and `notificaciones/design.md` Finding F2, and says that the F2
query must return no rows after applying it.

## 3. The test fixture

`ITests/PostgresFixture.cs:84-85` gains one line, as production showed (spec Finding F1, D10):

```sql
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO anon, authenticated, service_role;
```

Nothing else in the fixture changes: it still creates the roles, grants like Supabase, then migrates. After this change the
shared database ends with the API roles holding nothing, which is the point.

`TenantNoticeRecordTests.SupabasesApiRoles_HoldNothing_OnTheSentRecord` (`ITests/TenantNoticeRecordTests.cs:162-173`) loses its
line 167, which asserted that `anon` still held every privilege on `contract_events` (spec Finding F4). Its proof that the
fixture really grants moves to Test 1, which checks it before this migration runs. The rest of the test stays.

## 4. Tests

One new class, `ITests/SupabaseApiRoleRevocationTests.cs` **(new)**, plus Test 5 in its own class, because it starts its own
container. Each test that needs a before state builds an isolated database as `IsolatedDatabase.CreateAsync` does
(`ITests/ReciboSchemaTests.cs:948-968`), grants like Supabase, and migrates to the migration just before this one with
`IMigrator.MigrateAsync(<previous>)` (`ITests/AdjustmentDifferenceMigrationTests.cs:115-119`).

Privileges are read with `has_table_privilege`, `has_sequence_privilege` and `has_function_privilege`, never
`information_schema`, which does not show Postgres 17's `MAINTAIN` (spec Requirement 1). The table privileges checked are
the eight: `SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER, MAINTAIN`. An operator function is one whose
return type is not `trigger` (`prorettype <> 'trigger'::regtype`); every function in the migrations was checked against
that: 5 return `void`, 17 return `trigger`.

| Spec test | How |
|---|---|
| 1 | Before state: `anon` holds all eight privileges on `contract_events`, and `EXECUTE` on `app_set_role_password` through its own grant (`aclexplode(proacl)` names it) |
| 2 | After: for every table in `pg_tables` where `schemaname = 'public'`, each API role holds none of the eight. The test also asserts the list is not empty, so it cannot pass on nothing |
| 3 | The list of Test 2 contains `app_users` and `__EFMigrationsHistory` |
| 4 | The group roles' eight-privilege matrix over every table is read before and after, and the two are equal |
| 5 | A plain `postgres:17.6` container with no API roles: migrate to the end without error, and `pg_default_acl` has no entry naming an API role |
| 6 | After: as the migrating role, create a table, a sequence and a function in `public`. No API role holds any privilege on the table or the sequence; both group roles hold `SELECT` on the table; `aclexplode(proacl)` of the function names no API role |
| 7 | After: for the five operator functions, the `EXECUTE` matrix over the two group roles, the three API roles and `public` equals spec Requirement 3's table, with `No` for every API role and for `public` |
| 8 | A helper lists every function in `public` that is not a trigger function and that `public` or an API role can execute. After: the list is empty. Then the test creates such a function, and the helper names it, which is the "shown failing" of the spec |
| 9 | Not written: D6 leaves trigger functions open to `PUBLIC` |
| 10 | Migrate up, then back to the previous migration: Tests 2 and 7's assertions still hold |

Requirement 3's behavioural scenarios (an Empleado changes their password; an Admin creates and disables a login; an
Empleado is refused those) are already proven by `ChosenPasswordAndLoginTests` and `RolePermissionTests`, which run on the
shared database and therefore after this migration. They must pass unchanged.

**Shown failing, before GREEN:** Test 2 with the table revoke removed, Test 6 with the default revoke removed, and Test 7
with the `PUBLIC` revokes removed. Each mutation is reverted byte-identical.

## 5. Findings

| # | Finding | Consequence |
|---|---|---|
| F1 | **Revoking on an object one does not own depends on what one holds on it** (corrected 2026-11-13 by the verifier's probe). If the migrating role holds some privilege on it, Postgres skips what it cannot revoke with a warning and the migration succeeds. If it holds none, Postgres refuses the statement (`permission denied for table ...`) and the whole migration rolls back | Either way nothing is left half-done: a warning leaves the object visible to the F2 query after applying, and a refusal changes nothing and names the object. Production's F2 rows are all this system's tables, owned by `postgres`, so neither is expected. That is why Decision 11 of the spec checks production by query, not by trusting the migration |
| F2 | **`has_function_privilege('public', ...)` is valid**: `public` is accepted as a role name by Postgres's privilege functions, and `ClauseCorrectionTests` already checks `public` that way (`ITests/ClauseCorrectionTests.cs:447-449`) | Test 7 needs no special case |
| F3 | **The migration before this one is `20261112160850_AddTenantNotices`.** Tests name it as the "before" target | If another migration lands first, the tests' target moves with it |

## 6. Threats and rollback

| Threat | Answer |
|---|---|
| An operator is refused something after it is applied | The migration names only the API roles and `PUBLIC` on three functions. Every group-role grant is untouched, and Test 4 proves it. If it happens anyway: a `GRANT` to the group role, in a new migration; never a grant to an API role |
| The migration fails half-way on production | It runs in one transaction: nothing is changed, and the error says which statement. Nothing to clean up |
| A table is left open | The F2 query after applying shows it (spec Decision 11) |
| Supabase changes its defaults before it is applied | Revoking what is not held is a no-op (§1) |

**Applying it (Tomás):** as every migration, `docs/ESTADO.md`, "Aplicar una migración": first the test database, then
production. `AddTenantNotices` goes first on production, in the same `database update` (spec Finding F5). After each, the
F2 query in the SQL editor must return no rows, and the app must open and log in as before.

## 7. Slice, size and preconditions

One PR, about 200 lines: the migration (~40 with its comment), the fixture (1), `TenantNoticeRecordTests` (-1), the new
test classes (~150), and the `access-control` delta at archive. **Risk: item 2 (permissions)**, so it gets the independent
verifier and the gentle-ai review.

| Step | Docker | Supabase |
|---|---|---|
| Writing and testing | **Yes** | No |
| Applying, then the F2 query | No | **Yes**, test and production; Tomás |

## 8. Parameters the spec left to design

| Parameter | Answer |
|---|---|
| The migration's name, and how it finds every table | D1, D2 |
| Whether trigger functions are closed (P4) | No, D6 |
| Whether `USAGE` on `public` is revoked (P5) | No, D7 |
| How the fixture proves it imitates Supabase, and the reworked test | §3, Test 1 |
| How Test 8 tells an operator function from a trigger function | `prorettype <> 'trigger'::regtype` (§4) |
| What `Down` contains | Nothing, D8 |
