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

- [ ] Phase 2 (PR 2a): EF Configuration + Entity Changes
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

### Status

8/8 Phase 1 tasks complete. Ready for verify (or for the orchestrator to proceed to PR 2a / Phase 2
once the user has reviewed and merged this PR).
