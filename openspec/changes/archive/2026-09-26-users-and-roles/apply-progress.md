# Apply Progress: users-and-roles

## Mode

Standard (strict_tdd: false per `openspec/config.yaml`). Tests written alongside implementation,
not TDD-gated.

## PR 1 — Phase 1: Domain/Access — COMPLETE (8/8 tasks)

- [x] 1.1 `UserRole.cs` enum (`Admin`, `Empleado`)
- [x] 1.2 `AppUser.cs` (Id, Username, DisplayName, IsActive, MustChangePassword; no password/role column)
- [x] 1.3 `IUserSession.cs` (UserId, Username, DisplayName, Role, MustChangePassword, `ClearMustChangePassword()`)
- [x] 1.4 `UserSession.cs` implementing `IUserSession`
- [x] 1.5 `AppUserTests.cs` — constructor guards (null/whitespace username, display name) + default-values + full-assignment tests
- [x] 1.6 `UserSessionTests.cs` — construction from `AppUser`, `ClearMustChangePassword` flips once and is idempotent
- [x] 1.7 **[Guardrail]** `ArchitectureGuardTests` re-run and green — it is assembly-level reflection
      (`typeof(Party).Assembly.GetReferencedAssemblies()`), so it already covers every namespace in
      `Inmobiliaria.Domain` including the new `Access` namespace with no code change required
- [x] 1.8 **[Isolation check]** `dotnet build` (Domain project alone, then full solution in Release)
      and `dotnet test tests/Inmobiliaria.Domain.Tests` both green on this branch alone

### Files Changed

| File | Action | What Was Done |
|------|--------|----------------|
| `src/Inmobiliaria.Domain/Access/UserRole.cs` | Created | Enum `Admin`, `Empleado` |
| `src/Inmobiliaria.Domain/Access/AppUser.cs` | Created | Guarded constructor (username/displayName null-or-whitespace), `IsActive` defaults `true`, `MustChangePassword` defaults `false`. No password, no role column (spec Decision 5) |
| `src/Inmobiliaria.Domain/Access/IUserSession.cs` | Created | `UserId`, `Username`, `DisplayName`, `Role`, `MustChangePassword`, `ClearMustChangePassword()` |
| `src/Inmobiliaria.Domain/Access/UserSession.cs` | Created | Implements `IUserSession`; constructed from an `AppUser` + a `UserRole` derived elsewhere (see Deviations) |
| `tests/Inmobiliaria.Domain.Tests/AppUserTests.cs` | Created | Null/whitespace guards on username and display name (`Assert.ThrowsAny<ArgumentException>` — see Deviations), default-value test, full-assignment test |
| `tests/Inmobiliaria.Domain.Tests/UserSessionTests.cs` | Created | Construction copies identity from `AppUser`; null `AppUser` rejected; `ClearMustChangePassword` flips and is idempotent |

### Deviations from Design

1. **`UserSession` constructor shape was not fully specified by design.md** (only the `IUserSession`
   interface shape is shown). I chose `UserSession(AppUser user, UserRole role)`, reading
   `UserId`/`Username`/`DisplayName`/`MustChangePassword` straight off the loaded `AppUser` row and
   accepting the role as a separate parameter, since the role is derived from `pg_has_role` — a
   database concern outside `AppUser` (spec Decision 5) — while everything else about the session
   already lives on the `AppUser` row loaded at login (design Decision 10's sequence: "load
   app_users row by username" then combine with the `pg_has_role` result). This keeps `UserSession`
   free of duplicated primitive parameters and matches the one path design.md actually diagrams.
2. **Test assertion had to use `Assert.ThrowsAny<ArgumentException>` instead of
   `Assert.Throws<ArgumentException>`** for the null-string cases. `ArgumentException.ThrowIfNullOrWhiteSpace`
   throws `ArgumentNullException` (a subclass) for a literal `null` argument and plain
   `ArgumentException` for empty/whitespace; xUnit's `Assert.Throws<T>` requires an exact type match,
   so `ThrowsAny<T>` (which accepts subclasses) is the correct assertion for a guard that already
   behaves correctly. No existing test in this project exercised the null case for this exact BCL
   guard, so there was no prior precedent to follow — this is a new, correct pattern for future
   `ThrowIfNullOrWhiteSpace` guard tests in this codebase.

### Issues Found

None. No pre-existing test broke; the full solution (`Inmobiliaria.sln`, Release) builds clean with
`Inmobiliaria.Infrastructure` and `Inmobiliaria.Desktop` untouched.

### Scope Compliance

Confirmed no PR2a/PR2b files were touched: `ContractDocument.cs`, `RentAdjustment.cs`, `Contract.cs`,
any EF configuration, `InmobiliariaDbContext`, and all migrations remain byte-identical to `HEAD`.
No database connection was opened; no migration command was run.

### Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Domain.Tests/Inmobiliaria.Domain.Tests.csproj` → **64 passed, 0 failed, 0 skipped** (full project, since PR1 adds no test filter category distinct from the rest of Domain.Tests) |
| Runtime harness command/scenario and exact result | N/A — pure domain, no EF/Npgsql/DB boundary in this PR (confirmed by `ArchitectureGuardTests` passing) |
| Rollback boundary | Delete `src/Inmobiliaria.Domain/Access/*` and `tests/Inmobiliaria.Domain.Tests/{AppUserTests,UserSessionTests}.cs`; nothing else in the repository references this namespace yet |

### Remaining Tasks (later PRs, not part of this batch)

- [x] Phase 2 (PR 2a): EF Configuration + Entity Changes — see below
- [ ] Phase 3 (PR 2b): Migration, Roles, GRANTs
- [ ] Phase 4 (PR 3): Infrastructure/Access Ports and Adapters
- [ ] Phase 5 (PR 4): In-App Provisioning, Reset, Deactivation
- [ ] Phase 6 (PR 5): Desktop Bootstrap

### Workload / PR Boundary

- Mode: chained PR slice (feature branch chain — `Chain strategy` still recorded as `pending` in
  `tasks.md`'s Review Workload Forecast; the orchestrator resolves this before PR 2a starts)
- Current work unit: Unit 1 — `Domain/Access` (4 files) + domain tests, per tasks.md's Suggested
  Work Units table
- Boundary: starts from nothing (no prior PR merged) and ends with a self-contained, buildable,
  fully-tested `Domain/Access` namespace with zero downstream dependents yet
- Estimated review budget impact: well under the 800-line session budget; actual diff is ~180 lines
  of production code + tests, below even the design's own 240–320 line estimate for this slice

### Status (PR 1)

8/8 Phase 1 tasks complete. Ready for verify (or for the orchestrator to proceed to PR 2a / Phase 2
once the user has reviewed and merged this PR).

---

## PR 2a — Phase 2: EF Configuration + Entity Changes — COMPLETE (11/11 tasks)

- [x] 2.1 `AppUserConfiguration.cs` — table `app_users`, `CHECK (username = lower(username))`, unique username index
- [x] 2.2 `ContractDocument.cs` — `string UploadedBy` replaced by `Guid UploadedByUserId`; `ThrowIfNullOrWhiteSpace` dropped, `Guid.Empty` rejected
- [x] 2.3 `RentAdjustment.Confirm(...)` — gained a required, non-nullable `Guid confirmedBy` parameter
- [x] 2.4 `Contract.ConfirmAdjustment` — gained a required, non-nullable `Guid confirmedBy` parameter
- [x] 2.5 `ContractDocumentConfiguration.cs` — FK to `AppUser` (`ON DELETE RESTRICT`) replaces the text column
- [x] 2.6 `RentAdjustmentConfiguration.cs` — mapped nullable `confirmed_by` + its FK; existing `SetAfterSaveBehavior(Throw)` loop covers it with no new code
- [x] 2.7 `InmobiliariaDbContext.cs` — added `DbSet<AppUser>`; class comment now documents the thirteenth table
- [x] 2.8 Updated every existing call site of `RentAdjustment.Confirm` / `Contract.ConfirmAdjustment` / `new ContractDocument(...)` across 5 test files — no default-value shortcut used
- [x] 2.9 **[EF model check]** `dotnet ef dbcontext info` succeeded offline (placeholder connection string in `DesignTimeDbContextFactory`, never connected) AND a new permanent regression test (`EfModelValidationTests.cs`) was added, because `.github/workflows/ci.yml` never invokes `dotnet ef` — without this test the model-validation gap would stay uncaught by CI going forward
- [x] 2.10 **[Guardrail]** `ArchitectureGuardTests` re-run green after the entity edits
- [x] 2.11 **[Isolation check]** Full solution build + both test projects run on this branch alone; zero Postgres-dependent tests attempted (no migration exists yet)

### Files Changed (PR 2a)

| File | Action | What Was Done |
|------|--------|----------------|
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs` | Created | `app_users` table config: PK, `username`/`display_name` required, `is_active`/`must_change_password` defaults, lowercase CHECK, unique index on `username` |
| `src/Inmobiliaria.Domain/Leasing/ContractDocument.cs` | Modified | `UploadedByUserId` (Guid, rejects `Guid.Empty`) replaces the plain-string `UploadedBy` |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs` | Modified | `ConfirmedBy` is `Guid?` (nullable — matches the nullable column, holds pre-column rows as null forever); `Confirm(...)`'s `confirmedBy` parameter is the non-nullable `Guid` that enforces the asymmetry at every new call site |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modified | `ConfirmAdjustment` gained the same required, non-nullable `Guid confirmedBy` parameter, forwarded to `RentAdjustment.Confirm` |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractDocumentConfiguration.cs` | Modified | `uploaded_by_user_id` FK to `AppUser`, `ON DELETE RESTRICT`; doc comment corrected |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/RentAdjustmentConfiguration.cs` | Modified | `confirmed_by` column mapping (nullable, no `IsRequired()`) + FK to `AppUser`, `ON DELETE RESTRICT` |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modified | `DbSet<AppUser> AppUsers`; class doc comment updated for the 13-table count |
| `tests/Inmobiliaria.Domain.Tests/RentAdjustmentTests.cs` | Modified | 3 call sites of `RentAdjustment.Confirm` given a `Guid.NewGuid()` `confirmedBy` argument |
| `tests/Inmobiliaria.Domain.Tests/AdjustmentMathTruncationTests.cs` | Modified | 3 `RentAdjustment.Confirm` + 1 `contract.ConfirmAdjustment` call sites updated |
| `tests/Inmobiliaria.Domain.Tests/ContractShareSurvivesAdjustmentTests.cs` | Modified | 1 `contract.ConfirmAdjustment` call site updated |
| `tests/Inmobiliaria.Domain.Tests/ContractAdjustmentLateConfirmationTests.cs` | Modified | 2 `contract.ConfirmAdjustment` call sites updated |
| `tests/Inmobiliaria.Infrastructure.Tests/SchemaConstraintTests.cs` | Modified | Local `ConfirmAdjustment` test helper given a `confirmedBy` parameter forwarded as `Guid.NewGuid()`; 2 `new ContractDocument(...)` call sites given a `Guid.NewGuid()` uploader instead of a string |
| `tests/Inmobiliaria.Infrastructure.Tests/EfModelValidationTests.cs` | Created | Forces `OnModelCreating` via `context.Model` against a syntactically-valid, never-opened Npgsql connection string — the permanent CI-visible half of task 2.9 |

### Deviations from Design

1. **`RentAdjustment.ConfirmedBy`'s CLR type is `Guid?`, not `Guid`.** design.md's Decision 8 prose
   focuses entirely on the `Confirm(...)` **parameter** being required and non-nullable; it does not
   explicitly state the entity property's own CLR type. A non-nullable `Guid` property mapped to a
   nullable `confirmed_by uuid NULL` column would throw at read time for the existing (pre-column)
   rows the design itself says stay null forever, so the property had to be `Guid?` for the domain
   model to be able to represent — and EF to be able to read — those rows at all. The asymmetry the
   task description calls out (non-nullable parameter, nullable column) is implemented as: non-nullable
   `Guid confirmedBy` parameter on `Confirm`/`ConfirmAdjustment`, nullable `Guid? ConfirmedBy` property
   and column. This is the only way the stated asymmetry compiles and reads real data correctly.
2. **Added `EfModelValidationTests.cs`, a file design.md/tasks.md does not name.** Task 2.9 offered two
   routes — `dotnet ef dbcontext info`, or an Infrastructure test reading `context.Model`. The first
   route succeeded locally (the existing `DesignTimeDbContextFactory` placeholder connection string
   already lets it run with no live database), so strictly the fallback wasn't required. It was added
   anyway because `.github/workflows/ci.yml`'s `core` job runs only `dotnet build` + `dotnet test` —
   never `dotnet ef` — so without this test the exact CI-breaking gap task 2.9 exists to close (a
   green build, an invalid EF model, no automated check ever catching it) would still be open on every
   future PR. This is additive, not a scope violation: it touches no production file and needed no new
   package (Npgsql was already a test-project dependency via `PostgresFixture.cs`).
3. **`SchemaConstraintTests.cs` needed edits even though tasks.md names only `tests/Inmobiliaria.Domain.Tests/*`
   as the "5 affected test files".** In fact 4 of the 5 files with `Confirm`/`ConfirmAdjustment` call
   sites are under `Domain.Tests`, and the 5th (`SchemaConstraintTests.cs`) lives under
   `Infrastructure.Tests` — it has its own local `ConfirmAdjustment` wrapper plus 2 `new
   ContractDocument(...)` calls. Both had to change for the solution to compile; tasks.md's path is
   read as informative, not as an exhaustive path restriction, since task 2.11 requires the whole
   solution to build.

### Known Residual Gap (not this PR's scope, flagged for PR 2b)

`SchemaConstraintTests.InvalidDocumentKindCheckConstraint_Rejected` still issues a raw SQL `INSERT`
naming the literal column `uploaded_by` (the pre-migration name). This is deliberately left alone:
the test is Postgres-dependent (skips without Docker), no migration exists yet in this PR, and fixing
the literal SQL to `uploaded_by_user_id` requires knowing the exact legacy-row UUID and column shape
PR 2b's migration will actually create. Until PR 2b lands, this raw SQL simply never executes in this
environment (Docker unreachable ⇒ `[SkippableFact]` skips it) — but a developer running this test
suite with Docker *before* PR 2b merges would see it fail against the still-current (pre-migration)
schema for an unrelated reason (the EF model now expects `uploaded_by_user_id`/`app_users`, which
don't exist until PR 2b's migration runs). This is the expected, named consequence of splitting slice
2 into 2a/2b (tasks.md's own Suggested Work Units table), not a defect introduced here.

### Issues Found

None new. No pre-existing test broke. `dotnet build Inmobiliaria.sln` is clean (0 warnings, 0 errors).

### Scope Compliance

Confirmed: no migration file created, `ContractDocumentConfiguration.cs` was touched only for the
planned FK change (not `ContractConfiguration.cs`, which remains untouched), and no
`Infrastructure/Access` file was created. `git status --porcelain` shows only the 11 Phase 2
production/test files above, plus `tasks.md` and this `apply-progress.md`.

### Work Unit Evidence

| Evidence | Value |
|---|---|
| `dotnet build Inmobiliaria.sln` | 0 Warning(s), 0 Error(s) |
| `dotnet test tests/Inmobiliaria.Domain.Tests` | **67 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Inmobiliaria.Infrastructure.Tests` | **1 passed** (`EfModelValidationTests`), **22 skipped** (all Testcontainers-backed `SchemaConstraintTests`/`DueAdjustmentQueryTests` — Docker unreachable in this environment, by design, per `PostgresFixture`'s skip-not-fail behavior), **0 failed** |
| `dotnet ef dbcontext info -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | Succeeded offline: printed context type, provider (`Npgsql.EntityFrameworkCore.PostgreSQL`), database name, and naming convention — no connection ever opened (`DesignTimeDbContextFactory`'s placeholder connection string) |
| Rollback boundary | Revert `ContractDocument.cs`, `RentAdjustment.cs`, `Contract.cs`; delete `AppUserConfiguration.cs`, `EfModelValidationTests.cs`; revert `ContractDocumentConfiguration.cs`, `RentAdjustmentConfiguration.cs`, `InmobiliariaDbContext.cs`, and the 5 test files' call sites. PR 1 (`Domain/Access`) is unaffected — nothing in PR 2a modifies it |

### Status (PR 2a)

11/11 Phase 2 tasks complete. No migration created (out of scope — PR 2b). Ready for the orchestrator
to proceed to PR 2b (Phase 3: Migration, Roles, GRANTs) once this PR is reviewed and merged.

---

## PR 2b — Phase 3: Migration, Roles, GRANTs — 13/14 TASKS COMPLETE (3.13 PENDING THE USER)

**Context, not a re-plan**: PR 2a's own pull request failed CI — every Testcontainers test failed at
`MigrateAsync` with `PendingModelChangesWarning` before reaching a single assertion, because EF Core
refuses to migrate when the model has changes no migration covers. A model change and its migration
are atomic; the 2a/2b split was structurally impossible to ship as two green PRs. **The user decided
to merge 2a and 2b into one pull request.** PR 2a's code (already committed as `136b8c3`) is untouched
here; this section documents only the Phase 3 work added on top, so the branch is green as a whole.

- [x] 3.1 Generated `AddUsersAndRoles` via `dotnet ef migrations add` — scaffolded the naive shape
      (drop `uploaded_by`, add `uploaded_by_user_id` with an `EF`-invented default, add `confirmed_by`,
      create `app_users`)
- [x] 3.2 Hand-edited `Up`'s `contract_documents` delta into the exact required order: `app_users`
      table created first (needed by the seed insert and both FKs) → `uploaded_by_user_id uuid NULL`
      added → legacy `AppUser` row seeded (`id = 00000000-0000-0000-0000-000000000001`,
      `username = 'legacy'`, `is_active = false`) → `UPDATE ... SET uploaded_by_user_id = COALESCE(...)`
      backfill by username join → `ALTER COLUMN ... SET NOT NULL` → `DROP COLUMN uploaded_by` → FK
      `ON DELETE RESTRICT`
- [x] 3.3 Hand-edited `Up`'s `rent_adjustments` delta: `ADD COLUMN confirmed_by uuid NULL` + FK
      `ON DELETE RESTRICT`, no `UPDATE` anywhere near it — every existing row's value is left `NULL`
- [x] 3.4 Roles created idempotently (`IF NOT EXISTS` inside a `DO $$` block, `NOLOGIN`); baseline
      `GRANT USAGE ON SCHEMA public`, `GRANT SELECT ON ALL TABLES`, `ALTER DEFAULT PRIVILEGES` (no
      `FOR ROLE`); every per-table `INSERT`/`UPDATE` grant from design Decision 9's table in one
      `migrationBuilder.Sql(...)` block, including the single ungrouped `GRANT INSERT, UPDATE ON
      contracts` statement — the only place in this migration that names `contracts`
- [x] 3.5 Created all four functions exactly per design Decision 3/6:
      `app_set_role_password(name, text)`, `app_create_login_role(name, text, name)`,
      `app_set_role_login(name, boolean)` (none `SECURITY DEFINER`), and
      `app_clear_must_change_password()` (the only `SECURITY DEFINER` one, `search_path` pinned to
      `pg_catalog, public`); `EXECUTE` granted per design Decision 9's table
      (`app_set_role_password`/`app_clear_must_change_password` to both roles; the provisioning pair
      to `inmobiliaria_admin` only)
- [x] 3.6 `Down` written in the exact fixed order: restore `uploaded_by text` and backfill usernames
      back via join **first** → drop both FKs → drop `confirmed_by`/`uploaded_by_user_id` → `DROP
      FUNCTION` all four → `REVOKE ALL` + `DROP ROLE IF EXISTS` the two **group** roles only → drop
      `app_users` last
- [x] 3.7 Created `docs/runbooks/bootstrap-first-admin.md`: one transaction (`CREATE ROLE ... LOGIN
      PASSWORD`, `ALTER ROLE ... CREATEROLE`, `GRANT inmobiliaria_admin, inmobiliaria_empleado TO
      <admin> WITH ADMIN OPTION`, matching `INSERT INTO app_users (..., must_change_password=true)`),
      plus an explicit "If task 3.12 failed" section recording the actual tested consequence
- [x] 3.8 **[Guardrail — scope guard]** `git diff HEAD` against all five scope-guarded files
      (`20260913215911_InitialSchema.cs`, its `.Designer.cs`, `20260918233049_AddRentAdjustments.cs`,
      its `.Designer.cs`, `ContractConfiguration.cs`) — every one returned empty output (byte-for-byte
      unmodified); `grep -in "contracts"` on the new migration file matches exactly one line, the
      `GRANT INSERT, UPDATE ON contracts` statement — no `ALTER TABLE contracts` anywhere
- [x] 3.9 **[Spec test 29]** Created `RolePermissionTests.cs` with
      `AllThirteenTables_AreSelectableByBothRoles` (Testcontainers): creates a throwaway login role
      for each group role, connects as each, and runs `SELECT 1 FROM {table} LIMIT 0` against all
      thirteen tables — none raised `42501 insufficient_privilege`
- [x] 3.10 **[Spec test 30]** Added `UploadedByUserId_ResolvesToAppUserRow_AndPlainUploadedByColumnIsGone`
      to `SchemaConstraintTests.cs`: confirms `uploaded_by` is absent from
      `information_schema.columns`, then round-trips a real `ContractDocument` through a real
      `AppUser` and confirms the FK resolves after a fresh read
- [x] 3.10b Rewrote `InvalidDocumentKindCheckConstraint_Rejected`: seeds a real `AppUser` row first,
      then raw-SQL-inserts against `uploaded_by_user_id` (not the now-gone `uploaded_by`) with the
      seeded user's id — the test still proves only the unrelated `kind` CHECK constraint
- [x] 3.11 **[Spec test 32]** Added `ConfirmedByColumn_IsNullable` to `SchemaConstraintTests.cs`:
      confirms `is_nullable = 'YES'` for `rent_adjustments.confirmed_by` via
      `information_schema.columns` — the observable, DB-side half of "no `UPDATE` was issued against
      existing rows" (the source-level half is the migration's own `Up` method containing no such
      `UPDATE`, per task 3.3)
- [x] 3.12 **[GATE — RESOLVED]** See the "Task 3.12 finding" section below — tested, not assumed, and
      the answer is **it fails**.
- [ ] 3.13 **[GATE — PENDING THE USER — not attempted by an agent]** See the "Task 3.13 — action
      required from the user" section below. The live Supabase project was never connected to.
- [x] 3.14 **[Isolation check]** `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter
      FullyQualifiedName~SchemaConstraint` against a fresh Testcontainers `postgres:17.6` instance:
      **20 passed, 0 failed, 0 skipped** — this migration applies cleanly on top of
      `20260918233049_AddRentAdjustments` alone, with no PR3/PR4/PR5 code present anywhere in the
      solution

### Task 3.12 finding — ADMIN OPTION does NOT inherit through nested group membership

Tested against a real PostgreSQL 17.6 Testcontainers instance
(`RolePermissionTests.AdminOptionInheritance_ProvenNotAssumed`), not assumed. The scenario actually
proven is the one that matters for PR 4, not the trivial one: the bootstrap runbook's first Admin
(who is granted `ADMIN OPTION` on **both** group roles directly) was never in question. What was
tested is a **second** Admin, provisioned the way `app_create_login_role` (task 3.5) actually does
it — a bare `GRANT inmobiliaria_admin TO second_admin` with **no** `WITH ADMIN OPTION` clause and no
`CREATEROLE`. Authenticated as that second Admin, `GRANT inmobiliaria_empleado TO <any_role>` was
attempted.

**Result: it fails**, with `42501 insufficient_privilege`, exactly as design.md suspected it might.
`ADMIN OPTION` is scoped per `(role, member)` grant edge in PostgreSQL 17; it is not transitively
inherited through nested group membership, and the default `INHERIT` role attribute propagates only
ordinary privileges (`SELECT`/`INSERT`/etc.), never the right to administer role membership.

**Consequence, per tasks.md's own instruction**: PR 4's in-app provisioning function cannot rely on
inheritance for any Admin created after the first. The bootstrap runbook
(`docs/runbooks/bootstrap-first-admin.md`) now documents this in its "If task 3.12 failed" section:
every individual Admin login role must be granted `inmobiliaria_admin, inmobiliaria_empleado ...
WITH ADMIN OPTION` directly, at the moment it is created — not only the first one. This is a human
decision point (tasks.md H.3), not something this PR resolves by itself; PR 4 (Phase 5) must be
re-scoped accordingly before it starts.

### Task 3.13 — action required from the user (not attempted by an agent)

**No agent connected to the live Supabase project.** Per the hard boundary in this PR's instructions,
this task produces the exact read-only query for the user to run themselves, with their own
credentials, and states what each outcome means. The checkbox stays unticked until the user reports
back.

Run this against the live Supabase project (any authenticated session is enough — it reads
`pg_settings`, nothing else):

```sql
SELECT name, setting FROM pg_settings
 WHERE name IN ('log_statement', 'log_min_duration_statement', 'log_parameter_max_length');
```

| If `setting` comes back | Meaning | Action |
|---|---|---|
| `log_statement = 'none'` or `'mod'` | No DDL is logged | Ship PR 4/PR 5 as designed |
| `log_statement = 'ddl'` | DDL statement text is logged | Already neutralized by design Decision 3 — the client issues `SELECT app_set_role_password(...)`, whose top-level command tag is `SELECT`, not DDL; the `ALTER ROLE` runs inside `EXECUTE` and is never a logged top-level statement. Ship as designed |
| `log_statement = 'all'`, or `log_min_duration_statement = 0` | Every statement **and its bind parameters** are logged | **STOP.** The plaintext password reaches the log. Do not ship in-app password management until this is off, or adopt the documented SCRAM client-side-hashing escape hatch from design Decision 3 |
| `setting = NULL` for any row | The GUC is masked for a non-superuser | Read the value from the Supabase dashboard instead (Project Settings → Database → Logs) |

### Files Changed (PR 2b)

| File | Action | What Was Done |
|------|--------|----------------|
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260924202549_AddUsersAndRoles.cs` | Created | Hand-edited migration: `app_users` table, both schema deltas in the required order, idempotent role creation, the full GRANT set, all four SQL functions, and a `Down` in the fixed reverse order |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260924202549_AddUsersAndRoles.Designer.cs` | Created | EF-generated model snapshot for this migration (generated, not hand-authored) |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/InmobiliariaDbContextModelSnapshot.cs` | Modified | EF-generated; regenerated by `dotnet ef migrations add` (generated, not hand-authored) |
| `docs/runbooks/bootstrap-first-admin.md` | Created | The one bootstrap transaction, plus the task 3.12 contingency section |
| `tests/Inmobiliaria.Infrastructure.Tests/RolePermissionTests.cs` | Created | Spec test 29 (`AllThirteenTables_AreSelectableByBothRoles`) and task 3.12's ADMIN OPTION experiment (`AdminOptionInheritance_ProvenNotAssumed`) |
| `tests/Inmobiliaria.Infrastructure.Tests/SchemaConstraintTests.cs` | Modified | Added spec tests 30 (`UploadedByUserId_ResolvesToAppUserRow_AndPlainUploadedByColumnIsGone`) and 32 (`ConfirmedByColumn_IsNullable`); rewrote `InvalidDocumentKindCheckConstraint_Rejected` (task 3.10b) against the FK; added a `SeedAppUser` helper and fixed three pre-existing PR 2a tests (`AppendOnlyTrigger_RejectsRawUpdateAndDelete`, `CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection`, `OriginalAndAddendumDocuments_CoexistForOneContract`) whose `Guid.NewGuid()` placeholder user references now violate the real FK this migration adds — see Deviations below |

### Deviations from Design / tasks.md

1. **Three PR 2a tests needed fixing that neither design.md nor tasks.md named.** Only
   `InvalidDocumentKindCheckConstraint_Rejected` was flagged (task 3.10b) as certain to break. In
   practice, `AppendOnlyTrigger_RejectsRawUpdateAndDelete`,
   `CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection`, and
   `OriginalAndAddendumDocuments_CoexistForOneContract` all used `Guid.NewGuid()` as a stand-in
   `confirmedBy`/`uploadedByUserId` value — harmless before this migration existed (no FK to
   violate), a `23503` foreign-key violation now that one does. Fixed by adding a `SeedAppUser`
   helper and threading a real, saved `AppUser.Id` through the shared `ConfirmAdjustment` test
   helper (which gained a new required `confirmedByUserId` parameter) and into the two
   `ContractDocument` constructions. This was discovered only by actually running the suite against
   Testcontainers, which is exactly why task 3.14's isolation check exists.
2. **Task 3.12's test scenario was rewritten from tasks.md's literal wording to test the real open
   question.** tasks.md describes "a login role granted `inmobiliaria_admin` `WITH ADMIN OPTION` per
   the runbook", then attempting the empleado grant — but the runbook grants `ADMIN OPTION` on
   **both** roles directly, so that literal scenario would trivially succeed and prove nothing. The
   test instead reproduces the scenario design.md's own prose is actually worried about: a **second**
   Admin provisioned by `app_create_login_role`'s exact (non-admin-option) `GRANT`, which is where
   the "inherited" question actually lives. Recorded here, not silently substituted.
3. **`app_create_login_role`'s and `app_set_role_login`'s function bodies were written now (task
   3.5) even though PR 3/PR 4 are the ones that will call them.** design.md's Decision 3 and Decision
   9 both describe all four functions as part of the same migration, and task 3.5 explicitly lists
   all four — this is not scope creep into PR 3/PR 4's own port/adapter code, only the SQL-side
   functions the migration itself must ship with GRANTs already in place.

### Issues Found

None new beyond the three pre-existing tests fixed above (Deviation 1). `dotnet build
Inmobiliaria.Core.slnf --configuration Release` is clean: 0 Warning(s), 0 Error(s).

### Scope Compliance

- No `Infrastructure/Access` port or adapter created (PR 3 scope — untouched).
- No provisioning/reset/deactivation code created (PR 4 scope — untouched).
- No Desktop code touched (PR 5 scope — untouched).
- `ContractConfiguration.cs`: confirmed byte-for-byte unmodified (task 3.8).
- No `ALTER TABLE contracts` anywhere in the new migration; `contracts` is reached only by the one
  `GRANT INSERT, UPDATE ON contracts` statement (task 3.8, task 3.4).
- Both pre-existing migrations and their `.Designer.cs` files: confirmed byte-for-byte unmodified
  against `HEAD` (task 3.8).
- The live Supabase project was never connected to; no credential for it was read, invented, or
  assumed anywhere in this PR (task 3.13).

### Work Unit Evidence

| Evidence | Value |
|---|---|
| `dotnet build Inmobiliaria.Core.slnf --configuration Release` | 0 Warning(s), 0 Error(s) |
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~SchemaConstraint` (Docker up) → **20 passed, 0 failed, 0 skipped** (task 3.14's own required command) |
| Runtime harness command/scenario and exact result | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~RolePermission` (Testcontainers `postgres:17.6`, real roles, real GRANTs) → **2 passed, 0 failed, 0 skipped** — spec test 29 and the task 3.12 experiment both ran against a real engine, not a fake |
| Full suite, exact CI command | `dotnet test Inmobiliaria.Core.slnf --configuration Release` with Docker up → **Domain.Tests: 67 passed, 0 failed, 0 skipped; Infrastructure.Tests: 27 passed, 0 failed, 0 skipped** — zero tests skipped for Docker anywhere in either project |
| Rollback boundary | Delete the two new migration files, `RolePermissionTests.cs`, and `docs/runbooks/bootstrap-first-admin.md`; revert `SchemaConstraintTests.cs` and `InmobiliariaDbContextModelSnapshot.cs`; run `dotnet ef database update 20260918233049_AddRentAdjustments` against any environment where this migration was applied. PR 1 and PR 2a (already committed as `136b8c3` and earlier) are fully unaffected — nothing in this PR modifies their files |

### Review Budget (combined PR 2a + PR 2b, per the user's decision to ship one PR)

Measured against `develop`'s actual merge-base (`ce447a8`), i.e. PR 2a (`136b8c3`, already committed)
plus every uncommitted Phase 3 change in this working tree:

- **Authored (risk-counted) lines**: **≈1,031** (additions + deletions), summed from every file below
  except the two EF-generated ones.
- **Generated goldens (excluded from authored risk, included in full snapshot)**: **831** lines —
  `InmobiliariaDbContextModelSnapshot.cs` (72) and `20260924202549_AddUsersAndRoles.Designer.cs`
  (759), both machine-generated by `dotnet ef migrations add` and never hand-edited.
- **Session budget**: 800 lines (`review_budget_lines`). The authored total is **over budget by
  ≈231 lines**, exactly as tasks.md's own Review Workload Forecast anticipated for PR 2a alone
  before the merge decision (est. 360–440) plus PR 2b alone (est. 365–455) — the merge of the two
  phases the user ordered was always going to land above a single 800-line session budget on its
  own arithmetic. Reported honestly, not trimmed to fit.

### Status (PR 2b)

13/14 Phase 3 tasks complete. Task 3.13 is explicitly **pending the user** — its checkbox stays
unticked until they run the query above against the live Supabase project and report back; no agent
work item remains for it. Ready for `sdd-verify`, or for the orchestrator to hold PR 4 (Phase 5)
until both task 3.12's consequence (H.3) and task 3.13's outcome (H.2) are addressed, per tasks.md's
own gating.

---

## PR 3 — Phase 4: Infrastructure/Access Ports and Adapters — COMPLETE (26/26 tasks)

Task 3.13 remains explicitly pending the user (unchanged from PR 2b — not this PR's scope); it does
not block PR 3, which needs only PR 2b's roles/functions/schema, all already in place.

- [x] 4.1 `SupavisorUsername.cs` — `For(role, projectRef) => $"{role}.{projectRef}"`
- [x] 4.2 `ConnectionEndpoint.cs` — record with `Host`, `Port`, `Database`, `ProjectRef`, `SslMode`; no secret
- [x] 4.3 `IAuthenticator.cs` + `AuthenticationResult.cs` — closed `Success`/`Rejected`/`NotProvisioned` hierarchy
- [x] 4.4 `NpgsqlAuthenticator.cs` — builds the data source, one handshake, `SELECT current_user, pg_has_role(...)`, loads `app_users`
- [x] 4.5 `ISessionDbContextFactory.cs` + `SessionDbContextFactory.cs` — wraps the session's one `NpgsqlDataSource`
- [x] 4.6 `IPasswordService.cs` + `PostgresPasswordService.cs` — `SELECT app_set_role_password(@role,@pw)` via `ExecuteSqlInterpolatedAsync` (real EF/Npgsql parameters)
- [x] 4.7 `ApplicationServices.cs` — `AddPreLoginServices` extension; registers only `ConnectionEndpoint` + `IAuthenticator`
- [x] 4.8 `DesignTimeDbContextFactory.cs` — one doc sentence added; **zero behavioral change** (confirmed by diff)
- [x] 4.9 **[Spec tests 1, 6]** `CompositionGuardTests.cs`: config-file scan + runtime-environment scan (test 1) and pre-login `ServiceCollection` reflection scan for `DbContext`/`NpgsqlDataSource`/`NpgsqlConnection`/`ISessionDbContextFactory` (test 6)
- [x] 4.10 **[Spec test 1, cont. — lexical guard]** `CompositionGuardTests.MigrateAndDesignTimeConnectionString_AppearOnlyInTheAllowedFile`: walks `src/`, asserts `MigrateAsync(`, `Migrate(`, `INMOBILIARIA_DB` appear only in `DesignTimeDbContextFactory.cs`
- [x] 4.11 **[Spec test 2]** `AuthenticationTests.cs`: no Supabase/Gotrue assembly reference (reflection) + a lexical count confirming `NpgsqlAuthenticator.cs` calls `OpenConnectionAsync(` exactly once
- [x] 4.12 **[Spec tests 3, 4, 5]** `AuthenticationTests.cs` (Testcontainers): correct credentials succeed; wrong password and unknown username both produce the same `Rejected` result type with zero distinguishing data
- [x] 4.13 **[Spec tests 7, 8]** `AuthenticationTests.RoleIsDerivedFromPgHasRole_AndADatabaseSideChangeTakesEffectNextLogin` (Testcontainers): role changes from a direct `REVOKE`/`GRANT` take effect on the very next login, no `app_users` update
- [x] 4.14 **[Spec test 13]** `AuthenticationTests.SelfPasswordChange_KeepsTheAlreadyOpenSessionWorking` (Testcontainers): the already-open pooled connection keeps working after `IPasswordService.ChangeOwnPasswordAsync`; a brand-new connection requires the new password
- [x] 4.15 **[Spec test 19]** `PasswordDdlTests.PendingMustChangePassword_DoesNotConfineADirectDatabaseClient` (Testcontainers)
- [x] 4.16 **[Spec tests 20, 21]** `PasswordDdlTests.PasswordDdl_SetsTheExactLiteralPasswordWithNoInjection` (Testcontainers, `[Theory]` over both payloads: `o'brien55` and `x'; DROP TABLE app_users; --`)
- [x] 4.17 **[Spec tests 22, 23]** `RolePermissionTests.Empleado_DirectAppUsersWriteAndProvisioningFunction_BothRefused` / `Admin_DirectAppUsersWriteAndProvisioningFunction_BothSucceed` (Testcontainers)
- [x] 4.18 **[Spec test 24]** `RolePermissionTests.NeitherRole_CanDisableTheAppendOnlyTrigger` (Testcontainers)
- [x] 4.19 **[Spec test 28]** `RolePermissionTests.BothRoles_SeeEveryRowOfATableTheyMayRead` (Testcontainers)
- [x] 4.20 **[Spec test 25 — kept as a full positive end-to-end flow]** `RolePermissionTests.Empleado_CanTerminateAContract_NoticeAndEndBothSucceed` (Testcontainers)
- [x] 4.21 **[Spec test 26 — kept as a full positive end-to-end flow]** `RolePermissionTests.Empleado_CanConfirmARentAdjustment_WithConfirmedByRecorded` (Testcontainers) — reuses `SchemaConstraintTests.SeedContractWithClause`/`ConfirmAdjustment` (widened to `internal`, per the task's own instruction to reuse rather than duplicate)
- [x] 4.22 **[Spec test 27]** `RolePermissionTests.Empleado_AggregateOverContracts_SucceedsBecauseNoGrantCanForbidIt` (Testcontainers)
- [x] 4.23 **[Spec test 33]** `RolePermissionTests.ConfirmingAnAdjustment_AsAnyAuthenticatedUser_StoresHerAppUserReference` (Testcontainers, Admin this time — test 26 already covers Empleado)
- [x] 4.24 **[Spec test 34]** `SchemaConstraintTests.AppendOnlyTrigger_StillRejectsUpdateAndDelete_ForBothApplicationRoles` (Testcontainers)
- [x] 4.25 **[Guardrail]** Every new test file lives under `tests/Inmobiliaria.Infrastructure.Tests/`; nothing touches `Inmobiliaria.Desktop`
- [x] 4.26 **[Isolation check]** `dotnet test Inmobiliaria.Core.slnf --configuration Release` on this branch alone, on top of PR1+2a+2b merged, with none of PR4/PR5's code present — **Domain.Tests 67/67, Infrastructure.Tests 50/50, 0 failed, 0 skipped**, reproduced on two consecutive runs

### The two rules this slice exists to prove — both proven, not merely asserted

1. **No database access before authentication.** `AddPreLoginServices` registers exactly two
   things: the `ConnectionEndpoint` instance and `IAuthenticator -> NpgsqlAuthenticator`. Neither
   type is itself assignable to `DbContext`/`NpgsqlDataSource`/`NpgsqlConnection`/
   `ISessionDbContextFactory` — `CompositionGuardTests.PreLoginServiceCollection_RegistersNothingDatabaseShaped`
   builds the real `ServiceCollection` `AddPreLoginServices` produces and reflects over every
   descriptor to confirm it. The session's `NpgsqlDataSource` is created **inside**
   `NpgsqlAuthenticator.AuthenticateAsync` and only ever leaves that method wrapped inside
   `AuthenticationResult.Success.Factory` — there is no other path in the entire `Access`
   namespace that produces one.
2. **The application never assembles password SQL.** `PostgresPasswordService.ChangeOwnPasswordAsync`
   calls `context.Database.ExecuteSqlInterpolatedAsync($"SELECT app_set_role_password({_session.Username}, {newPassword})", ct)`
   — EF's interpolated-SQL API parameterizes every hole; the value never touches a
   client-built string. `PasswordDdlTests.PasswordDdl_SetsTheExactLiteralPasswordWithNoInjection`
   proves both payloads (`o'brien55`, `x'; DROP TABLE app_users; --`) become the exact literal
   password — `app_users` still exists afterward, and the payload string itself successfully
   authenticates a fresh connection.

### Files Changed (PR 3)

| File | Action | What Was Done |
|------|--------|----------------|
| `src/Inmobiliaria.Infrastructure/Access/SupavisorUsername.cs` | Created | `For(role, projectRef)` composition, used only to open the connection |
| `src/Inmobiliaria.Infrastructure/Access/ConnectionEndpoint.cs` | Created | Record: `Host`, `Port`, `Database`, `ProjectRef`, `Npgsql.SslMode` |
| `src/Inmobiliaria.Infrastructure/Access/AuthenticationResult.cs` | Created | Closed hierarchy: `Success(IUserSession, ISessionDbContextFactory)` / `Rejected` (no data) / `NotProvisioned(string Reason)` |
| `src/Inmobiliaria.Infrastructure/Access/IAuthenticator.cs` | Created | `AuthenticateAsync(username, password, ct)` |
| `src/Inmobiliaria.Infrastructure/Access/NpgsqlAuthenticator.cs` | Created | One `OpenConnectionAsync` call; `28P01`/`28000` map to `Rejected`; `pg_has_role` + `app_users` lookup on the same connection; Admin wins on dual membership |
| `src/Inmobiliaria.Infrastructure/Access/ISessionDbContextFactory.cs` | Created | `Create(): InmobiliariaDbContext`, `IAsyncDisposable` |
| `src/Inmobiliaria.Infrastructure/Access/SessionDbContextFactory.cs` | Created | Wraps one `NpgsqlDataSource`; every `Create()` shares its pool |
| `src/Inmobiliaria.Infrastructure/Access/IPasswordService.cs` | Created | `ChangeOwnPasswordAsync(newPassword, ct)` — self-change only |
| `src/Inmobiliaria.Infrastructure/Access/PostgresPasswordService.cs` | Created | `ExecuteSqlInterpolatedAsync` call into `app_set_role_password` |
| `src/Inmobiliaria.Infrastructure/Access/ApplicationServices.cs` | Created | `AddPreLoginServices` — 2 registrations, neither DB-shaped |
| `src/Inmobiliaria.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | Modified | One doc sentence; diffed byte-identical otherwise |
| `src/Inmobiliaria.Infrastructure/Inmobiliaria.Infrastructure.csproj` | Modified | Added `Microsoft.Extensions.DependencyInjection.Abstractions` (required by task 4.7's `IServiceCollection` extension — design Decision 7 names this exact package) |
| `Directory.Packages.props` | Modified | `Microsoft.Extensions.DependencyInjection.Abstractions` pinned at `10.0.7` — matched to the already-pinned EF Core version after two `NU1109` downgrade errors from `EFCore.NamingConventions`'s and `Testcontainers`'s own transitive floors |
| `tests/Inmobiliaria.Infrastructure.Tests/RepoPaths.cs` | Created | Shared `FindRepoRoot()` for every lexical/structural guard |
| `tests/Inmobiliaria.Infrastructure.Tests/AccessTestSupport.cs` | Created | Shared Testcontainers-side provisioning: `BuildEndpoint`, `ProvisionUserAsync`, `BuildRawConnectionAs`, `BuildDbContextAs` — reused by `AuthenticationTests`, `PasswordDdlTests`, `RolePermissionTests`, and `SchemaConstraintTests`' new test 34 |
| `tests/Inmobiliaria.Infrastructure.Tests/CompositionGuardTests.cs` | Created | Tests 1, 6 + task 4.10's lexical guard |
| `tests/Inmobiliaria.Infrastructure.Tests/AuthenticationTests.cs` | Created | Tests 2, 3, 4, 5, 7, 8, 13 |
| `tests/Inmobiliaria.Infrastructure.Tests/PasswordDdlTests.cs` | Created | Tests 19, 20, 21 |
| `tests/Inmobiliaria.Infrastructure.Tests/RolePermissionTests.cs` | Modified | Added tests 22, 23, 24, 25, 26, 27, 28, 33 (PR 2b's tests 29 and the task 3.12 experiment untouched) |
| `tests/Inmobiliaria.Infrastructure.Tests/SchemaConstraintTests.cs` | Modified | Added test 34; widened `SeedContractWithClause` and `ConfirmAdjustment` from `private` to `internal` so `RolePermissionTests` can reuse them per the task's own instruction |

### Deviations from Design / tasks.md

1. **No real Supavisor pooler exists under Testcontainers, so tests provision login roles named
   exactly as `SupavisorUsername.For` would compose them** (e.g. `maria.test-ref`), and give the
   matching `app_users` row that same composed username. In production, the pooler strips the
   `.projectref` suffix before Postgres ever sees it, so `current_user` and `app_users.username`
   are both the bare name; there is no pooler in this test suite to do that stripping, so the
   composed name is used end-to-end instead. This is a necessary, explicitly-documented test-only
   difference — the pooler's own suffix-stripping behavior is a separately VERIFIED FACT (spec
   Decision 9, proven from the office network) this test suite does not re-prove and does not
   need to; `NpgsqlAuthenticator` itself is agnostic to whatever string Postgres reports as
   `current_user`.
2. **`RentAdjustment.IndexValues`'s materialization constructor assigns an empty array literal
   (`IndexValues = [];` target-typed to `IReadOnlyList<T>`) — a pre-existing PR 2a mapping quirk,
   not introduced here.** `EF.Include(a => a.IndexValues)` throws `NotSupportedException:
   Collection was of a fixed size` when EF's include-fixup tries to append into it, because a
   target-typed `[]` against an interface type compiles to `Array.Empty<T>()`, which is fixed-size.
   Discovered only by actually running spec test 26 against Testcontainers (exactly why isolation
   checks exist). Worked around, not patched: test 26 queries
   `RentAdjustmentIndexValues.CountAsync(v => EF.Property<Guid>(v, "RentAdjustmentId") == id)`
   instead of `.Include(...)`, which proves the same fact (the child row inserted in the same
   transaction) without touching `RentAdjustment.cs`, out of this PR's scope. Flagging this as a
   latent defect for a future PR that ever needs `.Include(a => a.IndexValues)` in production code.
3. **Test 23's Admin case needed CREATEROLE plus ADMIN OPTION on both group roles, granted
   directly** — not merely "provisioned as Admin" — for `app_create_login_role`'s internal
   `CREATE ROLE` and `GRANT <group_role>` to succeed. This is exactly task 3.12's already-proven
   finding (a bare, non-admin-option Admin membership cannot grant role membership) applied
   forward: the test reproduces the bootstrap runbook's exact grant shape from
   `docs/runbooks/bootstrap-first-admin.md` rather than a weaker, PR-4-shaped provisioning path
   that does not exist yet in this slice.
4. **`SqlQueryRaw<int>` requires its raw SQL to alias the single projected column `"Value"`**
   (case-sensitive, quoted) — undocumented in the task list, discovered from the `42703: column
   s.Value does not exist` failure the very first time this ran against Testcontainers.

### Issues Found

None beyond the three testing-environment adaptations captured above as Deviations. No pre-existing
test broke; `RolePermissionTests`' own PR 2b tests (`AllThirteenTables_AreSelectableByBothRoles`,
`AdminOptionInheritance_ProvenNotAssumed`) and every `SchemaConstraintTests` test from PR 1/2a/2b
remain green, unmodified in substance.

### Scope Compliance

- No provisioning/reset/deactivation code created (`IUserProvisioning` does not exist yet — PR 4 scope).
- No Desktop/WPF file touched anywhere (PR 5 scope) — confirmed via `git status` and task 4.25's guardrail test.
- No migration created; the existing `20260924202549_AddUsersAndRoles` migration is untouched (confirmed via `git status` showing no `Migrations/` changes in this PR).
- The live Supabase project was never connected to; task 3.13 remains explicitly pending the user, unrelated to and unblocked by this PR.
- Only one `.csproj` and `Directory.Packages.props` edited, and only for the one package design Decision 7 explicitly names as required by this exact task (4.7).

### Work Unit Evidence

| Evidence | Value |
|---|---|
| `dotnet build Inmobiliaria.Core.slnf --configuration Release` | 0 Warning(s), 0 Error(s) |
| Focused test command and exact result | `dotnet test Inmobiliaria.Core.slnf --configuration Release --filter "FullyQualifiedName~CompositionGuardTests"` → **4 passed, 0 failed, 0 skipped** (task 4.10's lexical guard included, re-run individually and pasted in the phase report) |
| Runtime harness command/scenario and exact result | `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up, Testcontainers `postgres:17.6`) → **Domain.Tests: 67 passed, 0 failed, 0 skipped; Infrastructure.Tests: 50 passed, 0 failed, 0 skipped** — reproduced on two consecutive full runs, zero flakiness observed |
| Rollback boundary | Delete `src/Inmobiliaria.Infrastructure/Access/*` and the five new test files (`AccessTestSupport.cs`, `RepoPaths.cs`, `CompositionGuardTests.cs`, `AuthenticationTests.cs`, `PasswordDdlTests.cs`); revert `DesignTimeDbContextFactory.cs`'s doc sentence, the `.csproj`/`Directory.Packages.props` package addition, and the new tests appended to `RolePermissionTests.cs`/`SchemaConstraintTests.cs` (widen-to-`internal` included). PR 1/2a/2b (already committed) are fully unaffected — nothing yet authenticates depends on anything created here |

### Review Budget (PR 3 alone)

Measured via `git diff --stat` (modified files) + `wc -l` (new files) against the working tree at
the start of this PR (PR 1/2a/2b already committed as `48fb58d` and earlier):

- **Modified files**: `Directory.Packages.props` (+1), `Inmobiliaria.Infrastructure.csproj` (+1),
  `DesignTimeDbContextFactory.cs` (+4/−0 net, one doc sentence), `RolePermissionTests.cs`
  (+271/−0), `SchemaConstraintTests.cs` (+57/−3, from widening two methods to `internal`) — 331
  insertions, 3 deletions.
- **New files** (10 production + 5 test, all authored, no generated goldens in this PR): 847 lines.
- **Authored (risk-counted) total**: **331 + 3 + 847 = 1,181 lines.**
- **Session budget**: 800 lines (`review_budget_lines`). **Over budget by ≈381 lines**, and above
  tasks.md's own 570–670 estimate for this slice. Reported honestly, not trimmed to fit: the
  overage is concentrated in the eight positive/negative role-permission tests (22–28, 33) design.md
  itself calls out as absorbing "all three positive/negative guard tests (25, 26, 27)" into this
  slice, plus the shared `AccessTestSupport` helper every one of those tests depends on. No test was
  reduced to a smoke test to save lines — tasks.md explicitly forbids that for 25, 26, 27.

### Status (PR 3)

26/26 Phase 4 tasks complete. Ready for `sdd-verify`, or for the orchestrator to proceed to PR 4
(Phase 5: In-App Provisioning, Reset, Deactivation) once task 3.13 (H.2) is resolved by the user and
this PR is reviewed and merged — PR 4 also depends on task 3.12's already-recorded consequence (H.3).

---

## PR 4 — Phase 5: In-App Provisioning, Reset, Deactivation — COMPLETE (13/13 tasks)

Task 3.13 (H.2) was resolved by the project owner on 2026-09-25 against the live Supabase
project: `log_statement = ddl`, gate **passes** (see tasks.md task 3.13's own record). Task 3.12's
`ADMIN OPTION` finding (H.3) is the load-bearing fact this PR resolves against, below.

- [x] 5.1 Created `IUserProvisioning.cs` + `PostgresUserProvisioning.cs` (`CreateUserAsync`,
      `ResetPasswordAsync`, `DeactivateAsync`) — see "The provisioning decision" section below
- [x] 5.2–5.11 **[Spec tests 9, 10, 11, 12, 14, 15 (db half), 16, 17, 18, 31]** All ten in
      `UserProvisioningTests.cs` (Testcontainers)
- [x] 5.12 **[Guardrail]** Every new test lives under `tests/Inmobiliaria.Infrastructure.Tests/`
      (`git status` confirms; no `Inmobiliaria.Desktop` file touched)
- [x] 5.13 **[Isolation check]** `dotnet test Inmobiliaria.Core.slnf --configuration Release` —
      **Domain.Tests: 69 passed, 0 failed, 0 skipped; Infrastructure.Tests: 60 passed, 0 failed, 0
      skipped**, on top of PR1–PR3 merged, with none of PR5's Desktop code present

### The provisioning decision this PR had to make (task the design gate H.3 left open)

Task 3.12 proved `ADMIN OPTION` does not inherit through nested group membership. The prompt for
this PR named two options: (1) the runbook grants `CREATEROLE` + `ADMIN OPTION` on both group
roles directly to every Admin login role, or (2) creating an Admin is explicitly out of scope and
only Empleado users can be created in-app. **Option 2 was chosen — creating an Admin in-app is
out of scope; `PostgresUserProvisioning.CreateUserAsync` creates ONLY Empleado accounts.**

This was not a preference between two equally-workable options — reading `app_create_login_role`'s
own frozen SQL body (migration `20260924202549_AddUsersAndRoles.cs`, not editable from this PR)
settled it before any C# was written. That function's `GRANT %I TO %I` for the newly created role
carries no `WITH ADMIN OPTION` clause, for EITHER group role, regardless of which one is granted.
Task 3.12 already proved a bare (non-admin-option) membership cannot grant role membership at all.
The two facts together mean: **a login role `app_create_login_role` creates can never itself
successfully provision anyone**, Empleado or Admin alike — it would always fail with `42501`,
exactly like the second Admin in `RolePermissionTests.AdminOptionInheritance_ProvenNotAssumed`
(PR 2b). Option 1, read literally ("grant it to every Admin login role"), is therefore only
achievable by a HUMAN re-running the bootstrap runbook per additional Admin — the runbook can grant
`ADMIN OPTION` directly (a superuser/owner-credential operation), but the in-app `CreateUserAsync`
path structurally cannot, no matter which group role it targets. Rather than expose an in-app
"create Admin" operation that would provision a role permanently unable to do the one thing
supposed to distinguish her from an Empleado, this PR does not expose it at all — matching the
spec's own literal example ("Admin creates a new employee") and keeping the API's shape honest
about what it can actually do.

Proof: `UserProvisioningTests.CreateUser_CreatesLoginRoleAndAppUsersRowAsOneUnit_AndAForcedFailureOrphansNeither`
asserts the newly created role is a member of `inmobiliaria_empleado` **and explicitly NOT**
`inmobiliaria_admin`, immediately after a successful `CreateUserAsync` call. `docs/runbooks/
bootstrap-first-admin.md` gained a new "PR 4's resolution" section documenting the decision and
the procedure for provisioning an additional Admin (re-run the runbook; still a human/runbook
operation, never in-app).

### A second finding, discovered running this phase's own tests (not previously documented anywhere in this change)

PostgreSQL restricts `ALTER ROLE` (password change, `LOGIN`/`NOLOGIN`) on an **existing** role to
that role's own creator, or a superuser — `CREATEROLE` plus `ADMIN OPTION` on the group role is
NOT, by itself, sufficient for a role the caller did not personally create. This was discovered
empirically: the first draft of `UserProvisioningTests` provisioned target users via the existing
`AccessTestSupport.ProvisionUserAsync` helper (which creates the role through the Testcontainers
**superuser** connection) and then called `ResetPasswordAsync`/`DeactivateAsync` as a *different*
Admin test role — every one of those five tests failed with `42501: permission denied to alter
role`, not any of the errors previously anticipated in tasks.md or design.md.

**Consequence, applied and documented, not silently worked around**: in this application, every
user is created via `CreateUserAsync`, called by whichever Admin is logged in — so
`ResetPasswordAsync`/`DeactivateAsync` only succeed when the CALLING Admin is the SAME one who
originally provisioned that user. This is a non-issue in practice (this project has exactly one
Admin — design.md's own reasoning elsewhere already leans on that fact), but it is now stated
plainly in `PostgresUserProvisioning.ResetPasswordAsync`/`DeactivateAsync`'s XML doc remarks and
`IUserProvisioning.cs`'s interface docs, and every affected test was rewritten so the SAME
`provisioning` instance both creates and later resets/deactivates its target user.

### Deviation: `IPasswordService.ChangeOwnPasswordAsync` gained a `currentPassword` parameter

Spec test 17 ("re-submitting the just-authenticated password as the new one is rejected") is
assigned by tasks.md task 5.9 to `UserProvisioningTests` — Infrastructure, Testcontainers, no UI.
design.md's own Decision 6 prose describes this rejection as an in-memory, client-side ordinal
comparison happening in the forced-change `ChangePasswordViewModel` (PR 5, not yet built). Since
the test must prove this at the Infrastructure layer with no ViewModel to compare against,
`ChangeOwnPasswordAsync`'s signature grew a required `currentPassword` parameter: an ordinal
comparison against `newPassword`, checked and rejected (throwing `InvalidOperationException`)
BEFORE anything is ever bound to `app_set_role_password` — consistent with task 3.13's
"validate before binding the password" principle, and cheaper than the DB-probe alternative
considered and rejected below.

`ChangeOwnPasswordAsync` also now calls `app_clear_must_change_password()` immediately after a
successful `app_set_role_password` call, wrapped in the same explicit transaction, and updates
the in-memory `IUserSession.MustChangePassword` flag via `ClearMustChangePassword()` — necessary
for spec tests 16/18 to be provable at all (nothing else in the codebase yet calls this function;
without it, `MustChangePassword` could never be cleared by any self-service change, forced or
voluntary).

**Alternative considered and rejected**: a "probe-connect with the candidate password" mechanism
(open a throwaway connection using `newPassword` as if it were already live; success implies
reuse). Rejected because it depends on recomposing `_session.Username` through
`SupavisorUsername.For` — which is correct in production (where `_session.Username` is the bare,
pooler-stripped rolname) but WRONG in the Testcontainers test environment (where, per PR 3's own
Deviation 1, `_session.Username` already IS the fully composed value, since there is no real
pooler to strip it back down). Recomposing it there would double the suffix and make the probe
never resolve to a real role, so the rejection could never actually trigger in this test suite.
The ordinal-comparison design has no such asymmetry and needed no new dependency.

**Two existing PR 3 call sites updated for the new parameter** (no assertion in either test
changed): `AuthenticationTests.SelfPasswordChange_KeepsTheAlreadyOpenSessionWorking` and
`PasswordDdlTests.PasswordDdl_SetsTheExactLiteralPasswordWithNoInjection`, both now passing
`AccessTestSupport.DefaultPassword` as `currentPassword`.

### Files Changed (PR 4)

| File | Action | What Was Done |
|------|--------|----------------|
| `src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs` | Created | `CreateUserAsync` (returns the new `AppUser.Id`), `ResetPasswordAsync`, `DeactivateAsync` — full remarks on the Empleado-only decision and the ALTER-ROLE-creator constraint |
| `src/Inmobiliaria.Infrastructure/Access/PostgresUserProvisioning.cs` | Created | Calls `app_create_login_role`/`app_set_role_password`/`app_set_role_login` with real EF/Npgsql parameters; validates non-secret inputs (username shape, target existence/active state) before any password-binding call; wraps each operation's two halves in one explicit transaction |
| `src/Inmobiliaria.Domain/Access/AppUser.cs` | Modified | Added `Deactivate()` and `RequirePasswordChange()` mutator methods — needed by `PostgresUserProvisioning` and not previously exposed (only the constructor could set these flags) |
| `src/Inmobiliaria.Infrastructure/Access/IPasswordService.cs` | Modified | `ChangeOwnPasswordAsync` gained a required `currentPassword` parameter (see Deviation above) |
| `src/Inmobiliaria.Infrastructure/Access/PostgresPasswordService.cs` | Modified | Ordinal reject-reuse check; now also clears `must_change_password` (DB + in-memory) on every successful change, wrapped in one transaction |
| `docs/runbooks/bootstrap-first-admin.md` | Modified | New "PR 4's resolution" section: in-app provisioning is Empleado-only; procedure for provisioning an additional Admin (re-run this runbook) |
| `tests/Inmobiliaria.Infrastructure.Tests/UserProvisioningTests.cs` | Created | All ten spec tests (9, 10, 11, 12, 14, 15 db-half, 16, 17, 18, 31) |
| `tests/Inmobiliaria.Infrastructure.Tests/AccessTestSupport.cs` | Modified | Added `BuildSessionFactoryAs` and `GrantProvisioningCapabilityAsync` helpers, reused by every `UserProvisioningTests` test |
| `tests/Inmobiliaria.Infrastructure.Tests/AuthenticationTests.cs` | Modified | One call site updated for `ChangeOwnPasswordAsync`'s new parameter |
| `tests/Inmobiliaria.Infrastructure.Tests/PasswordDdlTests.cs` | Modified | One call site updated for `ChangeOwnPasswordAsync`'s new parameter |
| `tests/Inmobiliaria.Domain.Tests/AppUserTests.cs` | Modified | Two new tests for `Deactivate()`/`RequirePasswordChange()`, matching the existing idempotency-testing pattern in this file |

### Issues Found

None beyond the two findings documented above (both were investigated, resolved, and proven —
not merely worked around). No pre-existing test broke.

### Scope Compliance

- No Desktop/WPF file touched anywhere (PR 5 scope) — confirmed via `git status`.
- No migration created or modified; `20260924202549_AddUsersAndRoles.cs` untouched — confirmed via
  `git status` showing no `Migrations/` changes in this PR.
- `ContractConfiguration.cs` not touched.
- The live Supabase project was never connected to.

### Work Unit Evidence

| Evidence | Value |
|---|---|
| `dotnet build Inmobiliaria.Core.slnf --configuration Release --no-incremental` | 0 Advertencia(s) (Warnings), 0 Errores (Errors) |
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Infrastructure.Tests/Inmobiliaria.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~UserProvisioning"` → **10 passed, 0 failed, 0 skipped** |
| Runtime harness command/scenario and exact result | `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up, Testcontainers `postgres:17.6`) → **Domain.Tests: 69 passed, 0 failed, 0 skipped; Infrastructure.Tests: 60 passed, 0 failed, 0 skipped** |
| Rollback boundary | Delete `IUserProvisioning.cs`, `PostgresUserProvisioning.cs`, `UserProvisioningTests.cs`; revert `AppUser.cs`, `IPasswordService.cs`, `PostgresPasswordService.cs`, `AccessTestSupport.cs`, `AuthenticationTests.cs`, `PasswordDdlTests.cs`, `AppUserTests.cs`, and the runbook. PR1–PR3 (already committed) are fully unaffected — auth, roles, and permissions all still work with no provisioning code present |

### Review Budget (PR 4 alone)

Measured via `git diff --stat` (modified files, against the working tree at the start of this PR,
PR1–PR3 already committed) + `wc -l` (new files):

- **Modified files**: `docs/runbooks/bootstrap-first-admin.md` (+33), `AppUser.cs` (+27),
  `IPasswordService.cs` (+14/−2), `PostgresPasswordService.cs` (+31/−1), `AppUserTests.cs` (+26),
  `AccessTestSupport.cs` (+43), `AuthenticationTests.cs` (+1/−1), `PasswordDdlTests.cs` (+1/−1) —
  **172 insertions, 6 deletions = 178 lines.**
- **New files** (2 production + 1 test, all authored, no generated goldens): `IUserProvisioning.cs`
  (61) + `PostgresUserProvisioning.cs` (173) + `UserProvisioningTests.cs` (395) = **629 lines.**
- **Authored (risk-counted) total**: **178 + 629 = 807 lines.**
- **Session budget**: 800 lines (`review_budget_lines`). **Over budget by 7 lines** — marginal,
  but tasks.md's own forecast for this slice was 380–470, so the overage is substantial relative
  to that estimate. Reported honestly rather than trimmed to fit: the two undocumented PostgreSQL
  findings (Empleado-only provisioning forced by the frozen migration; the ALTER-ROLE-creator
  restriction) both required test rewrites and XML-doc remarks that were not in the original
  estimate, and the `IPasswordService` extension for spec test 17 touched two already-merged PR 3
  files in addition to the two new provisioning files.

### Status (PR 4)

13/13 Phase 5 tasks complete. Ready for `sdd-verify`, or for the orchestrator to proceed to PR 5
(Phase 6: Desktop Bootstrap) once this PR is reviewed and merged.

---

## PR 5 — Phase 6: Desktop Bootstrap — COMPLETE (10/10 tasks)

- [x] 6.1 `CommunityToolkit.Mvvm` (`8.4.2`) and `Microsoft.Extensions.DependencyInjection`
      (`10.0.7`, matched to the already-pinned `Abstractions` version) added to
      `Directory.Packages.props`; `Inmobiliaria.Desktop.csproj` gained its first
      `Inmobiliaria.Infrastructure` project reference plus the two new `PackageReference`s
- [x] 6.2 Created `LoginViewModel.cs` (`ObservableObject`, `CommunityToolkit.Mvvm` source
      generators: `[ObservableProperty]` for `Username`/`Password`/`IsBusy`/`ErrorMessage`,
      `[RelayCommand]` for `LoginCommand`); depends only on `IAuthenticator`
- [x] 6.3 Created `LoginWindow.xaml` + `LoginWindow.xaml.cs` — plain controls (`TextBox`,
      `PasswordBox`, one `Button`), no custom colours/fonts/theme, Spanish UI copy
- [x] 6.4 Created `ChangePasswordViewModel.cs` — `IsForced` gates whether `CancelCommand` is
      exercisable at all (`CanCancel() => IsForced`); depends on `IPasswordService`,
      `IUserSession` (the latter surfaced as a read-only `DisplayName` property, so the
      dependency is genuinely used, not merely accepted and ignored)
- [x] 6.5 Created `ChangePasswordWindow.xaml` + `ChangePasswordWindow.xaml.cs` — three
      `PasswordBox` controls, a Cancel button whose `Visibility` is bound to `IsForced` via the
      built-in `BooleanToVisibilityConverter` (no custom converter written)
- [x] 6.6 Modified `App.xaml.cs`: `OnStartup` (now `async void`, a deliberate WPF-lifecycle
      pattern — see Deviations) builds the pre-login container via `AddPreLoginServices`, then
      drives `LoginWindow` → (forced) `ChangePasswordWindow` → `MainWindow` through
      `LoginBootstrap`'s stages; a cancelled/closed-without-success forced change disposes
      `success.Factory` and loops back to a fresh `LoginWindow`, never falling through.
      `App.xaml`'s `StartupUri` was removed (WPF would otherwise construct `MainWindow` before
      any login ran) and `ShutdownMode="OnExplicitShutdown"` was added, so closing `LoginWindow`
      or `ChangePasswordWindow` mid-sequence never ends the process before the next window in
      the chain is shown — only `MainWindow.Closed` calls `Shutdown()`
- [x] 6.7 **[Spec test 15 — construction-order half]** See "The two rules this slice exists to
      prove" below — proven via a new pure state machine, not a WPF test
- [x] 6.8 Modified `openspec/config.yaml`: `credential-exposure` moved from `open_decisions` to
      `resolved_decisions` (resolved 2026-09-25, referencing this change's own spec decisions);
      `data-api-disabled`'s rationale corrected (it previously claimed EF Core "authenticates as
      the table owner" — no longer true now that every connection is an individual login role,
      a member of one of the two group roles, and neither group role owns any table); added the
      `new-table-ships-with-grants` convention entry
- [x] 6.9 **[Guardrail]** Confirmed by `grep` across `src/Inmobiliaria.Desktop/**/*.cs` and
      `**/*.xaml`: `Npgsql`/`EntityFrameworkCore`/`DbContext` appear in exactly one file,
      `App.xaml.cs` (the composition root, which needs `Npgsql.SslMode` to build
      `ConnectionEndpoint`) — one incidental match in `LoginViewModel.cs` is a doc-comment
      sentence ("it knows nothing about Npgsql..."), not a type reference. No `.xaml` file
      matches at all
- [x] 6.10 **[Isolation check]** See Work Unit Evidence below — full solution build (Windows
      `build` job's own command) and `Inmobiliaria.Core.slnf` test run (Linux `core` job's own
      command), both on this branch alone, on top of PR1–PR4 merged

### The two rules this slice exists to prove

1. **A forced password change cannot be walked around.** `App.xaml.cs`'s `RunBootstrapAsync`
   never constructs `MainWindow` before both gates pass: `LoginBootstrap.AfterAuthentication`
   returns `AwaitingForcedPasswordChange` (never `ReadyForMainWindow`) whenever
   `success.Session.MustChangePassword` is true, and `MainWindow` is only ever constructed after
   the `if (stage == BootstrapStage.AwaitingForcedPasswordChange)` block completes without
   hitting its own `continue`. Cancelling (`ChangePasswordViewModel.Cancel` when `IsForced`, or
   closing the window via its chrome — both land on a `ShowDialog()` result other than `true`)
   disposes `success.Factory` and loops back to a fresh `LoginWindow`; the `continue` statement
   is the only path out of that branch besides falling through to `MainWindow`, and it can only
   be reached from `BootstrapStage.Aborted`.
2. **Task 6.7's test lives in `Inmobiliaria.Infrastructure.Tests`, not Desktop.** Rather than
   leave the construction-order rule as WPF-only logic no Linux job could ever check, it was
   extracted into a new pure state machine — `LoginBootstrap`/`BootstrapStage` — in
   `Inmobiliaria.Infrastructure/Access` (no WPF, EF, or Npgsql reference). `App.xaml.cs` calls
   the exact same type to drive its real windows, so the test
   (`LoginBootstrapTests.MustChangePasswordPending_CannotReachMainWindow_UntilTheChangeSucceeds`
   and its sibling `CancellingAForcedChange_AbortsInsteadOfFallingThroughToMainWindow`) is not a
   parallel reimplementation of the rule — it exercises the production decision logic directly,
   with a real `UserSession`/`AppUser` and a throwaway `ISessionDbContextFactory` stub that is
   never actually opened.

### Files Changed (PR 5)

| File | Action | What Was Done |
|------|--------|----------------|
| `Directory.Packages.props` | Modified | Added `CommunityToolkit.Mvvm` (`8.4.2`) and `Microsoft.Extensions.DependencyInjection` (`10.0.7`) |
| `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj` | Modified | Added the two new `PackageReference`s and the first `ProjectReference` to `Inmobiliaria.Infrastructure` |
| `src/Inmobiliaria.Desktop/appsettings.json` | Modified | Added a `Supabase` section (`Host`, `Port`, `Database`, `ProjectRef`, `SslMode`) — none of the five is a secret (design Decision 2); `Host`/`ProjectRef` ship as clearly-labeled placeholders (`REPLACE_WITH_...`) since no agent connects to the live Supabase project and the real values are not this PR's to invent |
| `src/Inmobiliaria.Desktop/App.xaml` | Modified | Removed `StartupUri`; added `ShutdownMode="OnExplicitShutdown"` |
| `src/Inmobiliaria.Desktop/App.xaml.cs` | Modified | The composition root: `AddPreLoginServices`, the `LoginWindow` → `ChangePasswordWindow` → `MainWindow` sequence, `appsettings.json`-backed `ConnectionEndpoint` loading via `JsonDocument` |
| `src/Inmobiliaria.Desktop/LoginViewModel.cs` | Created | `Username`, `Password`, `IsBusy`, `ErrorMessage`, `LoginCommand`, `SuccessResult`, `LoginSucceeded` event |
| `src/Inmobiliaria.Desktop/LoginWindow.xaml`, `.xaml.cs` | Created | Plain login form; `PasswordBox` relayed to the ViewModel in code-behind (WPF does not support binding `PasswordBox.Password`) |
| `src/Inmobiliaria.Desktop/ChangePasswordViewModel.cs` | Created | `CurrentPassword`, `NewPassword`, `ConfirmNewPassword`, `IsBusy`, `ErrorMessage`, `IsForced`, `DisplayName`, `ChangeCommand`, `CancelCommand`, `PasswordChangeSucceeded`/`Cancelled` events |
| `src/Inmobiliaria.Desktop/ChangePasswordWindow.xaml`, `.xaml.cs` | Created | Three `PasswordBox` controls; Cancel button visible only when `IsForced` |
| `src/Inmobiliaria.Infrastructure/Access/LoginBootstrap.cs` | Created | `BootstrapStage` enum + `LoginBootstrap` static class — the pure construction-order state machine task 6.7 needs and `App.xaml.cs` actually drives its windows through |
| `tests/Inmobiliaria.Infrastructure.Tests/LoginBootstrapTests.cs` | Created | Spec test 15's construction-order half (task 6.7): 4 test methods (one a `[Theory]` over 2 cases) |
| `openspec/config.yaml` | Modified | `credential-exposure` → `resolved_decisions`; `data-api-disabled` rationale corrected; `new-table-ships-with-grants` convention added |

### Deviations from Design / tasks.md

1. **`LoginBootstrap`/`BootstrapStage` is a new production type neither design.md nor tasks.md
   names by filename.** Task 6.7 requires the construction-order rule to be provable from
   `Inmobiliaria.Infrastructure.Tests` (WPF cannot gate `core`), but design.md's own diagram
   (Decision 10) only shows the sequence as prose/ASCII art, not as a type. Rather than write a
   test that reimplements the rule in parallel (and could drift from what `App.xaml.cs` actually
   does), the sequence was extracted into this one small, pure, dependency-free state machine
   that both the real bootstrap and the test exercise identically.
2. **`App.xaml.cs`'s `OnStartup`/`OnExit` are `async void`, and `ShutdownMode` was added.**
   Neither design.md nor tasks.md specifies these — they are load-bearing implementation
   details task 6.6 leaves to this PR's judgment. `async void` is the documented, accepted
   pattern for WPF lifecycle overrides that must `await` something (there is no caller to
   propagate a `Task` back to). `ShutdownMode="OnExplicitShutdown"` was necessary because the
   default (`OnLastWindowClose`) would end the process the moment `LoginWindow` or a cancelled
   `ChangePasswordWindow` closes — before the bootstrap loop gets a chance to show the next
   window — which would have silently broken exactly the two-gate sequence this slice exists to
   prove.
3. **`appsettings.json`'s new `Supabase` section ships placeholder values for `Host` and
   `ProjectRef`.** Design Decision 2 states these are non-secret, but the actual live values for
   this project's Supabase instance are not something this PR's agent knows or is permitted to
   invent (the hard boundary against connecting to or inventing facts about the live Supabase
   project, carried from every earlier PR in this change). The placeholders are clearly labeled
   (`REPLACE_WITH_...`) and documented in `App.xaml.cs`'s own doc comment; filling them in is a
   deployment step for a human with access to the actual project, not a code change.
4. **No `Microsoft.Extensions.Configuration` package was added.** Task 6.1 names only
   `CommunityToolkit.Mvvm` and `Microsoft.Extensions.DependencyInjection` as the packages this
   slice needs ("add nothing beyond what tasks 6.1–6.10 need"). `appsettings.json` is read
   directly with `System.Text.Json.JsonDocument` (already part of the shared framework, no new
   package) rather than the `IConfiguration` pattern a larger app would use.

### Issues Found

None. No pre-existing test broke; the full solution builds clean with `Inmobiliaria.Desktop`
now fully wired (previously it built only because it was never asked to compile bootstrap code).

### Scope Compliance

- No navigation shell, no menus, no second application screen — `MainWindow.xaml` is untouched
  content-wise (still the placeholder `TextBlock`), matching design's explicit scope guard.
- No visual styling added anywhere: no custom colours, no theme resources, no fonts loaded —
  every control uses its WPF default appearance; only layout (`Grid`/`StackPanel`/margins) and
  Spanish label text were authored, per this PR's explicit instruction that visual design is out
  of scope.
- `IUserProvisioning` (PR 4) is never referenced from `Inmobiliaria.Desktop` — provisioning has
  no UI in this slice, matching design's "no menus" scope guard.
- No new test was placed under `Inmobiliaria.Desktop` — `LoginBootstrapTests.cs` lives under
  `tests/Inmobiliaria.Infrastructure.Tests/`, confirmed by its file path and by `git status`.
- The live Supabase project was never connected to; no credential or project-specific fact about
  it was read or invented — the two placeholder config values are clearly labeled as such.

### Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test Inmobiliaria.Core.slnf --configuration Release` (task 6.7's own test filtered in — no separate `--filter` needed since `LoginBootstrapTests` requires no container and always runs) → **Domain.Tests: 69 passed, 0 failed, 0 skipped; Infrastructure.Tests: 65 passed, 0 failed, 0 skipped** (60 from PR 4 + 5 new `LoginBootstrapTests` cases: 4 methods, one a `[Theory]` over 2 `[InlineData]` cases) |
| Runtime harness command/scenario and exact result | `dotnet build Inmobiliaria.sln --configuration Release --no-incremental` (the Windows `build` job's own command, full solution including `Inmobiliaria.Desktop` and its WPF/XAML compilation) → **0 Advertencia(s), 0 Errores** |
| Rollback boundary | Revert `App.xaml`, `App.xaml.cs`, `Inmobiliaria.Desktop.csproj`, `appsettings.json`, `Directory.Packages.props`; delete `LoginViewModel.cs`, `LoginWindow.xaml{,.cs}`, `ChangePasswordViewModel.cs`, `ChangePasswordWindow.xaml{,.cs}`, `src/Inmobiliaria.Infrastructure/Access/LoginBootstrap.cs`, `tests/Inmobiliaria.Infrastructure.Tests/LoginBootstrapTests.cs`. PR1–PR4 (already committed) are fully unaffected — authentication, roles, permissions, and provisioning all still work with no Desktop UI present |

### Review Budget (PR 5 alone)

Measured via `git diff --stat` (modified files) + `wc -l` (new files), against the working tree
at the start of this PR (PR1–PR4 already committed):

- **Modified files**: `Directory.Packages.props` (+2), `App.xaml` (+6/−2), `App.xaml.cs`
  (+128/−4), `Inmobiliaria.Desktop.csproj` (+6), `appsettings.json` (+7) — **149 insertions, 6
  deletions = 155 lines.**
- **New files** (5 production + 1 test in Desktop, 1 production + 1 test in Infrastructure, all
  authored, no generated goldens): `ChangePasswordViewModel.cs` (126) + `ChangePasswordWindow.xaml`
  (67) + `ChangePasswordWindow.xaml.cs` (56) + `LoginViewModel.cs` (95) + `LoginWindow.xaml` (45)
  + `LoginWindow.xaml.cs` (40) + `LoginBootstrap.cs` (63) + `LoginBootstrapTests.cs` (87) =
  **579 lines.**
- **Authored (risk-counted) total**: **155 + 579 = 734 lines.**
- **Session budget**: 800 lines (`review_budget_lines`). **Under budget by 66 lines** — within
  tasks.md's own 400–500 estimate range's upper bound plus the extra `LoginBootstrap` state
  machine and its test, which neither design.md nor tasks.md itemized by name but were needed to
  satisfy task 6.7 without a WPF-only test.

### Status (PR 5)

10/10 Phase 6 tasks complete. **This is the final code slice of `users-and-roles`.** Ready for
`sdd-verify` — all six planned PRs (1, 2a, 2b, 3, 4, 5) are now implemented, with task 3.13 (H.2)
already resolved by the user and task 3.12's consequence (H.3) already resolved by PR 4's own
design decision (Empleado-only in-app provisioning). Remaining open items are exactly the
Human Follow-Ups tasks.md already lists (H.1: apply the migration to the live Supabase project;
H.4: the carried-forward CHECK constraint follow-up for the collection change) — neither is an
agent task.
