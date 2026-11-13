# Spec: revocar-permisos-api (closing Supabase's API roles out of the database)

Single-file specification for the `revocar-permisos-api` change. One capability is **MODIFIED**: **`access-control`**. None
is new.

Vocabulary, glossed once: the **API roles** are Supabase's `anon`, `authenticated` and `service_role`; the **group roles**
are this system's `inmobiliaria_admin` and `inmobiliaria_empleado`; the **migrating role** is the role that runs
`dotnet ef database update`, which is `postgres` on Supabase (`docs/ESTADO.md:111-112`); **default privileges** are what
Postgres grants automatically on objects the migrating role creates later; an **operator function** is a function the
application calls, as opposed to a **trigger function**, which only a trigger runs.

The proposal is `openspec/changes/revocar-permisos-api/proposal.md`, merged as PR #119 on 2026-11-13, with its
exploration beside it. Rules already in `access-control` are cited, not restated.

**What this spec does not do.** It states what must be true, observably. The migration's name, its SQL, how it walks the
tables, and how the test fixture is arranged are design's. Where a parameter is design's, the text says so.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table, rows 6 to 12 | The calls this spec had to make beyond the proposal. Each has a strength. The **medium** and **weak** rows are where you can change my mind. |
| **Findings** box right after the table | What production and the code showed. F2 is why functions need care. |
| Requirements 1 and 3 | The tables, and the functions operators call: the two promises that matter. |

**Skip** the Tests section and "Parameters Left to Design". **Skim** the rest; the scenarios are there so a test can be
written from each one.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code, a living spec, or production (source
given). **SPEC CALL** = this spec had to decide because the proposal did not. Strength: **strong**, **medium**, **weak**
(act on it only provisionally).

Rows P1 to P5 are the proposal's Decisions, in the proposal's numbering, merged as PR #119 on 2026-11-13. Rows 6 onwards
are this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | Fix it in a migration, not by relying on the Data API switch. | Proposal §5 | Strong |
| P2 | Revoke `service_role` too. | Proposal §5 | Strong |
| P3 | `Down` does not restore the grants. | Proposal §5 | Strong |
| P4 | Trigger functions are left as they are, unless design proves with a test that closing them leaves every trigger firing for both group roles. | Proposal §5 | Medium |
| P5 | Revoking `USAGE` on schema `public` is not in scope; design adds it only if it costs nothing. | Proposal §5 | Weak |
| 6 | **"Every table" means every table in `public` at the moment the migration runs**, `app_users` and `__EFMigrationsHistory` included, found by the migration itself rather than listed by name, so a table nobody remembered is not missed. | SPEC CALL. Production's F2 query lists both (`notificaciones/design.md`, Finding F2) | Strong |
| 7 | **Future tables, functions and sequences created by the migrating role in `public` are not granted to the API roles.** The group roles' default `SELECT` on new tables (`AddUsersAndRoles.cs:148`) is kept. | VERIFIED FACT: production's defaults still grant the API roles all three kinds (exploration, **What production returned**) | Strong |
| 8 | **The three operator functions that are still open are closed to `PUBLIC` and to the API roles; the group roles keep exactly the `EXECUTE` they hold today.** `app_set_role_password` and `app_clear_must_change_password` stay callable by both group roles; `app_create_login_role` and `app_set_role_login` stay Admin-only. | VERIFIED FACT: production shows the API roles can run all three (exploration, query 2); their grants to the group roles are explicit (`AddUsersAndRoles.cs:209-216`) | Strong |
| 9 | **Closing a function means closing `PUBLIC`, not only the API roles**, because every role inherits what `PUBLIC` holds. Revoking only the API roles' own grant would leave them able to run it. | VERIFIED FACT, Finding F2 | Strong |
| 10 | **Every operator function added after this change ships closed to `PUBLIC` in its own migration**, with `EXECUTE` granted only to the group roles that call it, as the two hardened functions already do. This is a rule for future migrations, like "Every New Table Ships With Its GRANTs" (`access-control/spec.md:107`). The default for future functions is not changed for `PUBLIC` (P4). | SPEC CALL. Changing `PUBLIC`'s default would also close future trigger functions, P4's open question | Medium |
| 11 | **Production is checked after applying**: the F2 query (`notificaciones/design.md:261-264`) MUST return no rows. That closes Finding F2 of `notificaciones` and the "Permisos de más en producción" item in `docs/ESTADO.md`. | SPEC CALL | Strong |
| 12 | **Supabase's own schemas and `supabase_admin`'s defaults are left alone**, including `postgres`'s defaults in `storage`, which production also showed open. | SPEC CALL, following the proposal's Out of Scope. `storage` is empty of this system's data; `contract-documents` will revisit it when it uses Storage | Medium |

### Findings (production and the code)

| # | Finding | Consequence |
|---|---|---|
| F1 | **Production still has Supabase's old defaults** (Tomás, 2026-11-13): `postgres` in `public` grants the API roles every table privilege (`arwdDxtm`), `EXECUTE` on functions and `rwU` on sequences. Supabase's announced change of 2026-10-30 has not reached it. | Decision 7. The migration must still be correct if Supabase applies its change first: revoking what is not held is not an error. |
| F2 | **Every role inherits `PUBLIC`.** Postgres gives `PUBLIC` `EXECUTE` on every new function; the two hardened functions revoke it explicitly (`AddUsersAndRoles.cs:207`, `AddClauseWindowOffset.cs:121`), and the three open ones do not. | Decision 9. |
| F3 | **The three operator functions run with the caller's rights**, not as definer (`AddUsersAndRoles.cs:173-188`), so an API role that ran them would still be refused by Postgres when it tried to create or alter a role. They are closed anyway: a refusal at the door beats a refusal inside. | Decision 8 is defence in depth, not the only barrier. |
| F4 | **The test fixture's proof that it imitates Supabase depends on a table staying open**: `TenantNoticeRecordTests.cs:167` asserts `anon` holds every privilege on `contract_events`. After this change no table is open. | The proof moves to before the migration runs (Test 1). |
| F5 | **`AddTenantNotices` is not yet applied to production** (its two trigger functions are missing from query 2). | Applying this change's migration applies it first, in order. Nothing to decide; noted so nobody is surprised. |

## Table of Contents

- Capabilities covered
- Capability: access-control (MODIFIED)
  - Requirement 1: Supabase's API Roles Hold Nothing on Any Table
  - Requirement 2: A Table Created Later Is Not Granted to Them
  - Requirement 3: The Functions Operators Call Are Closed to Everyone but Their Roles
  - Requirement 4: A New Operator Function Ships Closed
  - Requirement 5: Rolling Back Does Not Reopen Anything
- Out of Scope
- Parameters Left to Design
- Questions for Tomás
- Preconditions
- Tests

## Capabilities covered

| Capability | Kind | Requirements |
|---|---|---|
| `access-control` | MODIFIED | 1 to 5 |

## Capability: access-control (MODIFIED)

### Requirement 1: Supabase's API Roles Hold Nothing on Any Table

After the migration, none of `anon`, `authenticated` and `service_role` MAY hold any privilege on any table in `public`
(Decision 6). The group roles' privileges on every table MUST be exactly what they were before the migration. Where the
API roles do not exist, as in a plain Postgres, the migration MUST run without error and change nothing.

#### Scenario: An existing table is closed

- GIVEN `contracts`, granted to the API roles as Supabase grants every table
- WHEN the migration has run
- THEN none of the three API roles MAY hold `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, `REFERENCES`, `TRIGGER` or
  `MAINTAIN` on it (`MAINTAIN` is Postgres 17's, the `m` in production's `arwdDxtm`, and `information_schema` does not list it)

#### Scenario: The users table and the migrations history are closed too

- GIVEN `app_users` and `__EFMigrationsHistory`
- WHEN the migration has run
- THEN none of the three API roles MAY hold any privilege on either

#### Scenario: The group roles keep what they had

- GIVEN every privilege both group roles hold on every table before the migration
- WHEN the migration has run
- THEN they MUST hold exactly the same privileges, no more and no fewer

#### Scenario: A database without the API roles

- GIVEN a database where none of the three API roles exists
- WHEN the migration runs
- THEN it MUST succeed and grant or revoke nothing

### Requirement 2: A Table Created Later Is Not Granted to Them

After the migration, a table, function or sequence that the migrating role creates in `public` MUST NOT be granted to any
API role (Decision 7). A table created later MUST still be readable by both group roles, as today.

#### Scenario: A later table is closed from birth

- GIVEN the migration has run
- WHEN the migrating role creates a new table in `public`
- THEN none of the three API roles MAY hold any privilege on it
- AND both group roles MUST hold `SELECT` on it

#### Scenario: A later function is not granted to them by default

- GIVEN the migration has run
- WHEN the migrating role creates a new function in `public`
- THEN none of the three API roles MAY hold an explicit `EXECUTE` grant on it from the default privileges

### Requirement 3: The Functions Operators Call Are Closed to Everyone but Their Roles

After the migration, no API role MAY be able to execute any operator function, and neither MAY `PUBLIC` (Decisions 8 and
9). Each group role MUST keep exactly the `EXECUTE` it holds today:

| Function | `inmobiliaria_admin` | `inmobiliaria_empleado` |
|---|---|---|
| `app_set_role_password` | Yes | Yes |
| `app_clear_must_change_password` | Yes | Yes |
| `app_replace_clause_indices` | Yes | Yes |
| `app_create_login_role` | Yes | No |
| `app_set_role_login` | Yes | No |

Trigger functions are outside this requirement unless design closes them under P4, with Test 9.

#### Scenario: An API role cannot create a login

- GIVEN the migration has run
- WHEN `anon` is asked whether it may execute `app_create_login_role`
- THEN the answer MUST be no, and the same for `authenticated` and `service_role`, and for the other four operator functions

#### Scenario: An Empleado still changes their own password

- GIVEN an Empleado login after the migration
- WHEN they change their password through the application
- THEN it MUST succeed, as it does today

#### Scenario: An Admin still creates and disables logins

- GIVEN an Admin login after the migration
- WHEN they create an Empleado and then disable them
- THEN both MUST succeed, as they do today

#### Scenario: An Empleado is still refused the Admin's functions

- GIVEN an Empleado login after the migration
- WHEN they try to create a login or disable one
- THEN the database MUST refuse, as it does today

### Requirement 4: A New Operator Function Ships Closed

Any migration that adds an operator function after this change MUST, in that same migration, revoke its `EXECUTE` from
`PUBLIC` and grant it only to the group roles that call it (Decision 10). Like "Every New Table Ships With Its GRANTs in
the Same Migration" (`access-control/spec.md:107`), this is checked by a test, so a migration that forgets it fails the
suite.

#### Scenario: An open operator function fails the suite

- GIVEN a function in `public` that is not a trigger function
- WHEN `PUBLIC` or an API role can execute it
- THEN Test 8 MUST fail, naming the function

### Requirement 5: Rolling Back Does Not Reopen Anything

Reverting the migration MUST NOT grant anything to the API roles, nor give `PUBLIC` back `EXECUTE` on an operator
function (P3).

#### Scenario: Down leaves the API roles with nothing

- GIVEN the migration has run
- WHEN it is reverted
- THEN the three API roles MUST still hold no privilege on any table, and no operator function MUST be executable by them

## Out of Scope

| Not here | Why |
|---|---|
| Turning the Data API on, or using it | `openspec/config.yaml`, `data-api-disabled` |
| Row Level Security | Rejected on its merits (`access-control/spec.md:97-105`) |
| `supabase_admin`'s default privileges, and the `storage`, `graphql`, `graphql_public`, `realtime`, `auth` and `extensions` schemas | Decision 12; Supabase's own |
| Changing `PUBLIC`'s default on future functions | Decision 10; P4 |
| `USAGE` on schema `public` | P5, unless design adds it |
| Moving the Supabase project to a Volkode-owned organisation | Go-live checklist |

## Parameters Left to Design

- The migration's name, and how it finds every table in `public` (Decision 6).
- Whether trigger functions are closed (P4); if so, Test 9 is written and proves every trigger still fires.
- Whether `USAGE` on schema `public` is revoked (P5).
- How the fixture proves it imitates Supabase before migrating (Finding F4), and how `SupabasesApiRoles_HoldNothing_OnTheSentRecord`
  is reworked or replaced.
- How Test 8 tells an operator function from a trigger function.
- What `Down` contains, given that it restores nothing to the API roles.

## Questions for Tomás

None open. Decisions 10 and 12 are where a different preference of yours would change it.

## Preconditions

| Phase | Docker | Supabase |
|---|---|---|
| This spec, design, tasks | No | No |
| The migration and its tests | **Yes** | No |
| Applying to the test database, then production, and running the F2 query on each | No | **Yes**, restored; Tomás applies it |

## Tests

Each names the requirement it proves and the project it lives in. All run against `postgres:17.6` through Testcontainers,
in `Inmobiliaria.Infrastructure.Tests`; a skipped one is an unrun test. Tests 1 and 6 are **presence** assertions, so they
cannot pass on an empty list.

1. **Before the migration runs, the fixture's API roles hold every table privilege on a table it created, and `EXECUTE` on
   a function it created.** Proves the fixture imitates Supabase, so the other tests prove something. Finding F4
2. **After the migration, no API role holds any privilege on any table in `public`.** Proven failing with the revoke
   removed. R1
3. **`app_users` and `__EFMigrationsHistory` are among the tables checked, and are closed.** R1
4. **Both group roles' privileges on every table are identical before and after the migration.** R1
5. **The migration runs without error on a database where the API roles do not exist.** R1
6. **A table created after the migration grants nothing to the API roles, and `SELECT` to both group roles; a function
   created after it carries no default `EXECUTE` grant to the API roles.** Proven failing with the default revoke removed.
   R2
7. **No API role and not `PUBLIC` can execute any operator function; each group role's `EXECUTE` matches Requirement 3's
   table exactly.** R3
8. **No function in `public` other than a trigger function is executable by `PUBLIC` or an API role**, naming any that is.
   Shown failing on a function added without the revoke. R4
9. **Only if design closes trigger functions (P4):** with `PUBLIC`'s `EXECUTE` revoked from every trigger function, an
   Empleado's and an Admin's ordinary inserts and updates still fire every trigger. R3
10. **After `Down`, the API roles still hold nothing on any table and cannot execute any operator function.** R5

The existing permission tests (`RolePermissionTests`, `ReciboGrantsTests`, `ClauseCorrectionTests`,
`ClearMustChangePasswordHardeningTests` and the user-administration tests behind Requirement 3's scenarios) MUST pass
unchanged.

Verification, beyond the tests: `dotnet ef migrations has-pending-model-changes` is clean (the migration is SQL only);
Tests 2, 6 and 8 are shown failing as stated; and after Tomás applies the migration, the F2 query returns no rows on the
test database and on production (Decision 11).
