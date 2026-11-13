# Proposal: revocar-permisos-api (closing Supabase's API roles out of the database)

Written 2026-11-13, from `openspec/changes/revocar-permisos-api/exploration.md`. Vocabulary: the **API roles** are
Supabase's `anon`, `authenticated` and `service_role`; the **group roles** are this system's `inmobiliaria_admin` and
`inmobiliaria_empleado`; **default privileges** are what Postgres grants automatically on objects created later.

Rules come from the `access-control` spec and `openspec/config.yaml` (decision `data-api-disabled`). This proposal cites
them; it does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope and §5 Decisions. **Skim** §9 Size. **Skip** §3, §4 and §6 to §8 unless you
want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Decisions (with how strongly each is held)
6. Affected Areas
7. Risks
8. Rollback Plan
9. Size Forecast
10. Success Criteria

---

## 1. Intent

Today every table in production can be read and changed by Supabase's API roles, and the only thing stopping that is a
dashboard switch, the Data API, which is off (exploration, **Finding**). This change takes the grants away in a migration,
so the database itself refuses those roles whatever the switch says, and so tables created later never get them.

It is required before the agency loads real data (`docs/ESTADO.md`, Pendientes). Nicolás is not involved: it changes
nothing anyone sees.

## 2. Scope

### In Scope

- Revoke every privilege the API roles hold on every existing table in `public`.
- Change `postgres`'s default privileges in `public` so future tables, functions and sequences are not granted to the API
  roles.
- Revoke the API roles' and `PUBLIC`'s `EXECUTE` on the three functions operators call that still have it
  (`app_set_role_password`, `app_create_login_role`, `app_set_role_login`), keeping the group roles' explicit grants.
- Tests that prove each of the above against the fixture that imitates Supabase. Production still has Supabase's old
  defaults (exploration, **What production returned**), which is what the fixture imitates.
- The `access-control` spec gains the requirement that the API roles hold nothing.

### Out of Scope

- Turning the Data API on, or using it. It stays off (`data-api-disabled`).
- Row Level Security. Rejected on its merits (`data-api-disabled`), not deferred.
- `supabase_admin`'s default privileges: `postgres` cannot change them, and they apply only to objects `supabase_admin`
  creates, which this system never does.
- Schemas other than `public` (`auth`, `storage` and the rest belong to Supabase).
- Moving the Supabase project to a Volkode-owned organisation (go-live checklist, a separate step).

## 3. Capabilities

### New Capabilities

None.

### Modified Capabilities

- `access-control`: a new requirement, "Supabase's API roles hold nothing in `public`", covering tables, future tables and
  the functions operators call.

## 4. Approach

**One migration, SQL only, no model change.** It reuses the guarded loop that three migrations already use, which revokes
only where the API roles exist, so it runs unchanged on Supabase and in the test container
(`AddTenantNotices.cs:88-101`). Revoking a privilege nobody holds is not an error in Postgres, so the same migration is
correct whether production still has Supabase's old defaults, as it did on 2026-11-13, or Supabase has applied its
announced change by then.

**Tests through the public surface the other permission tests use**: `has_table_privilege` and `has_function_privilege`
for each API role on each table and function, plus a table created after the migration to prove the defaults. The
fixture's sanity check that the API roles really were granted moves to before the migration runs, where it still proves
something (exploration, **What breaks**).

**Production was read first, not assumed.** Tomás ran the three read-only queries on 2026-11-13 (exploration, **What
production returned**); design writes the migration against those results.

## 5. Decisions

| # | Decision | Strength |
|---|---|---|
| P1 | **Fix it in a migration** (exploration option A), not by relying on the Data API switch (C) | Strong |
| P2 | **Revoke `service_role` too.** Its key is secret and bypasses everything by design, but nothing in this system uses it, so there is no reason to leave it holding anything | Strong |
| P3 | **`Down` does not restore the grants.** Rolling the migration back must not reopen the tables, as `HardenClearMustChangePassword` already decided (line 42) | Strong |
| P4 | **Trigger functions are left as they are**, unless design proves with a test that revoking `PUBLIC` from them leaves every trigger firing for both group roles. They cannot be called directly, so the gain is small and the risk is a broken insert | Medium |
| P5 | **Revoking `USAGE` on schema `public`** (exploration option B) is not in scope. Design adds it only if it costs nothing in Supabase's dashboard | Weak |

## 6. Affected Areas

| Area | Change |
|---|---|
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/` | One new migration, SQL only |
| `tests/Inmobiliaria.Infrastructure.Tests/PostgresFixture.cs` | Can start from Supabase's old or new defaults; the sanity check moves before migrating |
| `tests/Inmobiliaria.Infrastructure.Tests/` | New permission tests; `TenantNoticeRecordTests.SupabasesApiRoles_HoldNothing_OnTheSentRecord` reworked |
| `openspec/specs/access-control/spec.md` | The new requirement, at archive |
| `docs/ESTADO.md` | The "Permisos de más en producción" pending item closes once production's F2 query is clean |

## 7. Risks

| Risk | Likelihood | Answer |
|---|---|---|
| A group role loses a privilege it needs | Low | The migration names only the API roles and `PUBLIC` on three functions; the existing permission tests (`RolePermissionTests`, `ReciboGrantsTests` and others) run unchanged and must stay green |
| A trigger stops firing for an operator | Low | P4: trigger functions are not touched unless a test proves it safe |
| Production's starting point differs from the fixture's | Low | Checked on 2026-11-13: production has the defaults the fixture imitates. The migration is idempotent if Supabase changes them first |
| Someone turns the Data API on later | Low | After this change, it exposes nothing in `public` |

## 8. Rollback Plan

Nothing is deleted and no stored data changes. If an operator is refused something after applying it, the fix is a
`GRANT` to the group role that needs it, in a new migration; the API roles are never granted again.

## 9. Size Forecast

About 150 to 250 lines in one PR: the migration (~60), the tests (~100), the fixture (~30), and the spec delta. **Risk:
item 2 (permissions)**, so it gets the independent verifier and the gentle-ai review.

**Preconditions:** Docker for every test. Supabase restored only for the read-only queries before design, and later for
applying the migration, both by Tomás.

## 10. Success Criteria

- On the test database and on production, the F2 query (`notificaciones/design.md:261-264`) returns no rows.
- A table created after the migration is not granted to any API role.
- The API roles cannot execute any function operators call.
- Every existing permission test passes unchanged, apart from the reworked sanity check.
