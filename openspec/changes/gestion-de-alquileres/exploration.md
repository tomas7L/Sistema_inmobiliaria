# Exploration: gestion-de-alquileres (the missing rental screens)

Done 2026-11-04, read-only. Verified by reading code unless marked **[inferred]**. A parent spot check confirmed that no
production code creates these records or calls `ConfirmAdjustment`, and that no migration seeds an index.

Paths are relative to the repository root, with `Domain` = `src/Inmobiliaria.Domain/` and
`Infra` = `src/Inmobiliaria.Infrastructure/Persistence/`.

## Finding

No screen exists for parties, units, contracts, indexes or adjustments. Navigation has five targets
(`src/Inmobiliaria.Presentation/Shell/NavigationEntry.cs:4-11`). The write logic behind those screens is missing too.

Common to all five areas:

- No create, edit, list or search port exists, apart from the reads listed below.
- Both roles hold INSERT and UPDATE, with no DELETE (`Migrations/20260924202549_AddUsersAndRoles.cs:157-162`).
- No domain mutator below has a non-test caller.
- No refusal map for unique violations exists for these tables, comparable to `SetupErrorMap` (`GoLiveSetup.cs:161`).

## By area

| Area | Exists | Missing |
|---|---|---|
| Parties | `Parties/Party.cs:19`; `UpdateContactInfo` `:42`; unique DNI and CUIL (`PartyConfiguration.cs:16-24`) | Create, edit, list, search; correcting name, DNI or CUIL |
| Units | `Units/PropertyUnit.cs:8`, `ParkingUnit.cs:8`; `Shared/Address.cs:17`; `Unit.Relocate` `Unit.cs:38` | Create, edit, list; derived availability (`unit-registry` requirement has no implementation) |
| Contracts | `Leasing/Contract.cs:119`; `AssignParty` `:337`; `SetUnitShares` `:359`; `AttachAdjustmentClause` `:283`; `GiveNotice` `:414`; `End` `:430`; read-only `IGoLiveSetup.ListContractsAsync`, `IAccountDirectory.SearchAsync` | Create, edit, assign parties, notice, end, create account, detail. `new ContractAccount` exists in `src` only as an in-memory replay (`VoidingPlanner.cs:161`) |
| Indexes | `Indices/EconomicIndex.cs:16`; `IndexValue.cs:16`; `Correct` `:32`; `IndexResolver.cs:18`; unique name and (index, period) | Everything on a screen; catalog seed: IPC, RIPTE and ICL are not created by any migration |
| Adjustments | `AdjustmentClause.cs:39`; `AdjustmentProposal.cs:31`; `Contract.ConfirmAdjustment` `:315`; `AdjustmentSchedule.cs:12`; `IDueAdjustmentQuery` | Confirm, record clause, summary, history, correction. The only running-app caller is `CashDeskViewModel.cs:232`, a warning |

## Getting a contract into the database

No path works without direct database access. The repository has no SQL script, importer or seed for this. Go-live sets only
the cutover date and starting number, and lists a contract without an account as not settable (`GoLiveSetup.cs:77-80`).
**[inferred]** A contract without an account is invisible to the desk (`AccountDirectory.cs:35`).

## Gaps between spec and code

1. **Multiplicity half enforced.** "1..N lessors, exactly 1 tenant": only a second tenant is refused (`Contract.cs:347`).
   Nothing requires a tenant or a lessor (`ContractPartyConfiguration.cs:21`).
2. **Cutover on insert.** `access-control` reserves it to Admin, but only UPDATE is hardened; an Empleado can INSERT a contract
   carrying one (`20261023131755_HardenCutoverDateToAdmin.cs:27-28`, a named limit).
3. **Honorarios rate is write-once.** The desk requires it (`AccountDirectory.cs:83`); only the constructor sets it.
4. **Summary data missing.** The spec requires index values and periods, but `DueAdjustment` omits levels (`DueAdjustment.cs:27`),
   and the query discards the `AdjustmentProposal` it builds (`DueAdjustmentQuery.cs:86-88`).
5. **Effective date carries two meanings.** The query uses the due date, `NextDueDate` derives from it, and `CanonFor` prices any
   unmaterialised period by it (`AccrualSchedule.cs:208`). The late test uses a post-confirmation date
   (`ContractAdjustmentLateConfirmationTests.cs:26`). **[inferred]** The due date would charge unaccrued months retroactively; a
   later date would drift the next due date.
6. **Corrections unimplemented.** "A Correction Produces a New Adjustment" exists only as parameters.
7. **PendingTermination excluded** from the worklist (`DueAdjustmentQuery.cs:31`); the spec does not address it.
8. **[inferred]** `ChangeMonthlyRent` on a contract with no adjustment history would reprice unmaterialised past periods.

## Dependency order

FKs and constructors verified; the sequence is **[inferred]**:

1. Units, because the contract constructor needs unit shares.
2. Parties, then `AssignParty`.
3. Contract, then `ContractAccount`.
4. For a pre-existing contract: cutover date, which needs the account, then opening balances. The date must come first or the
   first read materialises history (`Contract.cs:63-65`).
5. Index catalog, then clause.
6. Index values, then due adjustment, then confirmation.

## Conventions to reuse: the Users screen

- `Presentation/Users/UsersViewModel.cs:24`, port in `Domain/Access/IUserAdministration.cs:9`, refusals as values.
- `SessionServices.cs:40`, per-call wrappers in `Infra/PerCall/`, wired at `App.xaml.cs:148-150,183` and
  `MainWindow.xaml.cs:18-36`, `MainWindow.xaml:135-141`.
- `OperationRunner.cs:68` for errors; `UsersView.xaml:45-57` for refusal text and the confirm band.
- Tests: `tests/Inmobiliaria.Presentation.Tests/UsersViewModelTests.cs`, `Fakes/FakeUserAdministration.cs`,
  `ShellViewModelTests.cs:37-60`, `tests/Inmobiliaria.Infrastructure.Tests/UserAdministrationTests.cs`, `SessionServicesTests.cs`,
  `RolePermissionTests.cs`.
- **Do not copy the Admin gating** (`ShellViewModel.cs:48-52`): these screens are Empleado work too
  (`access-control/spec.md:22,29`).

## What the owner said

Source: `openspec/domain/respuestas-del-dueno.md`.

- `:197-198` *"nosotros a mano cargaremos todos los datos de cada persona."*
- `:200-202` *"Lo llevan en un excel con la deuda de cada inquilino"*
- `:205` "No automated migration and no import from the old system." The team's reading, and it conflicts with
  Engram `decisions/carga-inicial-own-change`.
- `:217` the spreadsheet is per tenant; the account is per contract.
- `:146-147` *"Generalmente está utilizando casi siempre ICL."*

## Could not be determined

- The contents of the Excel.
- How the test database's fictitious contract was seeded.
