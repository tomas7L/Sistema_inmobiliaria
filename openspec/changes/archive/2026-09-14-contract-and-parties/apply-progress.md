# Apply Progress: Contract and Parties — Slices 1–2 (PR 1–2)

**Mode**: Standard (strict_tdd: false).

## Slice 1 (PR 1) — Solution Scaffolding, Domain Layer, CI Split

## Completed Tasks

- [x] 1.1 `Inmobiliaria.sln` at repo root + `Inmobiliaria.Core.slnf` (Domain + Domain.Tests, no Desktop)
- [x] 1.2 `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors) + `Directory.Packages.props` (central package versions)
- [x] 1.3 `Inmobiliaria.Domain` scaffolded (net10.0, references nothing), folders `Parties/ Units/ Leasing/ Shared/`
- [x] 1.4 `Party.cs` + `PartyRole.cs` (Lessor/Tenant/Codebtor — no Garante)
- [x] 1.5 `Address.cs` owned value object under `Shared/`
- [x] 1.6 `Unit.cs` (abstract, TPH) + `PropertyUnit.cs` + `ParkingUnit.cs` + `UnitType.cs`
- [x] 1.7 `Contract.cs` aggregate root with all lifecycle dates, canon, nullable honorarios, `ContractStatus.cs`, `EndReason.cs`
- [x] 1.8 Constructor always creates `Active` state, no Draft path
- [x] 1.9 `GiveNotice(...)` → `PendingTermination`; `End(reason, date)` rejects a null reason
- [x] 1.10 `ContractParty.cs` (join, key = contract+party+role) + `Contract.AssignParty` enforcing exactly-one-Tenant and no duplicate Party+Role (Lessor/Codebtor unbounded)
- [x] 1.11 `ContractUnit.cs` (join, key = contract+unit) + `UnitShare.cs` readonly record struct
- [x] 1.12 `RentSplit.Equal(unitIds)`: truncates to 6dp, residue to lowest-id unit among ties, `decimal` only
- [x] 1.13 `Contract.SetUnitShares(shares)` + `RentSplitInvariantException`: rejects any split not summing to exactly 100m or containing a non-positive share
- [x] 1.14 `ContractDocument.cs` + `DocumentKind.cs`; `UploadedBy` is a plain `string`
- [x] 1.15 `Inmobiliaria.Desktop` WPF shell (`App.xaml`, `MainWindow.xaml`, csproj) — no `appsettings.json` yet (that is task 2.10)
- [x] 1.16 `.github/workflows/ci.yml`: `build` job id/`name:` untouched (windows-latest, full `.sln`); new `core` job (ubuntu-latest, `Inmobiliaria.Core.slnf`, builds + runs `dotnet test`); removed the `.sln`-glob skip conditional entirely
- [x] 1.17 `RentSplitTests.cs`: sum-to-100 for N=4, N=3 residue-to-lowest-id worked example (33.333334/33.333333/33.333333), 60/39.99 split rejected, `ChangeMonthlyRent` leaves stored shares untouched
- [x] 1.18 `ArchitectureGuardTests.cs`: reflection guard on `typeof(Contract).Assembly.GetReferencedAssemblies()` against `Microsoft.EntityFrameworkCore*`, `Npgsql*`, `PresentationFramework`, `PresentationCore`, `WindowsBase`
- [x] 1.19 `ContractLifecycleTests.cs`: always-Active construction, past-`NominalEndDate` stays Active, `GiveNotice` → `PendingTermination`, `End` without reason rejected, `End` with reason transitions correctly
- [x] 1.20 `ContractPartyAssignmentTests.cs`: two lessors, second-tenant rejection, 0/2 codebtors, same party as lessor+codebtor, duplicate party+role rejection
- [~] 1.21 Local-equivalent verification only (see Work Unit Evidence) — actual GitHub Actions run not performed (no PR opened by this agent, per instructions)

## Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `Inmobiliaria.sln` | Created | Root solution (classic `.sln` format — `.NET 10 SDK` defaults `dotnet new sln` to `.slnx`; forced `-f sln` since the design and a `.slnf` filter both require the classic format) |
| `Inmobiliaria.Core.slnf` | Created | Filters to `Inmobiliaria.Domain` + `Inmobiliaria.Domain.Tests` (excludes `Inmobiliaria.Desktop`) |
| `Directory.Build.props` | Created | `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` for all projects |
| `Directory.Packages.props` | Created | Central package versions: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector |
| `src/Inmobiliaria.Domain/Inmobiliaria.Domain.csproj` | Created | Empty SDK project — no `PackageReference`/`ProjectReference` at all |
| `src/Inmobiliaria.Domain/Parties/Party.cs`, `PartyRole.cs` | Created | Role-agnostic identity; DNI/CUIL both nullable per the proposal's open assumption |
| `src/Inmobiliaria.Domain/Shared/Address.cs` | Created | Owned VO, required fields validated in the constructor |
| `src/Inmobiliaria.Domain/Units/Unit.cs`, `PropertyUnit.cs`, `ParkingUnit.cs`, `UnitType.cs` | Created | TPH root + two field-free subclasses; enum values `Property`/`ParkingSpace` (matches spec wording) |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Created | Aggregate root: lifecycle, `AssignParty`, `SetUnitShares`, `ChangeMonthlyRent`, `GiveNotice`, `End` |
| `src/Inmobiliaria.Domain/Leasing/ContractParty.cs`, `ContractUnit.cs`, `UnitShare.cs`, `ContractDocument.cs`, `ContractStatus.cs`, `EndReason.cs`, `DocumentKind.cs`, `RentSplitInvariantException.cs` | Created | Joins, VOs, enums, exception |
| `src/Inmobiliaria.Domain/Leasing/RentSplit.cs` | Created | Deterministic equal-split + residue rule, `decimal` only |
| `src/Inmobiliaria.Desktop/*` | Created (via `dotnet new wpf`, retargeted) | WPF shell only; references `Inmobiliaria.Domain`; no `appsettings.json` (task 2.10 owns that) |
| `tests/Inmobiliaria.Domain.Tests/*` | Created (via `dotnet new xunit`, retargeted to central package management) | `RentSplitTests`, `ArchitectureGuardTests`, `ContractLifecycleTests`, `ContractPartyAssignmentTests`, `ContractTestFactory` (shared builders) |
| `.github/workflows/ci.yml` | Modified | `build` job id/name untouched; removed the `.sln`-glob skip conditional; added `core` job (ubuntu-latest) that restores/builds/tests `Inmobiliaria.Core.slnf` |

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and result | `dotnet test tests/Inmobiliaria.Domain.Tests` → **15/15 passed**, 0 failed, 0 skipped |
| Runtime harness | N/A for this slice — pure domain unit tests, no database, no Testcontainers (Slice 3). Build harness instead: `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors (Domain + Domain.Tests + WPF Desktop all compile, including the WPF project, which only builds on Windows) |
| Rollback boundary | Entire PR is additive (new files) except `.github/workflows/ci.yml`. Reverting the PR removes all new projects/files and restores the original single-job CI with its `.sln`-glob skip conditional. No database, no migration, nothing else touched. |

## Deviations from Design

- **`.slnf` path separators**: the design shows no exact syntax; used Windows backslash paths (`src\\Inmobiliaria.Domain\\...`) matching what `dotnet sln add` wrote into `Inmobiliaria.sln` itself.
- **`.NET 10 SDK now defaults `dotnet new sln` to the new `.slnx` XML format`, not classic `.sln`.** Not called out in the design (written before this was tested against the actual installed 10.0.302 SDK). Forced `-f sln` explicitly. Flagging because a repo-wide `dotnet new sln` without `-f sln` in any future change would silently produce a `.slnx` that neither the CI glob nor `Inmobiliaria.Core.slnf` can consume.
- **Desktop → Domain project reference added in Slice 1**, ahead of Infrastructure (which does not exist until Slice 2). The design's dependency graph lists Desktop → Infrastructure, Domain; only the Domain half exists yet. This is forward-compatible and adds no forbidden reference.
- **`ChangeMonthlyRent` method added to `Contract`**, not explicitly named in tasks.md, but required to test and satisfy the lease-contract spec scenario "Canon change leaves percentages untouched" — there was otherwise no way to change the total canon after construction.
- No other deviations — implementation otherwise matches design.md exactly (enum-as-string-later mapping, precision choices, etc. are Slice 2 concerns, not touched here).

## Issues Found

None functionally. See Risks below for the review-budget overage, which is the one substantive issue.

## Remaining Tasks (Slice 1, as of that slice)

- [ ] Slice 2 (PR 2): `Inmobiliaria.Infrastructure`, `DbContext`, 6 EF configurations, secrets wiring
- [ ] Slice 3 (PR 3): initial migration, deferred trigger, Testcontainers tests, `core` ruleset addition
- [ ] Prerequisites P.1/P.2 (dedicated Supabase org + Postgres major version) — non-code, human follow-up, unstarted

## Slice 1 Workload / PR Boundary

- Mode: single PR (sequential delivery per the user's decision — Slice 1 ships as PR 1 to `develop`, merges before Slice 2 starts)
- Current work unit: Slice 1 only, as scoped
- Boundary: starts at an empty repo (no `.sln`/`.csproj` existed); ends at a green two-job CI with Domain + Domain.Tests + WPF shell all compiling and 15 domain tests passing
- **Estimated review budget impact: OVER BUDGET.** See Risks.

## Slice 1 Status

20/21 Slice 1 tasks complete (1.21 partially — local-equivalent build/test green, actual GitHub Actions run not performed since no PR exists yet).

---

## Slice 2 (PR 2) — Infrastructure: DbContext & EF Mappings

**Mode**: Standard. `Inmobiliaria.Infrastructure` project, `InmobiliariaDbContext`, six `IEntityTypeConfiguration<T>` classes, design-time factory, secrets wiring. No migration, no Testcontainers, no trigger SQL — those are Slice 3, not started here.

### Completed Tasks

- [x] 2.1 `Inmobiliaria.Infrastructure.csproj` (net10.0 → Domain); `Microsoft.EntityFrameworkCore` 10.0.7, `Microsoft.EntityFrameworkCore.Design` 10.0.7, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions` 10.0.1 — all pinned in `Directory.Packages.props` (central package management)
- [x] 2.2 `InmobiliariaDbContext.cs`: six `DbSet`s (`Parties, Units, Contracts, ContractParties, ContractUnits, ContractDocuments`); `OnConfiguring` calls `.UseSnakeCaseNamingConvention()` so the convention applies regardless of how the options were built externally (design-time factory today, desktop host or test fixture later); `OnModelCreating` applies all configurations from the assembly
- [x] 2.3 `PartyConfiguration.cs`: unique, nullable-safe (`HasFilter("... IS NOT NULL")`) indexes on `dni` and `cuil`
- [x] 2.4 `UnitConfiguration.cs`: `HasDiscriminator<string>("unit_type").HasValue<PropertyUnit>("property").HasValue<ParkingUnit>("parking")`; `.OwnsOne(Address)` flattened to `address_street/number/floor/apartment/city/province/postal_code`
- [x] 2.5 `ContractConfiguration.cs`: `monthly_rent numeric(14,2)`; `honorarios_percentage numeric(5,2)` nullable with a CHECK allowing NULL or 0–100; `Status`/`EndReason` mapped `.HasConversion<string>()` with matching CHECK constraints; `Parties`/`Units` navigations forced to field access mode (backing `List<T>` fields, read-only public properties)
- [x] 2.6 `ContractPartyConfiguration.cs`: composite PK `(contract_id, party_id, role)`; `role` as `text` + CHECK `IN ('Lessor','Tenant','Codebtor')` — no Garante value; FK to `Contract.Parties` (cascade) and to `Party` (restrict)
- [x] 2.7 `ContractUnitConfiguration.cs`: composite PK `(contract_id, unit_id)`; `share_percentage numeric(9,6)` CHECK `> 0 AND <= 100`; FK to `Contract.Units` (cascade) and to `Unit` (restrict)
- [x] 2.8 `ContractDocumentConfiguration.cs`: `kind` as `text` + CHECK `IN ('Original','Addendum','TerminationNotice')`; `uploaded_by` plain required `text` column, no FK (no user entity exists)
- [x] 2.9 `DesignTimeDbContextFactory.cs`: reads `INMOBILIARIA_DB` env var, falls back to an offline placeholder connection string pointing at nothing real (`localhost`/`placeholder`/`placeholder`)
- [x] 2.10 `Inmobiliaria.Desktop.csproj`: added `<UserSecretsId>` (fresh GUID); committed `appsettings.json` with only a `Logging` section — **no `ConnectionStrings` section, no secret in any committed file**. The documented local-dev key is `ConnectionStrings:SupabasePostgres`, set via `dotnet user-secrets set` (no consuming code yet — that is host-wiring work, not in this slice's task list)
- [x] 2.11 Verify: `Inmobiliaria.Infrastructure` builds green under both the full `.sln` and `Inmobiliaria.Core.slnf` (local-equivalent commands — see Work Unit Evidence); no migration exists yet

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `Directory.Packages.props` | Modified | Added 4 pinned EF Core/Npgsql/naming-convention package versions |
| `Inmobiliaria.Core.slnf` | Modified | Added `Inmobiliaria.Infrastructure` to the filter so the Linux `core` job builds it |
| `Inmobiliaria.sln` | Modified | `dotnet sln add` for the new project (generated GUID/config-platform boilerplate, not authored) |
| `src/Inmobiliaria.Infrastructure/Inmobiliaria.Infrastructure.csproj` | Created | net10.0 class library, `ProjectReference` to Domain only |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Created | Six `DbSet`s, snake_case convention wiring, applies all configurations |
| `src/Inmobiliaria.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | Created | Env-var-first, offline-placeholder-fallback factory for `dotnet ef` |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/PartyConfiguration.cs` | Created | DNI/CUIL unique nullable-safe indexes |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/UnitConfiguration.cs` | Created | TPH discriminator + owned `Address` |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractConfiguration.cs` | Created | Money/percentage precision, enum-as-text + CHECK, field-access navigations |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractPartyConfiguration.cs` | Created | Composite PK, role enum CHECK, FKs |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractUnitConfiguration.cs` | Created | Composite PK, share-percentage CHECK, FKs |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractDocumentConfiguration.cs` | Created | Document-kind enum CHECK, plain `uploaded_by` column |
| `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj` | Modified | `UserSecretsId` added; `appsettings.json` wired to copy to output |
| `src/Inmobiliaria.Desktop/appsettings.json` | Created | `Logging` section only — no `ConnectionStrings` |

### Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and result | `dotnet build src/Inmobiliaria.Infrastructure -c Release` succeeds as part of `dotnet build Inmobiliaria.sln -c Release` → **0 warnings, 0 errors**, all 4 projects (Domain, Infrastructure, Desktop, Domain.Tests) compile |
| Runtime harness command/scenario and exact result | `dotnet build Inmobiliaria.Core.slnf -c Release` → **0 warnings, 0 errors** (Domain + Infrastructure + Domain.Tests, the Linux-equivalent project set); N/A for an actual DB round-trip — no migration exists yet (Slice 3), so no runtime DB call is possible or expected in this slice |
| Regression check | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release` → **15/15 passed**, confirming `ArchitectureGuardTests` still finds no `Microsoft.EntityFrameworkCore*`/`Npgsql*` reference from `Contract`'s assembly (Domain), i.e. the dependency edge points Infrastructure → Domain only |
| Rollback boundary | Entirely additive except `Directory.Packages.props`, `Inmobiliaria.Core.slnf`, `Inmobiliaria.sln`, and `Inmobiliaria.Desktop.csproj` (all four are small, mechanical, reversible edits). Reverting the PR removes `Inmobiliaria.Infrastructure` entirely; no migration, no database, nothing else touched |

### Deviations from Design

- **`UnitType.Ignore()` on the base `Unit` configuration.** The design's literal fluent snippet (`HasDiscriminator<string>("unit_type")...`) creates a shadow discriminator column independent of the CLR `UnitType` enum property. Mapping both would put the same fact in two columns (`unit_type` shadow discriminator + a hypothetical `unit_type` or `unittype` column from the enum), so the CLR property is explicitly ignored and the TPH discriminator is the single source of truth for the column. Not spelled out character-by-character in the design, but it is the only way to apply the design's exact fluent call without a duplicate column.
- **`Contract.Parties`/`Contract.Units` navigation access mode.** Explicitly set to `PropertyAccessMode.Field` in `ContractConfiguration` even though EF Core's backing-field convention would likely find `_parties`/`_units` automatically. Made explicit rather than relying on convention, since these are read-only (`IReadOnlyCollection<T>`) properties with no public setter.
- **No `ProjectReference` from `Inmobiliaria.Desktop` to `Inmobiliaria.Infrastructure` yet.** Task 2.10 only asks for `UserSecretsId` + `appsettings.json`; no task in this slice asks for DI/host wiring that would consume `InmobiliariaDbContext` from the Desktop project, so no reference was added to avoid an unused dependency. The design's Decision 1 project graph shows this edge as the target state; it is not required by any Slice 2 task.
- No other deviations — enum/CHECK strategy, snake_case naming, decimal precision/scale, and key strategy all match design.md Decision 2 exactly.

### Issues Found

None. Actual authored diff for this slice (`git diff --stat` over the new/changed Infrastructure and Desktop files, plus `Directory.Packages.props`/`Inmobiliaria.Core.slnf`, excluding the machine-generated `.sln` edit) is **327 added lines**, comfortably inside the 380–450 estimate — no `size:exception` needed for this PR.

## Combined Remaining Tasks (this change, later slices — NOT started, per scope)

- [ ] Slice 3 (PR 3): initial migration, deferred constraint trigger, Testcontainers tests, `core` ruleset addition
- [ ] Prerequisites P.1/P.2 (dedicated Supabase org + Postgres major version) — non-code, human follow-up, unstarted

## Slice 2 Workload / PR Boundary

- Mode: single PR (sequential delivery — Slice 2 ships as PR 2 to `develop` after Slice 1 merges)
- Current work unit: Slice 2 only, as scoped
- Boundary: starts at Slice 1's merged state (Domain + WPF shell + two-job CI); ends at a green build of `Inmobiliaria.Infrastructure` on both `build` and (locally verified) `core`, with no migration yet
- Estimated review budget impact: within budget (327 authored lines against 380–450 estimated)

## Combined Status (superseded by Slice 3 section below for the current state)

Slice 1: 20/21 tasks complete (1.21 partial, GitHub Actions run pending a PR). Slice 2: 11/11 tasks complete (2.11 partial in the same sense — local-equivalent verification only, no PR opened by this agent). Slice 3 and Prerequisites P.1/P.2 untouched, as scoped. Ready for `sdd-verify` on Slice 2.

---

## Slice 3 (PR 3) — Initial Migration, Trigger, Testcontainers, `core` Ruleset

**Mode**: Standard. Generates the first EF Core migration, hand-appends the deferred
constraint trigger for the rent-split invariant (design.md Decision 3), scaffolds
`Inmobiliaria.Infrastructure.Tests` with a Testcontainers-backed Postgres fixture, and updates
`openspec/config.yaml`'s testing block. Per this agent's explicit instructions: **no**
`git commit`/`add`/`push`, **no** `dotnet ef database update` and **no** connection to the real
Supabase project were performed.

### Completed Tasks

- [x] 3.1 Migration generated: `dotnet ef migrations add InitialSchema -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure -o Persistence/Migrations` (the `-o` deviates from the literal task command so the files land at `src/Inmobiliaria.Infrastructure/Persistence/Migrations/`, matching design.md's file table — the plain command without `-o` would have produced project-root `Migrations/`).
- [x] 3.2 Hand-appended `assert_contract_share_sum()` function + `CREATE CONSTRAINT TRIGGER contract_units_share_sum ... DEFERRABLE INITIALLY DEFERRED` to `Up`; matching `DROP TRIGGER`/`DROP FUNCTION` in `Down`. The function also short-circuits (`RETURN NULL`) when the parent `contracts` row no longer exists in the same transaction, so a full-contract cascade delete (which empties `contract_units` first) is not itself blocked by the sum check.
- [x] 3.3 Confirmed: image pinned to exact `postgres:17.6` (not `-alpine`, not floating), matching `openspec/config.yaml`'s `resolved_decisions: postgres-version`.
- [x] 3.4 `PostgresFixture.cs`: `ICollectionFixture` calling `context.Database.MigrateAsync()`, never `EnsureCreated()`. Both `PostgreSqlBuilder(...).Build()` and `.StartAsync()` are wrapped in the same try/catch — `Build()` alone throws first when the daemon is unreachable (it validates the Docker endpoint), which a first draft of this fixture missed and had to be corrected after a real local test run surfaced it.
- [x] 3.5 `SchemaConstraintTests.DeferredTrigger_RejectsCommittedSplitNotSummingTo100` (raw-SQL single-row insert inside an explicit transaction, summing to 60%, rejected at `CommitAsync()`) and `DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow` (positive control: `Contract.SetUnitShares` + EF `SaveChangesAsync()` inserting two rows one at a time in one transaction, must succeed — this is the test that actually exercises why `DEFERRABLE INITIALLY DEFERRED` is load-bearing).
- [x] 3.6 `DuplicateContractUnitCompositeKey_Rejected` (two `UnitShare`s for the same `UnitId` — the domain does not dedupe a shares list, so the DB composite PK is the real backstop) and `DuplicateContractPartyCompositeKey_Rejected` (raw SQL, since `Contract.AssignParty` already rejects an in-memory duplicate before it ever reaches the DB).
- [x] 3.7 `InvalidRoleCheckConstraint_Rejected` ('Garante'), `InvalidStatusCheckConstraint_Rejected` ('Draft'), `InvalidDocumentKindCheckConstraint_Rejected` ('Draft') — all via raw SQL bypassing the C# enum type system, which is the only way to reach the DB CHECK at all.
- [x] 3.8 `TphRoundTrip_PreservesConcreteUnitType` — writes a `PropertyUnit` and a `ParkingUnit` in one context, reads both back from a fresh context, asserts `IsType<PropertyUnit>`/`IsType<ParkingUnit>`.
- [x] 3.9 `TwoSequentialContractsOnSameUnit_BothRemainReadable` — an expired and a current contract both reference the same unit at 100%; both `contract_units` rows persist and are queryable (no overwrite, no availability column exists to conflict).
- [x] 3.10 `OriginalAndAddendumDocuments_CoexistForOneContract`.
- [x] 3.11 `DuplicateDni_RejectedByUniqueIndex`, `DuplicateCuil_RejectedByUniqueIndex`.
- [x] 3.12 No `core` job step edit was needed: adding `Inmobiliaria.Infrastructure.Tests` to `Inmobiliaria.Core.slnf` is sufficient for the existing `dotnet test Inmobiliaria.Core.slnf` step to pick it up. Docker is present by default on `ubuntu-latest` GitHub-hosted runners, so the Testcontainers tests execute for real there, not skipped.
- [x] 3.13 `openspec/config.yaml`: `testing.status: available`; `apply.test_command` / `verify.test_command` / `verify.build_command` filled; new `ef_testing_decision` key records the Testcontainers-vs-Supabase-schema decision and the `Migrate()`-not-`EnsureCreated()` rule.
- [ ] 3.14 **Not done — explicitly forbidden for this agent.** Applying the migration to the real Supabase project and one manual Npgsql smoke insert/read is a human follow-up after this PR merges, using the developer's own connection string (never seen or requested by this agent).
- [ ] 3.15 **Not done — manual, admin-only, and cannot happen until `core` has run at least once on an actual PR.** Left unchecked and labelled as a required human follow-up.
- [x] 3.16 Repo-wide "garante" check: every remaining occurrence is a deliberate one that documents or proves the exclusion (see tasks.md for the full list); no enum member, column value, or identifier derived from it exists.
- [x] 3.17 Verified: `build` job id/`name:` in `.github/workflows/ci.yml` are still literally `build` (lines 16-17).

### A materialization bug found and fixed in already-merged Slice 1 Domain code

Running `dotnet ef migrations add` for the first time (this is the first point in the whole
change where the EF model is actually *built*, not just compiled) surfaced a real EF Core
constructor-binding limitation that Slices 1-2 could not have caught, because no migration had
ever been generated before:

- **Root cause**: EF Core's constructor-binding convention requires every constructor parameter
  to bind to a property already known to the model. A get-only property with **no setter at
  all** (not even `private set`) is only bindable through a constructor parameter; it can never
  be set afterward via reflection. `Contract.StartDate`/`NominalEndDate` and four
  `ContractDocument` properties (`StoragePath`, `FileName`, `ContentType`, `UploadedAt`) are
  exactly this shape and were not explicitly mapped in their `IEntityTypeConfiguration<T>`, so EF
  could not resolve them as constructor-bindable and migration generation failed with
  "no suitable constructor was found". **Fix**: added explicit `.Property(...)` calls for all
  five in `ContractConfiguration.cs` / `ContractDocumentConfiguration.cs`. No column names,
  types, or precisions changed — this only makes properties EF already intended to map visible
  to the constructor-binding convention.
- **A second, structural instance**: `PropertyUnit`/`ParkingUnit`'s public constructors take
  `Address` (an **owned navigation**) as a parameter. EF Core's constructor binding explicitly
  refuses to bind navigations/owned types this way ("Navigations to related entities, including
  references to owned types, cannot be bound"), so no amount of `.Property()` configuration
  fixes this — the owned reference must never be a constructor parameter EF has to bind. Adding
  a `.Property()` call for the four date/string properties above worked because those are plain
  scalars; it does **not** work for `Address`.
  **Fix**: added a `protected Unit(Guid id, UnitType unitType)` materialization-only constructor
  on the abstract `Unit` base (`Address` left `null!`, documented as EF-only, leaving `Id` and
  `UnitType` bound the normal way since they are plain scalars), and a matching private
  1-parameter constructor on `PropertyUnit`/`ParkingUnit` that forwards to it. EF now selects
  this fully-bindable constructor over the public 2-parameter one (which remains the only public,
  application-facing way to construct a unit) and sets `Address` afterward through its existing
  `private set` — no access-mode configuration change was needed since `UnitConfiguration.cs`
  already relies on the default property access mode for `Address`.
  This is a real, necessary change to already-merged Slice 1 domain code, not a design deviation:
  without it, no `PropertyUnit`/`ParkingUnit` could ever be materialized by EF at all, which
  would have surfaced as a runtime failure the first time any query touched `units`, not as a
  migration-generation error. `dotnet test tests/Inmobiliaria.Domain.Tests` (15/15) still passes
  after the change, confirming no public Domain behavior moved.

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `Directory.Packages.props` | Modified | Added `Testcontainers.PostgreSql` 4.15.0, `Xunit.SkippableFact` 1.4.13, an explicit `Microsoft.EntityFrameworkCore.Relational` 10.0.7 pin, and `CentralPackageTransitivePinningEnabled=true` (fixes an MSB3277 assembly-version conflict: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 transitively drags in `Microsoft.EntityFrameworkCore.Relational` 10.0.4, colliding with the 10.0.7 pinned everywhere else) |
| `Inmobiliaria.sln` | Modified | `dotnet sln add` for the new test project (generated GUID/config-platform boilerplate) |
| `Inmobiliaria.Core.slnf` | Modified | Added `Inmobiliaria.Infrastructure.Tests` so the Linux `core` job builds and runs it |
| `src/Inmobiliaria.Domain/Units/Unit.cs` | Modified | Added the EF materialization-only `protected Unit(Guid, UnitType)` constructor — see bug note above |
| `src/Inmobiliaria.Domain/Units/PropertyUnit.cs`, `ParkingUnit.cs` | Modified | Added the matching private 1-parameter materialization constructor on each concrete type |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractConfiguration.cs` | Modified | Explicit `.Property()` mapping for `StartDate`, `NominalEndDate`, `NoticeGivenDate`, `PlannedMoveOutDate`, `ActualEndDate` — see bug note above |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractDocumentConfiguration.cs` | Modified | Explicit `.Property()` mapping for `StoragePath`, `FileName`, `ContentType`, `UploadedAt` |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260913215911_InitialSchema.cs` | Created | EF-generated `CreateTable`/`CreateIndex` calls (six tables) plus the hand-written trigger function/trigger in `Up` and matching `DROP` in `Down` |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260913215911_InitialSchema.Designer.cs`, `InmobiliariaDbContextModelSnapshot.cs` | Created | Fully EF-generated; diff noise per design.md, not authored risk |
| `tests/Inmobiliaria.Infrastructure.Tests/Inmobiliaria.Infrastructure.Tests.csproj` | Created | Central-package-managed xunit + `Xunit.SkippableFact` + `Testcontainers.PostgreSql`; references both Domain and Infrastructure |
| `tests/Inmobiliaria.Infrastructure.Tests/PostgresFixture.cs` | Created | Collection fixture: starts/migrates a pinned `postgres:17.6` container, or records `SkipReason` when Docker is unreachable |
| `tests/Inmobiliaria.Infrastructure.Tests/SchemaConstraintTests.cs` | Created | 12 `[SkippableFact]`s covering tasks 3.5-3.11 |
| `openspec/config.yaml` | Modified | `testing.status: available`; `apply`/`verify` `test_command`/`build_command` filled; `ef_testing_decision` key added |

### Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release` → **15/15 passed**, 0 failed (regression: the `Unit`/`PropertyUnit`/`ParkingUnit` constructor change did not alter public Domain behavior) |
| Runtime harness command/scenario and exact result | `dotnet test tests/Inmobiliaria.Infrastructure.Tests -c Release` → **0 passed, 0 failed, 12 skipped**, each with an explicit `SkipReason` ("Docker is unreachable locally..."). Docker Desktop (29.4.3) is installed on this machine but its daemon never came up in this session even after launching it and polling for ~2 minutes — this agent cannot force it further in a non-interactive session. This is exactly the local-skip path design.md specifies; it was not silently assumed; it was actually exercised and produced the correct graceful-skip behavior end-to-end, including a real bug catch (an early draft only guarded `StartAsync()`, not `Build()`, and a real local run surfaced the miss because `Build()` throws first when the daemon is down). **The 12 tests have never executed against a live Postgres in this session** — that verification is deferred to the `core` CI job on `ubuntu-latest`, which always has Docker. |
| Combined command | `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors, all 5 projects compile. `dotnet test Inmobiliaria.Core.slnf -c Release` → Domain.Tests 15/15 passed, Infrastructure.Tests 12/12 skipped (Docker), matching the two commands above run together |
| Rollback boundary | `dotnet ef database update 0` reverses the schema (the `Down` drops the trigger/function first, matching design.md Decision 7) — **not run**, since no environment was ever applied to. Reverting this PR removes the migration, the new test project, the two Slice 2 configuration edits, and the two Domain constructor-only additions; nothing else is touched. No database was ever connected to in this session |

### Deviations from Design

- **Migration output folder.** `dotnet ef migrations add ... -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` alone (the literal task 3.1 command) generates to project-root `Migrations/`. Added `-o Persistence/Migrations` so the files land where design.md's file table says they should (`src/Inmobiliaria.Infrastructure/Persistence/Migrations/*_InitialSchema.cs`).
- **Two Domain-layer materialization constructors added** (`Unit`, `PropertyUnit`, `ParkingUnit`) and **five explicit `.Property()` mappings added** (`ContractConfiguration`, `ContractDocumentConfiguration`) — both described in detail above. Neither changes any column name, type, precision, or public API signature; both were required for `dotnet ef migrations add` to succeed at all, and neither design.md nor tasks.md anticipated this specific EF Core constructor-binding limitation because no migration had ever been attempted before this slice.
- **Trigger function also checks whether the parent `contracts` row still exists**, returning early (no-op) if not. This is not in the design's literal SQL snippet (which shows only the `CREATE CONSTRAINT TRIGGER` statement, not the full function body) but is necessary: without it, deleting an entire contract (which cascades and empties `contract_units` first) would itself violate the "sums to 100" check at commit, since zero rows sum to zero. This does not touch the explicitly accepted residual gap (a contract that never had any `contract_units` rows in the first place) — see the restated decision below.
- **`Xunit.SkippableFact` (MIT) added as a new dependency**, not named in design.md's file table. xunit v2 (pinned here at 2.9.3) has no native runtime-skip mechanism; without it, a Docker-unreachable run would either fail every fact or require aborting the whole fixture non-gracefully. This is the standard, narrowly-scoped idiom for exactly the "skip locally, always run in CI" requirement design.md Decision 4 specifies.
- **`CentralPackageTransitivePinningEnabled` added to `Directory.Packages.props`** — not previously present. Needed once a second package (`Npgsql.EntityFrameworkCore.PostgreSQL`) pulled a differently-versioned transitive `Microsoft.EntityFrameworkCore.Relational`; without it, `dotnet build` succeeds but emits an unresolved assembly-version-conflict warning (MSB3277) that a later slice would otherwise have to debug from scratch.

### Restated (not resolved) design decision: zero-unit contracts

design.md Decision 3 explicitly accepts that a contract with **zero** `contract_units` rows is
database-legal (a row-level trigger can never fire when there are no rows to fire on), even
though the domain forbids constructing such a `Contract` in the first place. This slice does
**not** attempt to close that gap with a statement-level trigger or any other DB-side guard —
design.md rejected that as over-engineering for two internal users, and this agent found no
new information that changes that trade-off. Restated here, not silently fixed.

### Issues Found

None beyond the two materialization-constructor issues and the `Build()`-vs-`StartAsync()` skip
guard, all described and fixed above. No test failed for a reason other than "Docker
unreachable" (an intentional skip, not a failure).

### Combined Remaining Tasks (this change)

- [ ] 3.14 Manual Supabase smoke check — human follow-up after merge, out of scope for this agent by explicit instruction.
- [ ] 3.15 Admin adds `core` to the GitHub ruleset — human follow-up, blocked until `core` has run on an actual PR.
- [ ] Prerequisites P.1/P.2 — non-code, human follow-up (P.2's Postgres version is now resolved per `openspec/config.yaml`'s `resolved_decisions`, but the checkbox itself lives in tasks.md's Prerequisites section, outside this agent's assigned Slice 3 scope).

### Slice 3 Workload / PR Boundary

- Mode: single PR (sequential delivery — Slice 3 ships as PR 3 to `develop` after Slice 2 merges)
- Current work unit: Slice 3 only, as scoped
- Boundary: starts at Slice 2's state (Infrastructure + 6 configurations, no migration); ends at a generated-and-hand-completed migration, a passing (locally skipped, CI-live) Testcontainers test suite, and an updated `config.yaml` testing block
- Estimated review budget impact: authored risk (excluding EF-generated `Designer.cs`/`ModelSnapshot.cs`/`CreateTable` boilerplate, which design.md explicitly designates as non-authored diff noise) is comfortably inside the 450-650 estimate — see Files Changed for the generated-vs-hand-written split

## Combined Status

Slice 1: 20/21 tasks complete (1.21 partial, GitHub Actions run pending a PR). Slice 2: 11/11
tasks complete (2.11 partial, same reason). Slice 3: 15/17 tasks complete (3.14 and 3.15 are
explicit, out-of-scope human follow-ups — see above). Prerequisites P.1/P.2: non-code, human
follow-up, P.2's underlying fact is now resolved in `openspec/config.yaml`. Ready for
`sdd-verify` on Slice 3.
