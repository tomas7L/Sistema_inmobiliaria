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
