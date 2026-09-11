# Apply Progress: Contract and Parties — Slice 1 (PR 1)

**Mode**: Standard (strict_tdd: false). Slice 1 only — solution scaffolding, domain layer, CI split.

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

## Remaining Tasks (this change, later slices — NOT started, per scope)

- [ ] Slice 2 (PR 2): `Inmobiliaria.Infrastructure`, `DbContext`, 6 EF configurations, secrets wiring
- [ ] Slice 3 (PR 3): initial migration, deferred trigger, Testcontainers tests, `core` ruleset addition
- [ ] Prerequisites P.1/P.2 (dedicated Supabase org + Postgres major version) — non-code, human follow-up, unstarted

## Workload / PR Boundary

- Mode: single PR (sequential delivery per the user's decision — Slice 1 ships as PR 1 to `develop`, merges before Slice 2 starts)
- Current work unit: Slice 1 only, as scoped
- Boundary: starts at an empty repo (no `.sln`/`.csproj` existed); ends at a green two-job CI with Domain + Domain.Tests + WPF shell all compiling and 15 domain tests passing
- **Estimated review budget impact: OVER BUDGET.** See Risks.

## Status

20/21 Slice 1 tasks complete (1.21 partially — local-equivalent build/test green, actual GitHub Actions run not performed since no PR exists yet). Slice 2 and Slice 3 tasks untouched, as scoped. Ready for `sdd-verify` on Slice 1, pending the line-budget decision below.
