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
