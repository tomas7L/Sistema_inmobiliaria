# Tasks: ui-de-cobranza (the cash desk, the recibo on paper, and go-live setup)

Turns `design.md` section 13 (six slices, S1 to S4b) into ordered, checkable work. It does not re-plan the
change and does not edit `spec.md` or `design.md`. Where slicing exposed a gap, it is listed under
**Issues found** at the end.

Domain vocabulary: **recibo** = receipt (non-fiscal, one per copy); **locatario** = tenant; **propietario** =
lessor/owner; **recargo** = late-payment surcharge; **cutover** = the go-live date of a contract that
predates the system; **starting number** = the first recibo number the system issues; **anulado** = voided.

Slice to phase map (one slice = one phase = one PR): S1 = Phase 1, S2 = Phase 2, S3a = Phase 3,
S3b = Phase 4, S4a = Phase 5, S4b = Phase 6.

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, the **Suggested Work Units** table, the **Human Follow-Ups** (H.1 to H.5) and **Issues found** at the end | Everything inside Phases 1 to 6 except the **Verification** table at the end of each phase. The checkbox lists are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 2,950 authored (additions + deletions, tests included), design section 13 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | High: four of six slices are estimated at 450 to 600 lines, and the previous change landed its slices at up to 2.5x their estimates |
| Chained PRs recommended | Yes |
| Suggested split | PR1 (S1) to PR6 (S4b), forced order, one slice per PR |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main (Tomás, 2026-10-22): each slice PR targets `develop` once the previous slice has merged. No PR is opened on top of an unmerged one |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

**Why some slices exceed 400 lines, and why they are not split further.** S2 (about 550) is one migration,
one domain rule, one port and one screen that only make sense together. S3b (about 600) is the whole cash
desk flow: picker, preview, summary, commit and the stale diff share one view-model state machine, and
splitting them would leave a screen that cannot commit. Design D27 names the optional split points
(S2 into domain plus migration and port plus screen; S3b into picker and desk flow); they stay unused unless
a slice measurably overshoots during apply, in which case the assistant stops and reports before going on.

**Working agreements that apply to every phase** (from `CLAUDE.md`): the assistant writes files and leaves
them **uncommitted**; Tomás commits, pushes and opens the PR (GitHub Desktop). At the end of each phase the
assistant hands over the file list and a Conventional Commit message, with no `Co-Authored-By`. At the start
of the next session, before continuing, the **second inspection** runs (task X.1 of each phase): did the last
merge land intact, and does what is about to be built still match the code.

**Preconditions, announced before each phase starts**

| Phase | Docker | Supabase | Model change |
|---|---|---|---|
| 1 (S1) | **Yes** (its guard tests live in `Inmobiliaria.Infrastructure.Tests`, whose other tests need the container) | No | No |
| 2 (S2) | **Yes** | **Tomás only, restored after S2 merges, to apply the migration (H.1).** The assistant never handles the password | No EF model change; one hand-written migration |
| 3 (S3a) | **Yes** | No | No |
| 4 (S3b) | **Yes** (full suite) | No | No |
| 5 (S4a) | **Yes** (full suite) | No | No |
| 6 (S4b) | **Yes** (full suite) | No | No |

A skipped Testcontainers test is an **unrun** test, not a passing one: every verification below requires
0 skipped.

**Test locations.** Tests go in `Inmobiliaria.Domain.Tests`, `Inmobiliaria.Infrastructure.Tests` or the new
`Inmobiliaria.Presentation.Tests`. **Never in `Inmobiliaria.Desktop`** (excluded from `Inmobiliaria.Core.slnf`,
so it cannot gate the Linux CI job).

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| 1 (S1) | Shell and plumbing: two new projects in sln and slnf, Presentation guard, `AddSessionServices` with per-call wrappers for the existing ports, recargo settings, `ShellViewModel`, `OperationRunner`, `MainWindow` shell. About 450 lines | PR 1 | `dotnet test tests/Inmobiliaria.Presentation.Tests -c Release` and `dotnet test tests/Inmobiliaria.Infrastructure.Tests -c Release --filter "FullyQualifiedName~SessionServices\|FullyQualifiedName~RecargoSettings"` | Spy factory plus connection-refusing interceptor (no database needed for the two session tests) | Revert the PR: remove the two projects from sln and slnf; `App.xaml.cs` returns to `new MainWindow()` |
| 2 (S2) | Go-live: cutover rule, `CutoverNotSet` text, hand-written migration (Admin-only cutover date), `IGoLiveSetup`, `GoLiveViewModel` and view, shared role-test helper. About 550 lines | PR 2 | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~Cutover"` and `dotnet test tests/Inmobiliaria.Infrastructure.Tests -c Release --filter "FullyQualifiedName~GoLive\|FullyQualifiedName~CutoverTrigger"` | Testcontainers `postgres:17.6` as real login roles (Admin, Empleado); `Down` on an `IsolatedDatabase`; `has-pending-model-changes` | Migration `Down` (restores the previous function body, lossless); revert the PR |
| 3 (S3a) | Read ports: `IAccountDirectory`, `IReciboReader`, adapters, wrappers, `ReciboRows` move. About 450 lines | PR 3 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests -c Release --filter "FullyQualifiedName~AccountDirectory\|FullyQualifiedName~ReciboReader\|FullyQualifiedName~CollectionDesk"` | Testcontainers, as each role | Revert the PR: nothing calls the ports yet; the `ReciboRows` move is covered by the unchanged desk tests |
| 4 (S3b) | Cash desk: picker, desk and recibo-display view-models and views, `PreviewDifference`, adjustment warning. About 600 lines | PR 4 | `dotnet test tests/Inmobiliaria.Presentation.Tests -c Release --filter "FullyQualifiedName~CashDesk\|FullyQualifiedName~AccountPicker\|FullyQualifiedName~PreviewDifference"` | Faked ports; real run needs Supabase and is Tomás's smoke (H.5) | Revert the PR: removes the desk screens only |
| 5 (S4a) | Recent list and voiding: `RecentCollectionsViewModel`, `VoidingGate`, `VoidingViewModel` and view. About 400 lines | PR 5 | `dotnet test tests/Inmobiliaria.Presentation.Tests -c Release --filter "FullyQualifiedName~Voiding\|FullyQualifiedName~RecentCollections"` | Faked ports | Revert the PR: removes the recent list and voiding screens |
| 6 (S4b) | Printing: print model, `AmountInWords`, `PaperRows`, `AgencyDetails`, `SheetArrangement`, `FlowDocumentReciboPrinter`, Print actions, `Agency` settings and logo. About 500 lines | PR 6 | `dotnet test tests/Inmobiliaria.Presentation.Tests -c Release --filter "FullyQualifiedName~Print\|FullyQualifiedName~AmountInWords\|FullyQualifiedName~PaperRows\|FullyQualifiedName~Agency\|FullyQualifiedName~SheetArrangement"` | Faked `IReciboPrinter`; the real paper behaviour is **M1 to M3, by Tomás**, on a real printer or Microsoft Print to PDF | Revert the PR: removes the printer and Print actions; `appsettings.json` loses the `Agency` section |

---

## Phase 1: Shell and plumbing (S1, PR 1, est. about 450 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests in this PR: 1, 2, 3, 4, 5, 6, 7. Design anchors:
D1, D2, D3, D22, D24, sections 3, 4, 10.

- [x] 1.1 **[Second inspection, read-only]** Confirm the design's anchors still match the code: `Inmobiliaria.Core.slnf` (read-only) excludes Desktop; `src/Inmobiliaria.Desktop/App.xaml.cs` (read-only) around the login success branch and `OnExit`; `src/Inmobiliaria.Infrastructure/Access/ApplicationServices.cs` (read-only) and `tests/Inmobiliaria.Infrastructure.Tests/CompositionGuardTests.cs` (read-only) as the wiring precedent; `src/Inmobiliaria.Infrastructure/Persistence/CollectionDesk.cs` (read-only) constructor `(context, clock, terms)`. With Docker up, run the full baseline once and record the counts (passed, failed, skipped) here
- [x] 1.2 **[Projects]** Create `src/Inmobiliaria.Presentation/Inmobiliaria.Presentation.csproj` (net10.0 inherited, references Domain only, package CommunityToolkit.Mvvm, no new version in `Directory.Packages.props` (read-only)) and `tests/Inmobiliaria.Presentation.Tests/Inmobiliaria.Presentation.Tests.csproj` (xunit as `Inmobiliaria.Domain.Tests`). Add both to `Inmobiliaria.sln` and `Inmobiliaria.Core.slnf`; add a Presentation reference to `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj`. Verify: restore and build exit code 0
- [x] 1.3 **[RED then GREEN, Presentation guard]** `tests/Inmobiliaria.Presentation.Tests/PresentationArchitectureGuardTests.cs`, modelled on `ArchitectureGuardTests` in Domain.Tests: Presentation references no `Inmobiliaria.Infrastructure`, WPF, EF Core or Npgsql assembly. **Prove it by making it fail:** add a temporary Infrastructure reference, watch the test go red, remove it, record here
- [x] 1.4 **[RED, Tests 5, 6]** `tests/Inmobiliaria.Presentation.Tests/OperationRunnerTests.cs` with a faked desk: an exception becomes a fixed Spanish message that never contains the exception text, the screen and typed values stay in place, `IsBusy` resets in `finally`, a failed commit adds the "may or may not have been recorded, check the recent collections" text (5); a refusal returned as a value is shown as its own operator text and never reaches the generic path (6). Watch it fail for the right reason (type missing)
- [x] 1.5 **[GREEN, Tests 5, 6]** `src/Inmobiliaria.Presentation/Common/OperationRunner.cs` and the generic-error message constants in `src/Inmobiliaria.Presentation/Common/ErrorMessages.cs` (D24)
- [x] 1.6 **[RED, Tests 1, 2]** `tests/Inmobiliaria.Presentation.Tests/ShellViewModelTests.cs`: shows the session's display name and role (1); an Empleado's navigation has the desk, the recent list and voiding and no go-live; an Admin's has all (2). In S1 the navigation entries for screens not built yet are declared as the full list the spec names, so Test 2 is proven once and not rewritten in later slices
- [x] 1.7 **[GREEN, Tests 1, 2]** `src/Inmobiliaria.Presentation/Shell/ShellViewModel.cs` and `src/Inmobiliaria.Presentation/Shell/NavigationEntry.cs`: built from the session, ports, clock and settings problems; navigation by role (D1)
- [x] 1.8 **[RED, Test 7]** `tests/Inmobiliaria.Infrastructure.Tests/RecargoSettingsTests.cs`: the shipped `src/Inmobiliaria.Desktop/appsettings.json` (read-only, located the way `CompositionGuardTests` walks to the repo root) yields rate 0.02 and 0 grace days; a missing rate, a non-number, a rate of 2 and negative grace days each produce one problem naming the key (`Recargo:DailyRate` or `Recargo:GraceDays`); no fallback value exists. The credential-shape guard in `CompositionGuardTests` stays green
- [x] 1.9 **[GREEN, Test 7]** `src/Inmobiliaria.Infrastructure/Access/RecargoSettings.cs` (`Read(JsonElement root)` returning terms or a named problem, using the `RecargoTerms` constructor's own range checks) and add only the `Recargo` section (`DailyRate` 0.02, `GraceDays` 0) to `src/Inmobiliaria.Desktop/appsettings.json`. The `Agency` section arrives in S4b
- [x] 1.10 **[RED, Tests 3, 4]** `tests/Inmobiliaria.Infrastructure.Tests/SessionServicesTests.cs`, no container: the session provider resolves `ICollectionDesk` and `IDueAdjustmentQuery` for an Admin session and an Empleado session while a spy factory's `Create()` count stays 0, and registers no `DbContext` (3); two calls on one wrapper obtain two distinct contexts, each disposed, using a context whose connection opening an interceptor refuses (4). Terms invalid means `ICollectionDesk` is not registered (D16)
- [x] 1.11 **[GREEN, Tests 3, 4]** `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs` (`AddSessionServices(factory, session, recargo)`), `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCall.cs` (shared `RunAsync`), `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCallCollectionDesk.cs` and `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCallDueAdjustmentQuery.cs` (D2, D3). The container never holds a `DbContext`
- [x] 1.12 **[Prove the session guard by making it fail]** Register a `DbContext` in `AddSessionServices` once in a scratch run: Test 3 must go red. Remove it, record here
- [x] 1.13 **[Desktop wiring, no behaviour]** `src/Inmobiliaria.Desktop/App.xaml.cs`: after both gates (forced password change untouched), read `Recargo`, build the second `ServiceCollection` and session provider, build `ShellViewModel`, show `MainWindow(shellViewModel)`, dispose the session provider in `OnExit` before the factory, add a `DispatcherUnhandledException` backstop showing the same generic message. `src/Inmobiliaria.Desktop/MainWindow.xaml` and `src/Inmobiliaria.Desktop/MainWindow.xaml.cs`: user, role and navigation list bound to the view-model; no business logic. Checked by build only here; the live smoke is H.5
- [x] 1.14 **[Guardrails]** `ArchitectureGuardTests` and `CompositionGuardTests` green; `dotnet ef migrations has-pending-model-changes` reports no changes (no model touched)
- [x] 1.15 **[Handover]** File list and Conventional Commit message (for example `feat(presentation): session shell, per-call services and recargo settings`) to Tomás. No commit by the assistant

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; read the **Advertencia(s) and Errores counts**, both 0. Never grep for a pattern |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; 0 skipped ("Con error", "Omitido"); passed = baseline + new tests; Presentation.Tests now run in the slnf |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: Go-live (S2, PR 2, est. about 550 lines)

**Docker: yes. Supabase: Tomás only, after this PR merges (H.1).** Spec tests in this PR: 13 (domain half),
27 to 40. Design anchors: D5, D6, D7, D25, D26, sections 5.3 and 6. S2 is independent of S3 and S4 and may move
after them if go-live is not urgent (design section 13), but the default order stands.

- [x] 2.1 **[Second inspection, read-only]** S1 landed intact (`src/Inmobiliaria.Presentation`, `AddSessionServices`, both projects in the slnf). The code still matches the design's anchors: `src/Inmobiliaria.Domain/Leasing/Contract.cs` (read-only) `SetCutoverDate` has no `today` parameter; `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs` (read-only) holds `reject_cutover_change_after_movement()`; the three `SetCutoverDate` calls in `tests/Inmobiliaria.Domain.Tests/ContractualFeePlanTests.cs` (read-only here, edited in 2.3). Docker up; record the baseline counts
- [x] 2.1a **[From the S1 review, R3-dispatcher-backstop-zombie]** `src/Inmobiliaria.Desktop/App.xaml.cs`: the `DispatcherUnhandledException` backstop marks every exception handled, so an exception during bootstrap (endpoint, login gates, `LoadRecargoSettings`) shows the generic box and leaves a windowless process alive (`ShutdownMode` is `OnExplicitShutdown`). After showing the message, shut down when no main window is open yet. Build check; behaviour checked in the live smoke (H.5)
- [x] 2.2 **[Shared role-test helper, no behaviour change]** Move `CreateTestLoginRoleAsync` and `BuildConnectionAs` out of `tests/Inmobiliaria.Infrastructure.Tests/RolePermissionTests.cs` into `tests/Inmobiliaria.Infrastructure.Tests/RoleTestHelpers.cs`; `RolePermissionTests` keeps passing unchanged (run it before and after)
- [x] 2.3 **[RED, Tests 32, 33, 34 domain half, 13 domain half]** `tests/Inmobiliaria.Domain.Tests/CutoverDateRuleTests.cs`: a date after today and a date before the contract start are refused with the stored date unchanged; today and the start date are accepted (32); order of checks own account, movement, after today, before start; a movement still blocks (34 domain half). `tests/Inmobiliaria.Domain.Tests/CollectionRefusalTextTests.cs`: the `CutoverNotSet` operator text names the starting recibo number and does not mention the contract's cutover date (13 domain half). Update the three calls in `tests/Inmobiliaria.Domain.Tests/ContractualFeePlanTests.cs` to pass a `today`, with their assertions untouched (33). Watch it fail to compile or fail for the right reason
- [x] 2.4 **[GREEN, Tests 13 domain half, 32, 33, 34 domain half]** `src/Inmobiliaria.Domain/Leasing/CutoverRefusal.cs`; in `src/Inmobiliaria.Domain/Leasing/Contract.cs` add `CheckCutoverDate(cutoverDate, account, today)` and give `SetCutoverDate` a required `today` (the two-argument overload is removed, not defaulted); in `src/Inmobiliaria.Domain/Collection/CollectionPlanner.cs` rewrite the `CutoverNotSet` text and its doc comment (D7). The enum member keeps its name, so `tests/Inmobiliaria.Infrastructure.Tests/CollectionDeskAtomicityTests.cs` (read-only) stays untouched
- [x] 2.5 **[RED, Tests 35, 36, 37, 38, 39]** `tests/Inmobiliaria.Infrastructure.Tests/CutoverTriggerTests.cs`, Testcontainers, connecting as real login roles through `RoleTestHelpers`: an Admin changes the cutover date and succeeds; an Empleado changing it by raw `UPDATE` is refused with `contracts_cutover_admin_only` (35); an Empleado can still end a contract, update the honorarios percentage and update naming `cutover_date` without changing it (36); the lock after a movement refuses both roles with `contracts_cutover_locked` (37); an Empleado can insert a contract carrying a cutover date and then cannot change it, the named limit (38); `Down` restores the original function body, run on an `IsolatedDatabase`, never on the shared container (39). Record which of these pass on the unmigrated database (36 to 38 are expected to pass already: they pin existing behaviour) and which go red (35, 39)
- [x] 2.6 **[GREEN, migration]** Generate with `dotnet ef migrations add HardenCutoverDateToAdmin -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` on the **unchanged** model; fill only `Up` and `Down` with `migrationBuilder.Sql` in `src/Inmobiliaria.Infrastructure/Persistence/Migrations/<timestamp>_HardenCutoverDateToAdmin.cs` (its `.Designer.cs` is generated). `Up` replaces only the body of `reject_cutover_change_after_movement()`: the existing lock first, then `pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER')` raising `P0001` with constraint `contracts_cutover_admin_only`; not `SECURITY DEFINER`; the trigger is not recreated. `Down` restores the original body verbatim. `git diff` shows **no change** to `src/Inmobiliaria.Infrastructure/Persistence/Migrations/InmobiliariaDbContextModelSnapshot.cs`
- [x] 2.7 **[Prove the Admin guard by making it fail]** Invert the `pg_has_role` line once in a scratch run: Test 35 must refuse an Admin (go red). Restore it; record here
- [x] 2.8 **[Test 40]** `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` exits 0 with no changes. Read the exit code, not the text
- [x] 2.9 **[RED, Tests 27, 28, 29, 30 port half, 31 adapter half]** `tests/Inmobiliaria.Infrastructure.Tests/GoLiveSetupTests.cs` (Testcontainers): the list shows each contract's cutover date or none, changeable or locked, and a contract with no account as `NoAccount` (27 port half); set and correct before any movement is accepted, refused as a `SetupRefusal` after a movement (28); starting-number state not set, set and changeable, set and locked, readable by both roles (29); 31001 then 31100 before any recibo is accepted and the first recibo carries 31100 (30 port half). `tests/Inmobiliaria.Infrastructure.Tests/SetupErrorMapTests.cs` (no container): the string-in mapping table for `contracts_cutover_locked`, `contracts_cutover_admin_only`, `recibo_numbering_cutover_locked`, `ck_recibo_numbering_starting_positive` and SQLSTATE `42501` scoped to the one statement (31 adapter half)
- [x] 2.10 **[GREEN, port and adapter]** `src/Inmobiliaria.Domain/GoLive/IGoLiveSetup.cs` (port, records, `SetupRefusal`, `SetupRefusalText` Spanish operator text), `src/Inmobiliaria.Infrastructure/Persistence/GoLiveSetup.cs` (adapter over the existing `ReciboNumbering` and `Contract.CheckCutoverDate`; refusals as values, anything else surfaces as an error) (D25), `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCallGoLiveSetup.cs`, and register it in `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`. Extend `tests/Inmobiliaria.Infrastructure.Tests/SessionServicesTests.cs` so Test 3 also resolves `IGoLiveSetup` for both roles
- [x] 2.11 **[RED, Tests 27 display half, 30 before-the-port half, 31]** `tests/Inmobiliaria.Presentation.Tests/GoLiveViewModelTests.cs` with a faked port: the list shows date or none, changeable or locked, "cannot be set: no account" (27); 0, -5, 12.5, text and a value above `int.MaxValue` are refused before the port is called (30); a lock arriving after the screen showed "changeable" is shown as a refusal for the cutover date and for the starting number, not as an error (31); state is re-read after any set; commands are unavailable while busy
- [x] 2.12 **[GREEN, view-model and shell]** `src/Inmobiliaria.Presentation/GoLive/GoLiveViewModel.cs`; add the Admin-only go-live entry to the navigation built in `src/Inmobiliaria.Presentation/Shell/ShellViewModel.cs` (Test 2 from S1 stays green and covers it)
- [x] 2.13 **[Desktop view]** `src/Inmobiliaria.Desktop/Views/GoLiveView.xaml` and `src/Inmobiliaria.Desktop/Views/GoLiveView.xaml.cs`; hook it into `src/Inmobiliaria.Desktop/MainWindow.xaml`. No logic in the view; checked by build
- [x] 2.14 **[Guardrails]** All previous tests green, including `ContractualFeePlanTests` and `CollectionDeskAtomicityTests`; no test placed in Desktop
- [x] 2.15 **[Handover]** File list and Conventional Commit message (for example `feat(go-live): admin-only cutover date and go-live setup screen`) to Tomás, together with H.1. No commit by the assistant

### Verification, PR 2 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; Advertencia(s) 0, Errores 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; **0 skipped**; passed = previous total + new tests |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, **no pending changes** (Test 40); snapshot file unchanged in `git diff` |

---

## Phase 3: Read ports (S3a, PR 3, est. about 450 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests in this PR: 8, 9 (flags), 21, 22, 23, 41
(reader half). Design anchors: D4, D17, D18, D19, D20, D26, sections 5.1 and 5.2.

- [ ] 3.1 **[Second inspection, read-only]** S2 landed intact and its migration file is in the folder; the private bag helpers in `src/Inmobiliaria.Infrastructure/Persistence/CollectionDesk.cs` (read-only here) are still where design D20 cites them. Docker up; record the baseline counts
- [ ] 3.2 **[Characterise before the move]** Run the full `CollectionDesk` test classes in `tests/Inmobiliaria.Infrastructure.Tests` and confirm green with 0 skipped; this is the safety net for the move, record the count
- [ ] 3.3 **[Move, no behaviour change (D20)]** Extract the recibo-bag reading helpers from `src/Inmobiliaria.Infrastructure/Persistence/CollectionDesk.cs` into `src/Inmobiliaria.Infrastructure/Persistence/ReciboRows.cs` (internal), and make the desk call it. The same desk tests stay green, **unmodified**; update the class comment that says no other file knows the bags
- [ ] 3.4 **[RED, Tests 8, 9 flags]** `tests/Inmobiliaria.Infrastructure.Tests/AccountDirectoryTests.cs` (Testcontainers, run as Admin and as Empleado through `RoleTestHelpers`): finds by tenant, unit, lessor and the contract's short id, ignoring case and accents, ended contracts included (8); readiness flags: missing rate, frozen account and unset starting number each make an account not ready and are named, a 0% rate and a missing cutover date do not (9). `tests/Inmobiliaria.Infrastructure.Tests/SearchFoldTests.cs` (no container): "Demaría" and "demaria" fold equal; 2-character minimum; 50-result cap
- [ ] 3.5 **[GREEN, directory]** `src/Inmobiliaria.Domain/Accounts/IAccountDirectory.cs` (port, `AccountListing`, `AccountReadiness`), `src/Inmobiliaria.Infrastructure/Persistence/AccountDirectory.cs` (no tracking, no accrual materialisation, in-memory fold search), `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCallAccountDirectory.cs`, registration in `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`; extend the Test 3 resolution in `tests/Inmobiliaria.Infrastructure.Tests/SessionServicesTests.cs`
- [ ] 3.6 **[RED, Tests 21, 22, 23, 41 reader half]** `tests/Inmobiliaria.Infrastructure.Tests/ReciboReaderTests.cs` (Testcontainers, as each role): either number finds the whole pair, an unknown number and a number outside 1 to `int.MaxValue` return nothing (the latter without a query) (21); the recent list (newest 50) shows both numbers, tenant, period, received and recorded dates and the annulled flag (21); an annulled recibo returns reason, user display name and time read at the moment of the call, and a recibo voided after an earlier read reads as annulled on the next (22); after the contract's rate or the tenant's name changes, the honorarios line, the total (sum of signed lines) and the header read as issued (23, 41)
- [ ] 3.7 **[GREEN, reader]** `src/Inmobiliaria.Domain/Collection/IReciboReader.cs` (port, `IssuedPayment`, `IssuedRecibo`, `AnnulmentRecord`, `RecentCollection`), `src/Inmobiliaria.Infrastructure/Persistence/ReciboReader.cs` (uses `ReciboRows`; zone conversion for `RecordedOn` and `AnnulledAt`; `annulled_by` joined to `app_users.username` with the role name as fallback), `src/Inmobiliaria.Infrastructure/Persistence/PerCall/PerCallReciboReader.cs`, registration in `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`; extend Test 3 again for `IReciboReader`
- [ ] 3.8 **[Guardrails]** `has-pending-model-changes` reports no changes; the Presentation guard still green; no change to `CollectionPlanner`, `VoidingPlanner`, numbering or ledger behaviour
- [ ] 3.9 **[Handover]** File list and Conventional Commit message (for example `feat(infrastructure): account directory and recibo reader ports`) to Tomás. No commit by the assistant

### Verification, PR 3 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; Advertencia(s) 0, Errores 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; **0 skipped**; the `CollectionDesk` tests keep their count from 3.2 |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 4: Cash desk (S3b, PR 4, est. about 600 lines)

**Docker: yes (full suite). Supabase: no. Model change: no.** All new tests are in
`Inmobiliaria.Presentation.Tests` with faked ports (fakes in `tests/Inmobiliaria.Presentation.Tests/Fakes/`).
Spec tests in this PR: 9 (display), 10 to 18, 13, 13a. Design anchors: D8, D9, D21, D22, D23, sections 7.1 to 7.4.
Over the 400-line heuristic because the picker, preview, summary, commit and stale diff are one state machine
(see the forecast note).

- [ ] 4.1 **[Second inspection, read-only]** S3a landed intact: `IAccountDirectory`, `IReciboReader`, their per-call wrappers and registrations exist; `AccountListing`, `IssuedPayment` and `CollectionPreview`/`CollectionRequest` still have the members design sections 5 and 7 use (`src/Inmobiliaria.Domain/Collection/ICollectionDesk.cs` (read-only), `src/Inmobiliaria.Domain/Collection/CollectionPlanner.cs` (read-only)). Docker up; record the baseline counts
- [ ] 4.1a **[From the S1 review, three advisory findings on `OperationRunner`]** (a) R3-cancelled-commit-silent: a cancelled **commit** must show the same "may or may not have been recorded" text as a failed one (only a cancelled read stays silent); (b) R3-isbusy-overlap: lock down overlapping runs with a test once the desk view-model uses latest-wins (D23); (c) R3-typed-values-test-vacuous: prove "typed values stay on screen" (Test 5) on the real desk view-model, replacing the vacuous runner test. RED then GREEN in `tests/Inmobiliaria.Presentation.Tests`
- [ ] 4.2 **[RED, Test 9 display]** `tests/Inmobiliaria.Presentation.Tests/AccountPickerViewModelTests.cs`: a listing shows tenant, units, contract status, short id and readiness; each missing gate is named with what to do; a not-ready account stays selectable; the missing cutover date shows as information only; search starts from 2 characters
- [ ] 4.3 **[GREEN, picker]** `src/Inmobiliaria.Presentation/CashDesk/AccountPickerViewModel.cs`, `src/Inmobiliaria.Presentation/CashDesk/ReadinessText.cs`
- [ ] 4.4 **[RED, Tests 10, 11, 12, 13, 13a, 14]** `tests/Inmobiliaria.Presentation.Tests/CashDeskPreviewTests.cs`: choosing an account requests a preview with handed amount 0 and changing period, received date or a closing line requests another (10); the received date is sent as `null` unless typed and shown from the preview, taken from the agency clock fake and not the machine's (11, D21); `AmountNotTotal` at the owed step shows figures and no error, every blocking refusal keeps the operator from the summary (12); an unset starting number (from readiness, D8, before any summary) shows the corrected text naming the starting recibo number and where an Admin sets it, never a contract's cutover date (13); a due unconfirmed adjustment warns naming each missing index and period, or says it is ready to confirm, never blocks, and none shows no warning (13a, D9, called once per account selection); the handed amount starts empty, the ledger figure and each recibo total carry different names, the ledger figure is accepted and the recibo total refused, a fractional seguro shows truncated (14); a result from an older input is dropped (latest-wins, D23)
- [ ] 4.5 **[GREEN, preview flow]** `src/Inmobiliaria.Presentation/CashDesk/CashDeskViewModel.cs` (state machine NoAccount, Owed, Ready; busy flag; cancellation and version), `src/Inmobiliaria.Presentation/CashDesk/AdjustmentWarning.cs`
- [ ] 4.6 **[RED, Tests 15, 16, 17, 18]** `tests/Inmobiliaria.Presentation.Tests/CashDeskCommitTests.cs`: commit unavailable until an amount is typed and a committable summary is shown, and withdrawn when any input changes (15); a successful commit shows both numbers with the locatario first and shows both recibos read through `IReciboReader` (16; the "nothing sent to a printer" half is asserted together with Test 20 in Phase 6, because `IReciboPrinter` does not exist yet); `PreviewStale` and `ConcurrentChange` each cause exactly one fresh preview, show what differs, clear the confirmation, keep the typed amount, and the fake desk's commit-call count stays at 1 (17); invoking commit twice before the first completes sends one commit, and commands return after a failure (18). `tests/Inmobiliaria.Presentation.Tests/PreviewDifferenceTests.cs`: period, received date, previous amount owed, ledger figure, resulting amount owed and each recibo line changed, appeared or disappeared, matched by copy and kind
- [ ] 4.7 **[GREEN, summary and commit]** `src/Inmobiliaria.Presentation/CashDesk/PreviewDifference.cs` and the Summary, Committing, Issued, Refused, Stale and Failed states in `src/Inmobiliaria.Presentation/CashDesk/CashDeskViewModel.cs`; the summary holds the exact `(CollectionPreview, CollectionRequest)` pair that built it
- [ ] 4.8 **[RED then GREEN, recibo display]** `tests/Inmobiliaria.Presentation.Tests/ReciboViewModelTests.cs` then `src/Inmobiliaria.Presentation/Recibos/ReciboViewModel.cs`: both recibos shown as issued (number, copy, tenant, lessors, units, period, received date, each line with Spanish kind label and signed amount in stored order, total, legend); an annulled payment also shows number, reason, user and time with lines still readable; no Print action yet
- [ ] 4.9 **[Desktop views]** `src/Inmobiliaria.Desktop/Views/AccountPickerView.xaml`, `src/Inmobiliaria.Desktop/Views/CashDeskView.xaml`, `src/Inmobiliaria.Desktop/Views/ReciboView.xaml` (with code-behind files) and navigation hookup in `src/Inmobiliaria.Desktop/MainWindow.xaml`; the desk entry is disabled with the named setting when the recargo terms are invalid (D16). No logic in views; checked by build
- [ ] 4.10 **[Guardrails]** Presentation guard green; `CollectionPlanner` and `VoidingPlanner` untouched; no test in Desktop
- [ ] 4.11 **[Handover]** File list and Conventional Commit message (for example `feat(cash-desk): account picker, preview, summary and commit flow`) to Tomás. No commit by the assistant

### Verification, PR 4 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; Advertencia(s) 0, Errores 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; **0 skipped**; passed = previous total + new tests |

---

## Phase 5: Recent list and voiding (S4a, PR 5, est. about 400 lines)

**Docker: yes (full suite). Supabase: no. Model change: no.** Spec tests in this PR: 19, 25, 26. Design
anchors: D19, section 8.

- [ ] 5.1 **[Second inspection, read-only]** S3b landed intact; `ReciboViewModel` and the desk exist; `src/Inmobiliaria.Domain/Collection/VoidingPlanner.cs` (read-only) and `src/Inmobiliaria.Domain/Collection/CollectionRules.cs` (read-only) still hold the age windows (7 days both roles, 8 to 30 Admin only, 31 none) the pre-gate must mirror. Docker up; record the baseline counts
- [ ] 5.2 **[RED, Tests 25, 26 pre-gate]** `tests/Inmobiliaria.Presentation.Tests/VoidingGateTests.cs`: day 0 to 7 both roles, day 8 to 30 Admin only, day 31 and over nobody, annulled nobody, each unavailable action with its explanation reusing the domain's own texts (26)
- [ ] 5.3 **[GREEN, gate]** `src/Inmobiliaria.Presentation/Voiding/VoidingGate.cs`
- [ ] 5.4 **[RED, Tests 19, 25, 26 database refusal]** `tests/Inmobiliaria.Presentation.Tests/VoidingViewModelTests.cs`: found by the number of either recibo and from the recent list, with the same preview offered; a blank reason shows the `ReasonRequired` text and the port is not called; the preview shows corrections, resulting amount owed and both recibos to be annulled (25); a stale voiding re-previews once and requires a new confirmation, never repeating the void (19); a pre-gate that allowed an action the database then refuses shows the refusal's operator text (26); after a voiding the payment is re-read and shown annulled; nothing prints. `tests/Inmobiliaria.Presentation.Tests/RecentCollectionsViewModelTests.cs`: shows the newest 50 with both numbers, tenant, period, received and recorded dates and the annulled flag
- [ ] 5.5 **[GREEN, view-models]** `src/Inmobiliaria.Presentation/Voiding/VoidingViewModel.cs`, `src/Inmobiliaria.Presentation/Recibos/RecentCollectionsViewModel.cs`; entries offered to both roles in `src/Inmobiliaria.Presentation/Shell/ShellViewModel.cs` (Test 2 stays green)
- [ ] 5.6 **[Desktop views]** `src/Inmobiliaria.Desktop/Views/RecentCollectionsView.xaml`, `src/Inmobiliaria.Desktop/Views/VoidingView.xaml` (with code-behind files) and hookup in `src/Inmobiliaria.Desktop/MainWindow.xaml`; when recargo terms are invalid the voiding entry is disabled too (D16). Checked by build
- [ ] 5.7 **[Guardrails]** Presentation guard green; no change to `VoidingPlanner`
- [ ] 5.8 **[Handover]** File list and Conventional Commit message (for example `feat(cash-desk): recent collections and voiding screen`) to Tomás. No commit by the assistant

### Verification, PR 5 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; Advertencia(s) 0, Errores 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; **0 skipped**; passed = previous total + new tests |

---

## Phase 6: Printing (S4b, PR 6, est. about 500 lines)

**Docker: yes (full suite). Supabase: no. Model change: no.** Spec tests in this PR: 20, 24, 24a, 24b, 24c,
24d, 41 (print model half), 16 (nothing printed half), and by hand M1 to M3. Design anchors: D10 to D15, section 9
and 10. **Precondition before 6.2 starts: H.3 (agency letterhead values and logo) is confirmed by Tomás.**
Nothing is invented: tests use obviously fake fixture values, only `appsettings.json` carries the real ones.

- [ ] 6.1 **[Second inspection, read-only]** S4a landed intact; `ReciboViewModel` and `IReciboReader` exist; `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj` (read-only) copies `appsettings.json` to the output so the logo can be copied the same way; `src/Inmobiliaria.Domain/Collection/Recibo.cs` (read-only) still carries the stored legend. Docker up; record the baseline counts
- [ ] 6.2 **[RED then GREEN, Test 24b]** `tests/Inmobiliaria.Presentation.Tests/AmountInWordsTests.cs` then `src/Inmobiliaria.Presentation/Printing/AmountInWords.cs`: 47,860, 507,000, 598,260, 21,000 "veintiún mil", 31,001 "treinta y un mil uno", 1,000,000 "un millón", 100 "cien", 101 "ciento uno", 0 "cero", the suffix always "con 00/100.-", a negative prefixed "menos", up to 999,999,999,999
- [ ] 6.3 **[RED, Tests 24, 24a, 41 print model half]** `tests/Inmobiliaria.Presentation.Tests/ReciboSheetBuilderTests.cs` and `tests/Inmobiliaria.Presentation.Tests/PaperRowsTests.cs`: ORIGINAL and DUPLICADO identical except the caption; legend on both halves; received date; the pair's two sheets never share a line; two builds of a live recibo are equal and carry no reprint mark; annulled shows ANULADO, date and reason on both halves and a live one none (24); modelled rows print 0 when absent, unmodelled rows print the label with an empty amount, Seguro and the contractual instalment print on an added row, Importe Neto equals the recibo's total and the builder asserts it equals the sum printed (24a); the sheet equals the stored recibo with no recomputation (41)
- [ ] 6.4 **[GREEN, print model]** `src/Inmobiliaria.Presentation/Printing/PaperRows.cs`, `src/Inmobiliaria.Presentation/Printing/ReciboSheet.cs`, `src/Inmobiliaria.Presentation/Printing/ReciboSheetBuilder.cs`
- [ ] 6.5 **[RED then GREEN, Test 24c]** `tests/Inmobiliaria.Presentation.Tests/AgencyDetailsTests.cs` then `src/Inmobiliaria.Presentation/Printing/AgencyDetails.cs`: each missing configured item (name, address, phone, email, tax status, rental legend, broker name, broker title, licence number, logo file) stops the print and is named; nothing is invented
- [ ] 6.6 **[RED then GREEN, Test 24d]** `tests/Inmobiliaria.Presentation.Tests/SheetArrangementTests.cs` then `src/Inmobiliaria.Presentation/Printing/SheetArrangement.cs`: A4 landscape gives two-up; portrait, or a half under 120 mm, or a half that does not fit one page gives one half per page, ORIGINAL first (D11). The real output is M1
- [ ] 6.7 **[RED, Tests 20, 16 nothing-printed half]** `tests/Inmobiliaria.Presentation.Tests/ReciboPrintingTests.cs` with a fake `IReciboPrinter`: no printer call after a commit or after a voiding; Print and "print both recibos of the payment" are available whenever a recibo is shown; each Print re-reads the payment through `IReciboReader` first, so a payment voided while the screen was open prints ANULADO; cancelling the dialog (`PrintOutcome.Cancelled`) changes nothing and calls no database port
- [ ] 6.8 **[GREEN, printing seam and actions]** `src/Inmobiliaria.Presentation/Printing/IReciboPrinter.cs` (`PrintAsync`, `PrintOutcome`), Print commands in `src/Inmobiliaria.Presentation/Recibos/ReciboViewModel.cs`
- [ ] 6.9 **[Desktop printer and configuration]** `src/Inmobiliaria.Desktop/Printing/FlowDocumentReciboPrinter.cs` (`PrintDialog`, A4 landscape ticket, two-up columns with `BreakColumnBefore` or one column with `BreakPageBefore`, content wraps inside table cells); `src/Inmobiliaria.Desktop/appsettings.json` gains the `Agency` section with the values confirmed in H.3 (none contains `Password`, `ConnectionString` or `Pwd=`); `src/Inmobiliaria.Desktop/Assets/agency-logo.png` (supplied by Tomás) and its copy-to-output item in `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj`; compose `AgencyDetails` and the printer in `src/Inmobiliaria.Desktop/App.xaml.cs`; add the Print buttons to `src/Inmobiliaria.Desktop/Views/ReciboView.xaml`. A missing logo file stops before the dialog with a message naming it. Checked by build, then by hand in 6.10
- [ ] 6.10 **[Guardrails]** `CompositionGuardTests` credential-shape guard green on the new `Agency` keys; Presentation guard green; no test in Desktop
- [ ] 6.11 **[Handover]** File list and Conventional Commit message (for example `feat(receipt): print the recibo on A4 with agency letterhead`) to Tomás, together with H.4 (M1 to M3). No commit by the assistant

### Verification, PR 6 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; Advertencia(s) 0, Errores 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; **0 skipped**; passed = previous total + new tests |
| Manual M1 to M3 (H.4) | Tomás's confirmation, recorded beside H.4 |

---

## Human Follow-Ups (Tomás)

| # | When | What | Who |
|---|---|---|---|
| H.1 | After PR 2 merges | **Apply the `HardenCutoverDateToAdmin` migration to Supabase.** Restore the project first (free plan pauses after about a week). Only Tomás connects; the assistant never handles the password. If it is not applied, the app still works but an Empleado is not blocked at the database from changing the cutover date | Tomás |
| H.2 | Before each slice | Docker running (all six slices touch `Inmobiliaria.Infrastructure.Tests`). The assistant announces it at the start of the session | Tomás |
| H.3 | **Before Phase 6 starts** | Confirm the agency's letterhead values (name, address, phone, email, tax status, rental legend, broker name, broker title, licence number) and supply the logo file. The values exist in Engram observation #135 (business data only, no personal data); the assistant proposes them from there for your confirmation and does not invent any | Tomás |
| H.4 | After PR 6 | Manual print checks on a real printer or Microsoft Print to PDF: **M1** A4 landscape is ORIGINAL left and DUPLICADO right on one page, A4 portrait and Carta reflow with nothing clipped; **M2** a voided pair reprints with ANULADO, date and reason on both halves, numbers still readable; **M3** a commit and a voiding open no print dialog by themselves. Report any layout correction; the 120 mm and 9 pt values in design D11 stay provisional until M1 | Tomás |
| H.5 | After PR 4 (optional) and PR 6 | A live smoke against Supabase once it is awake: log in, find an account, preview, commit, find the recibo by number, void. Outside CI; not required for the PRs to merge | Tomás |

## Issues found (while slicing)

1. **`Inmobiliaria.Core.slnf` must be edited in S1.** It currently lists four projects and Presentation and Presentation.Tests are not among them; without task 1.2 the new tests would not gate the Linux job. Design D1 already says so; the task makes it explicit.
2. **Test 16 and Test 20 are split across slices.** Test 16's "sends nothing to the printer" half cannot be asserted in S3b because `IReciboPrinter` is introduced in S4b. Task 4.6 proves the numbers and recibo display; task 6.7 proves the nothing-printed half together with Test 20. Design section 13 maps 16 only to S3b.
3. **Test 3 grows across slices.** S1 registers two ports, S2 adds `IGoLiveSetup`, S3a adds `IAccountDirectory` and `IReciboReader`. Each slice extends the same `SessionServicesTests` rather than leaving the resolution test proving fewer ports than the app uses (tasks 2.10, 3.5, 3.7).
4. **Test 7 reads Desktop's `appsettings.json` from Infrastructure.Tests without a project reference.** It uses the repo-root walk `CompositionGuardTests` already uses; in S1 the shipped file carries only `Recargo`, and the `Agency` section arrives in S4b (task 6.9).
5. **Tests 35 to 38 are partly characterisation.** 36, 37 and 38 pin behaviour the unmigrated database already has, so they pass at once; only 35 and 39 can be RED. The guard is then proved by inverting the role check (task 2.7), not by a RED run.

## Deviations (recorded beside tasks during apply)

**Phase 1 (S1), applied 2026-10-22.**

- **1.1 baseline** (Docker up, `dotnet test Inmobiliaria.Core.slnf -c Release`): 598 passed, 0 failed, 0 skipped (Domain.Tests 365, Infrastructure.Tests 233). The anchors in the task all matched the code.
- **1.3 Presentation guard.** The guard loads the assembly by name (`Assembly.Load("Inmobiliaria.Presentation")`) instead of `typeof(SomeType).Assembly`, so it exists before any Presentation type does. Limitation, accepted: `GetReferencedAssemblies()` lists only assemblies the compiled IL actually uses, so a project reference nobody uses is invisible to it (and harmless). **Proved by making it fail:** added a temporary Infrastructure project reference plus one type usage in Presentation; the test went red with `Collection: ["Inmobiliaria.Infrastructure"]`; both removed, test green again.
- **1.4 and 1.6 and 1.8 and 1.10 RED** were observed as compile errors for the missing types (`OperationRunner`, `Shell` namespace, `RecargoSettings`, `AddSessionServices`), the right reason.
- **1.5 `OperationRunner` shape (design left it open).** `RunAsync<T>(Func<Task<T>>, OperationKind = Read)` returns `OperationOutcome<T>(Value, Completed, ErrorMessage)` and also exposes `IsBusy` and `ErrorMessage` for binding. `OperationKind.Commit` selects `ErrorMessages.CommitFailed`. An `OperationCanceledException` is neither an error nor a completion (no message), so the latest-wins cancellation of D23 has a home in S3b. Extra tests beyond the task text: cancellation, a failed read does not claim a payment may have been recorded.
- **1.7 `ShellViewModel` takes `(IUserSession, IReadOnlyList<string> settingsProblems)` only.** The task says "from the session, ports, clock and settings problems"; no S1 screen consumes a port or the clock, so they join the constructor in the slice that first uses them. `NavigationTarget` declares all four entries now (Test 2 proven once). Desk and voiding entries are present but `IsAvailable = false` with the problem text when the recargo terms are invalid (D16); an extra test covers it. `NavigationTitles` (in `NavigationEntry.cs`) holds the Spanish screen names so the commit-failure text can point to "Cobros recientes" by constant.
- **1.9 `RecargoSettings` shape.** `Terms`, `Problem`, `IsValid`, `Read(JsonElement)`; one problem, the rate checked first; problem texts in Spanish, naming `Recargo:DailyRate` or `Recargo:GraceDays`. Non-integer grace days (1.5) are a problem too.
- **1.11 wrappers are `internal`** (`PerCall`, `PerCallCollectionDesk`, `PerCallDueAdjustmentQuery`); the tests reach them only through the session provider. The container registers the session, `IAgencyClock`, `IDueAdjustmentQuery` and (valid terms only) `ICollectionDesk`; it never registers the factory or a context. Extra tests: invalid terms leave the desk unregistered; the desk wrapper also gets one disposed context per call. The disposal assertion was itself proved: with `await using` removed from `PerCall.RunAsync`, the two Test 4 tests went red.
- **1.12 proved by making it fail:** with `services.AddSingleton<InmobiliariaDbContext>(_ => factory.Create())` added to `AddSessionServices`, Test 3's container guard went red with `Service type ...InmobiliariaDbContext is a DbContext.`; removed, 6/6 green.
- **1.13** `App.xaml.cs`, `MainWindow.xaml` and `MainWindow.xaml.cs` checked by build only (0 warnings, 0 errors); the live smoke is H.5. The `Recargo` section was added to `appsettings.json` only (no `Agency`). The `MainWindow` files were rewritten whole, so their diff is the whole file.
- **1.14** `ArchitectureGuardTests` and `CompositionGuardTests` green inside the full run; `dotnet ef migrations has-pending-model-changes`: exit 0, "No changes have been made to the model since the last migration."
- **Size.** About 1,080 authored lines (971 in new files, about 142 added or changed in existing files, minus 30 generated `.sln` lines) against the 450 estimate, about 2.4x; roughly 560 of them are tests. Reported to the orchestrator, not cut.
- **Result.** Full suite after S1: 631 passed (Domain 365, Presentation 15, Infrastructure 251), 0 failed, 0 skipped; baseline 598 plus 33 new.
- **Native review (gentle-ai, 2026-10-22).** RDD on; risk medium (25 files, 1,158 lines); Tomás granted consent. One lens (reliability) ran and the review was **approved** and acknowledged (lineage `review-5cfb15f548ce292f`, authority burned). Four advisory, non-blocking findings were recorded as later work: task 2.1a (bootstrap backstop) and task 4.1a (three on `OperationRunner`). Running it needed `gentle-ai sync --agent claude-code` first (managed assets outdated), authorized by Tomás mid-change.
- **One PR, not split.** S1 ships as a single PR despite the size: the native review already covered the whole candidate, and the code cannot be split without a build-verified intermediate state that adds work and no review value.

**Phase 2 (S2), applied 2026-10-23.**

- **2.1 baseline** (Docker 29.9.2 up, `dotnet test Inmobiliaria.Core.slnf -c Release`): 631 passed, 0 failed, 0 skipped (Presentation 15, Domain 365, Infrastructure 251); build 0 warnings, 0 errors. S1 landed intact (`4243e74`, #71). The anchors all matched: `SetCutoverDate(cutoverDate, account)` with no `today`, the function body at `20261014204601_...:153-169`, the three calls at `ContractualFeePlanTests.cs:212`, `:228`, `:240`.
- **2.1a** `App.xaml.cs`: a `_shellShown` flag set after `mainWindow.Show()`; the backstop shuts down when it is false. Build only; behaviour in H.5.
- **2.2** `RoleTestHelpers` is `internal static`; `RolePermissionTests` uses it through `using static`, so its call sites are unchanged. 10 passed before and after.
- **2.3 RED** observed as compile errors (`CutoverRefusal`, `CheckCutoverDate`, the `today` parameter), then the text test failed alone on the old wording before 2.4 changed it.
- **2.4** `SetCutoverDate` now calls `CheckCutoverDate` and throws per refusal (movement as `InvalidOperationException`, bounds as `ArgumentOutOfRangeException`). New `CutoverNotSet` text: "El número inicial de recibo no está cargado. Un Administrador debe cargarlo en Puesta en marcha antes de cobrar." Domain 373 passed.
- **2.5 deviation from Issues found 5.** On the unmigrated database 36 and 37 passed, and **35, 38 and 39 went red**. Test 38 cannot pass before the migration: its second half ("then cannot change it") is exactly the new Admin-only rule. Test 39 rebuilds the previous body from its own migration (roll back to `AddContractAccounts`, re-apply to `AddCollectionsAndRecibos`) instead of copying the text into the test.
- **2.6** migration `20261023131755_HardenCutoverDateToAdmin`, generated on the unchanged model; snapshot unchanged (`git diff --quiet` exit 0). The trigger was not recreated.
- **2.7 proved by making it fail:** with `IF NOT pg_has_role(` changed to `IF pg_has_role(`, Test 35 went red with `P0001: Only an Administrator can change the cutover date ...` for the Admin; restored, green.
- **2.8** `has-pending-model-changes`: exit 0.
- **2.9 and 2.10.** RED as the missing `GoLiveSetup` type. The adapter reuses `CollectionDesk.DescribeParty`, `DescribeUnit` and `NameSeparator` (made `internal`; `DescribeParty` extracted from the header code, no behaviour change) so the list names tenants and units as the recibo does. A starting number below 1 is refused before `ReciboNumbering` (which would throw). `SetupErrorMap` is one table per statement. Extra tests beyond the task: an Empleado setting a cutover date is `CutoverAdminOnly`; an Empleado setting the number is `StartingNumberAdminOnly`; the go-live wrapper gets one disposed context per call; invalid recargo terms still register `IGoLiveSetup` (D16).
- **2.11 and 2.12.** RED as the missing `Presentation.GoLive` namespace. The go-live navigation entry already existed from S1 (1.7), so 2.12 adds nothing to `ShellViewModel`. Refusals before the port use the `StartingNumberOutOfRange` text. The screen re-reads after a completed set (refused or not), not after an exception, so the generic message stays visible.
- **2.13** `MainWindow` shows `GoLiveView` through a `DataTrigger` on the selected navigation entry (no code-behind decides it); `App.xaml.cs` builds `GoLiveViewModel` for an Admin only. `GoLiveView` reloads when it becomes visible. Build only.
- **2.14 result.** Build 0 warnings, 0 errors. Full suite: 683 passed (Presentation 33, Domain 373, Infrastructure 277), 0 failed, 0 skipped; baseline 631 plus 52 new. `has-pending-model-changes` exit 0. No test in Desktop.
- **Size.** About 1,920 authored lines (about 1,715 in new files, of which about 1,040 are tests, plus 144 added and 65 removed in existing files; the 1,203-line generated `.Designer.cs` is not counted) against the 550 estimate, about 3.5x. Reported, not cut (see the handover).
- **Native review (gentle-ai, 2026-10-23).** RDD on; risk escalated by the assistant to item 2 (the database role guard); Tomás granted consent. Two lenses (risk, reliability) ran; **approved** and acknowledged (lineage `review-9842eae1aad11c9c`, authority burned). Risk found no issue. Three non-blocking reliability suggestions, left as later work: `R3-golive-list-culture-order` (`GoLiveSetup.cs:63` orders tenants by current culture), `R3-golive-tests-undisposed-contexts` (`GoLiveSetupTests.cs:31`, contexts made by `SetupOver(world.NewContext())` are disposed with the world, not per call), `R3-golive-visible-reload-skipped-when-busy` (`GoLiveView.xaml.cs:19`, the reload on becoming visible is skipped if an operation is running).
- **One PR, not split**, as in S1: the review covered the whole candidate.
