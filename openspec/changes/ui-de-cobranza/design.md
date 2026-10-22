# Design: ui-de-cobranza (the cash desk, the recibo on paper, and go-live setup)

Technical design for the `ui-de-cobranza` change, from `spec.md` (merged in PR #68, Decisions P1 to 39,
Tests 1 to 41 and M1 to M3) and `proposal.md` in this folder. Written 2026-10-22. Nothing here is code;
nothing here edits the spec. Every type, member, column and file named as existing was read in the code
and is cited `path:line`; anything new is marked **(new)**.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **weak** and **medium** rows are where you can change my mind |
| **§12 Spec claims checked against the code** | Five places where the spec, the proposal or the code say something different. Rows C1 and C2 change what the screen does |
| **§13 Slices** | The PR plan: six slices instead of four. S2 is optional to split |
| **§15 Questions for Tomás** | Two, ranked. Both answered on 2026-10-22 |

**Skip** §3 to §11 unless you want the detail: they are written for whoever implements and for the
second inspection at the start of each session. **Skim** §1 and §2 if you want the shape in one picture.

---

## Decisions

**VERIFIED FACT** = read in the code, cited. **SPEC-derived** = the spec decided it; the design gives it a
shape. **DESIGN CALL** = decided here. Strength: **strong**, **medium**, **weak** (act on it only
provisionally).

| # | Decision | Basis | Strength |
|---|---|---|---|
| D1 | **Approach B is confirmed.** A new non-WPF `src/Inmobiliaria.Presentation` (net10.0, CommunityToolkit.Mvvm, references **Domain only**) holds every view-model and every pure presentation function (refusal mapping, stale diff, voiding pre-gate, print model, amount in words, paper rule). `tests/Inmobiliaria.Presentation.Tests` tests them with faked ports. Both join `Inmobiliaria.sln` and `Inmobiliaria.Core.slnf`, so they run on the Linux `core` job. `Inmobiliaria.Desktop` keeps XAML, the WPF printer and composition only. Fallback C is not needed. | VERIFIED: `Core.slnf:4-9` excludes Desktop; `ci.yml:36-60` runs tests only from the slnf; `Directory.Build.props:4` already targets net10.0; CommunityToolkit.Mvvm is centrally pinned (`Directory.Packages.props:23`) and is not WPF-bound | **Strong** (was medium, 65%, in the proposal; nothing found against it) |
| D2 | **One database context per port call, by per-call adapters.** Each port the screens use is registered as a small Infrastructure wrapper that holds the session factory, and every method does `factory.Create()`, runs the real adapter, and disposes the context. The container never holds a `DbContext`. A preview is one call; a commit is one call (it re-previews inside its own transaction). | DESIGN CALL; VERIFIED: `ISessionDbContextFactory.Create()` (`ISessionDbContextFactory.cs:12`); `CollectionDesk` takes a context in its constructor (`CollectionDesk.cs:44`) | **Strong** |
| D3 | **`AddSessionServices` lives in Infrastructure beside `AddPreLoginServices`**, and its guard test sits beside `CompositionGuardTests`. It registers ports and the clock; view-models are built by Desktop from resolved ports, not resolved from the container. | VERIFIED precedent (`ApplicationServices.cs:15-27`, `CompositionGuardTests.cs:66-98`) | **Strong** |
| D4 | **Three new Domain ports**: `IAccountDirectory` (Accounts), `IReciboReader` (Collection), `IGoLiveSetup` (new `GoLive` folder). Adapters in `Infrastructure/Persistence`. The reader returns a read model, `IssuedPayment`, that carries the recibo ids, the recorded date and the annulment's reason, user and time, because the domain `Recibo` cannot (Finding F3). | SPEC-derived (P8, F3); VERIFIED: `Recibo.cs:9-58` has no id or annulment; `PreviewVoidAsync` needs the recibo **id** (`ICollectionDesk.cs:57-58`) | **Strong** |
| D5 | **Cutover value rule: `contract.StartDate <= cutover <= today`**, with `today` passed as an argument (no clock in Domain). A new pure `Contract.CheckCutoverDate` returns a `CutoverRefusal?`; `SetCutoverDate` gains a required `today` parameter and still throws on refusal. No first-of-month rule; the lower bound is the spec's, not refined. | SPEC-derived (Decision 22); VERIFIED: `Contract.cs:199-210` has no bound; `CollectionPlanner.Plan` takes `today` as an argument (`CollectionDesk.cs:359`) | **Medium** on the rule; **strong** on today-as-argument |
| D6 | **The hand-written migration replaces only the body of `reject_cutover_change_after_movement()`**: the existing lock first, then a `pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER')` check raised as `P0001` / `contracts_cutover_admin_only`. The trigger itself (`BEFORE UPDATE OF cutover_date`) is not recreated. `Down` restores the original body verbatim. No EF model change; proved by `has-pending-model-changes` (§6). | VERIFIED: function and trigger at `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:153-173`; precedent for `pg_has_role` and the name pattern at `20261015204506_AddCollectionsAndRecibos.cs:336-338` | **Strong** |
| D7 | **The `CutoverNotSet` text is fixed in Domain**, not mapped by the screen: `CollectionPlanner.cs:81-82` and the doc comment at `:46` are rewritten to name the starting recibo number and where an Admin sets it. The enum member keeps its name. | DESIGN CALL; VERIFIED: the only test of it asserts the enum, not the text (`CollectionDeskAtomicityTests.cs:124`) | **Medium-strong** |
| D8 | **The starting number is checked before the summary, not only at the commit.** The domain preview never returns `CutoverNotSet` (only the commit does), so the desk reads the starting-number state from the account listing's readiness and refuses to reach the summary while it is unset. | VERIFIED: `PreviewAsync` never reads the counter (`CollectionDesk.cs:55-64`); the commit does (`:120-123`). See §12 C1 | **Strong** |
| D9 | **Pending-adjustment warning: reuse `IDueAdjustmentQuery.GetDueAsync(today)` unchanged, filtered by `ContractId` in the view-model, called once per account selection** (not per preview). No by-contract method now. | DESIGN CALL; VERIFIED cost: four queries, all clauses and all index values (`DueAdjustmentQuery.cs:27-54`); the agency has two users and a few hundred contracts at most | **Medium** (revisit only if verify measures it slow) |
| D10 | **Printing**: Presentation builds a pure print model (`ReciboSheet`: two identical halves, fixed paper rows, amount in words, optional ANULADO block); Desktop renders it as one WPF `FlowDocument` and prints it through `PrintDialog`. The application asks for A4 landscape; **two-up** uses two columns with `BreakColumnBefore` on the DUPLICADO half; **one-per-page** uses one column with `BreakPageBefore`. | SPEC-derived (P2, P14, Decisions 15-19) | **Strong** on FlowDocument/PrintDialog; **medium** on the column technique |
| D11 | **Paper rule: two-up when the printable area is landscape, a half is at least 120 mm wide, and one half paginates onto a single page at that width; otherwise one half per page, ORIGINAL first.** Type is fixed at 9 pt body, 8 pt footer, never scaled. | DESIGN CALL (Decision 19 leaves both numbers to design). A4 landscape gives halves of about 140 mm; A5 landscape about 100 mm, so it falls back | **Medium** on the rule; **weak** on 120 mm and 9 pt until M1 |
| D12 | **Fixed paper rows are data, not code paths**: one ordered table of (label, kind-or-none) in Presentation. Modelled rows print 0 when absent; unmodelled rows print the label with an empty amount; a stored line whose kind has no row (Seguro, the contractual instalment) prints on an added row after Otros Conceptos. Importe Neto is the stored total, and the builder asserts it equals the sum of what it printed. | SPEC-derived (Decisions 34, 35); config `rules.design` "catalog + lines, not fixed columns" (`openspec/config.yaml:226-228`) | **Strong** |
| D13 | **ANULADO is a banner block at the top of each half (word, date, reason), plus a diagonal watermark as the half's background.** It is read from the database at print time: Print re-reads the payment through `IReciboReader` before building the sheet. | SPEC-derived (Decisions 17, 18; Requirement 15). A FlowDocument has no overlay layer, and a `BlockUIContainer` cannot paginate, so a true overlay would clip | **Medium** on the content; **weak** on the watermark |
| D14 | **Amount in words is a pure function in Presentation**, no library: "Son Pesos " + words + " con NN/100.-", up to 999,999,999,999, Spanish apocope rules ("un", "veintiún", "ciento un mil"), "cien" exactly 100, "cero", "menos" for negatives. | SPEC-derived (Decision 36, Test 24b) | **Strong** |
| D15 | **Configuration**: `appsettings.json` gains `Recargo` (`DailyRate`, `GraceDays`) and `Agency` (name, address, phone, email, tax status, rental legend, broker name, broker title, licence number, logo file). Recargo is parsed in Infrastructure (Test 7); agency data is parsed and validated in Presentation at print time (Test 24c). The logo is a shipped file under `src/Inmobiliaria.Desktop/Assets/`. | SPEC-derived (P11, Decisions 33, 37); VERIFIED: endpoint is read with `JsonDocument`, not Microsoft.Extensions.Configuration (`App.xaml.cs:116-136`) | **Medium-strong** |
| D16 | **Invalid recargo terms disable the cash desk and the voiding screen**, not only the desk, because voiding is planned with the same terms. The other screens (recent list, recibo display, print, go-live) still open. | VERIFIED: `VoidingPlanner.Plan` receives `_terms` (`CollectionDesk.cs:277-278`). See §12 C3 | **Strong** |
| D17 | **Account search is done in memory in the adapter**, over a fold (lower case, diacritics removed) of tenant names, lessor names and unit descriptions. Minimum 2 characters, at most 50 results, sorted by tenant. No `unaccent` extension. | DESIGN CALL; VERIFIED: no extension exists in any migration (search of `src` finds no `EXTENSION`) | **Medium** |
| D18 | **"Find by contract" means by lessor name or by the contract's short id** (first 8 hex characters, shown in the listing), because the contract has no number an operator knows. | VERIFIED: `Contract.cs:25-32` holds no human-facing identifier. See §12 C2. No folder number or other field is added (Tomás, 2026-10-22, Q2) | **Strong** |
| D19 | **Recent collections: the newest 50, no paging.** Older payments are found by number. | DESIGN CALL (spec "Parameters Left to Design") | **Medium** |
| D20 | **The recibo-bag reading helpers move out of `CollectionDesk` into one internal mapper** (`ReciboRows`) that the desk and the reader share. A move only; no behaviour change, covered by the existing desk tests. This touches a file the proposal listed as "not touched". | DESIGN CALL; VERIFIED: the helpers are private (`CollectionDesk.cs:451`, `:511-527`), and the class comment says no other file knows the bags (`:24-27`), which the reader would break either way | **Medium** |
| D21 | **The received date is sent as `null` unless the operator typed one**, and the screen shows the preview's `ReceivedOn`. Sending the displayed date would freeze "today" and defeat the midnight scenario. | VERIFIED: `BuildPreview` uses `request.ReceivedOn ?? today` (`CollectionDesk.cs:340`); `ResolvedToday` is part of preview equality (`CollectionPlanner.cs:124`) | **Strong** |
| D22 | **Double-click and busy are handled by `AsyncRelayCommand`'s default (no concurrent execution) plus one shared `IsBusy` flag per screen** that every state-changing command's `CanExecute` reads. | DESIGN CALL on CommunityToolkit.Mvvm 8.4 behaviour | **Medium-strong** |
| D23 | **Previews are latest-wins**: each input change cancels the previous preview's token and bumps a version; a result whose version is stale is dropped. Money fields bind on lost focus, not per keystroke. No debounce timer. | DESIGN CALL | **Medium** |
| D24 | **One generic error path in Presentation** (`OperationRunner`): every screen operation runs through it; a refusal never reaches it (refusals are values); any exception becomes a fixed Spanish message with no exception text; a failed commit adds the "may or may not have been recorded, check the recent collections" text. Desktop adds a `DispatcherUnhandledException` backstop that shows the same message. | SPEC-derived (Requirement 4) | **Strong** |
| D25 | **Setup refusals are values.** `IGoLiveSetup` returns `SetupResult` with a `SetupRefusal?`; the adapter maps the domain check and the database's named errors (`contracts_cutover_locked`, `contracts_cutover_admin_only`, `recibo_numbering_cutover_locked`, `ck_recibo_numbering_starting_positive`, and SQLSTATE `42501` from the column grant, scoped to that one statement). Anything else surfaces as an error. | SPEC-derived (Decision 31); VERIFIED precedent `DeskErrorMap` (`CollectionDesk.cs:536-558`); column grant `20261015204506_AddCollectionsAndRecibos.cs:408` | **Medium-strong** |
| D26 | **The annulment's user is shown by display name**: the reader joins `recibo_annulments.annulled_by` (a role name) to `app_users.username`, falling back to the role name. | VERIFIED: `annulled_by` is type `name`, stamped `current_user` (`20261015204506_AddCollectionsAndRecibos.cs:104`, `:317`); `username` equals the role name (`AppUserConfiguration.cs:9`) | **Medium-strong** |
| D27 | **Six slices, not four**: S1 shell, S2 go-live, S3a read ports, S3b desk, S4a recent list and voiding, S4b printing. S3 and S4 both split. | Proposal §11 forecast S3 ~650 and S4 ~550 lines | **Medium** (advisory; `sdd-tasks` owns the plan) |

---

## Table of Contents

1. [Technical approach](#1-technical-approach)
2. [Data flow](#2-data-flow)
3. [Project layout](#3-project-layout)
4. [Session composition and one context per operation](#4-session-composition-and-one-context-per-operation)
5. [The three new ports and their adapters](#5-the-three-new-ports-and-their-adapters)
6. [Go-live: the cutover rule, the migration, the starting number](#6-go-live-the-cutover-rule-the-migration-the-starting-number)
7. [The cash desk flow](#7-the-cash-desk-flow)
8. [Voiding](#8-voiding)
9. [Printing](#9-printing)
10. [Configuration](#10-configuration)
11. [Testing strategy](#11-testing-strategy)
12. [Spec claims checked against the code](#12-spec-claims-checked-against-the-code)
13. [Slices, sizes and test mapping](#13-slices-sizes-and-test-mapping)
14. [Threat matrix, migration and rollback, risks](#14-threat-matrix-migration-and-rollback-risks)
15. [Questions for Tomás](#15-questions-for-tomás)
16. [Parameters left to design: where each is settled](#16-parameters-left-to-design-where-each-is-settled)

---

## 1. Technical approach

The domain under this change is finished: `ICollectionDesk` previews, commits and voids
(`ICollectionDesk.cs:44-62`). What is missing is everything around it: a shell that knows the session,
a way to pick an account, read an issued recibo, set the go-live parameters, and print. This design adds:

- **Three read/setup ports in Domain** with Testcontainers-tested adapters in Infrastructure.
- **A per-call wrapper per port**, so each operation gets its own context without the view-models
  knowing a context exists.
- **One Presentation project** where all screen logic lives as plain classes, testable on Linux.
- **One small domain change** (cutover rule, refusal text) and **one hand-written migration** (Admin-only
  cutover date).
- **A WPF printer** in Desktop that renders a print model Presentation already built and tested.

Nothing in `CollectionPlanner`, `VoidingPlanner`, numbering or the ledger changes behaviour. The one edit
to `CollectionDesk` is a move of private helpers (D20).

## 2. Data flow

```
 Login (unchanged)                                       Desktop (WPF)
 ───────────────                                         ─────────────
 AuthenticationResult.Success(Session, Factory) ──► App.xaml.cs
                                                        │ reads appsettings (endpoint, Recargo, Agency)
                                                        │ services.AddSessionServices(factory, session, terms)
                                                        ▼
                                              ServiceProvider (session)          Infrastructure
                                              ├ IAgencyClock ─────────────────── AgencyClock
                                              ├ ICollectionDesk ─► PerCall ──► CollectionDesk(ctx, clock, terms)
                                              ├ IAccountDirectory ─► PerCall ─► AccountDirectory(ctx, clock)
                                              ├ IReciboReader ───► PerCall ──► ReciboReader(ctx)
                                              ├ IGoLiveSetup ────► PerCall ──► GoLiveSetup(ctx, clock)
                                              └ IDueAdjustmentQuery ► PerCall ► DueAdjustmentQuery(ctx)
                                                        │   each call: ctx = factory.Create(); …; dispose
                                                        ▼
 Presentation (no WPF, no EF)          ShellViewModel(session, ports, settings problems)
                                        ├ CashDeskViewModel ── AccountPicker ── preview/summary/commit
                                        ├ RecentCollectionsViewModel ── ReciboViewModel ── Print
                                        ├ VoidingViewModel
                                        └ GoLiveViewModel (Admin only)
                                                        │ Print: re-read IssuedPayment, build ReciboSheet
                                                        ▼
                                        IReciboPrinter (Presentation interface)
                                                        │
 Desktop                                FlowDocumentReciboPrinter ── PrintDialog ── printer
```

## 3. Project layout

| Project | Target | References | Holds | In `Core.slnf` |
|---|---|---|---|---|
| `src/Inmobiliaria.Domain` | net10.0 | nothing | ports, rules (existing) | yes (`Core.slnf:5`) |
| `src/Inmobiliaria.Infrastructure` | net10.0 | Domain | adapters, per-call wrappers, `AddSessionServices`, recargo settings | yes (`:6`) |
| `src/Inmobiliaria.Presentation` **(new)** | net10.0 (inherited, `Directory.Build.props:4`) | Domain; package CommunityToolkit.Mvvm | view-models, pure presentation functions, print model, `IReciboPrinter` | **yes (added)** |
| `src/Inmobiliaria.Desktop` | net10.0-windows (`Inmobiliaria.Desktop.csproj:5`) | Domain, Infrastructure, **Presentation (added)** | XAML views, `App.xaml.cs` composition, `FlowDocumentReciboPrinter`, logo asset | no (by design) |
| `tests/Inmobiliaria.Presentation.Tests` **(new)** | net10.0 | Presentation, Domain; xunit (as `Inmobiliaria.Domain.Tests.csproj:7-12`) | view-model and print-model tests, faked ports, no Docker | **yes (added)** |

Rules this layout keeps:

- **Presentation references neither Infrastructure nor any WPF, EF Core or Npgsql assembly.** A guard test
  in Presentation.Tests, modelled on `ArchitectureGuardTests.cs:12-35`, adds `Inmobiliaria.Infrastructure`
  to the forbidden prefixes. The Linux build already fails on a WPF reference.
- **Ports stay in Domain**, never in Presentation, so Infrastructure never references Presentation.
- **Nothing behavioural goes into Desktop**: it cannot gate the Linux job (`Core.slnf` excludes it).
- `Inmobiliaria.Pv1.Tests` stays outside the sln and slnf (`Inmobiliaria.Pv1.Tests.csproj:3-13`).
- `Directory.Packages.props` needs **no new package**: Presentation uses CommunityToolkit.Mvvm (`:23`);
  Presentation.Tests uses the test packages already pinned (`:12-15`).

## 4. Session composition and one context per operation

### 4.1 The bootstrap change

`App.xaml.cs:101-111` today stores the factory and shows `new MainWindow()`. After this change, at the
same point:

1. Read the `Recargo` and `Agency` sections from the same `appsettings.json` already parsed for the
   endpoint (`App.xaml.cs:124-136`).
2. `services.AddSessionServices(success.Factory, success.Session, recargo)` into a **second**
   `ServiceCollection`, built into a session `ServiceProvider` held beside `_activeSessionFactory` and
   disposed in `OnExit` before the factory (`App.xaml.cs:44-54`).
3. Build `ShellViewModel` from the resolved ports, the session, the clock, the agency details and the
   settings problems; build `MainWindow(shellViewModel)`.

The forced-password-change path (`App.xaml.cs:82-99`) is untouched; no session container exists before
both gates pass (Requirement 1, second scenario).

### 4.2 `AddSessionServices`

**(new)** `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`:

```csharp
public static IServiceCollection AddSessionServices(
    this IServiceCollection services,
    ISessionDbContextFactory factory,
    IUserSession session,
    RecargoSettings recargo);   // terms or the named problem (§10)
```

It registers, all as singletons that hold no context: `IUserSession`, `IAgencyClock` → `AgencyClock`
(`AgencyClock.cs:13`), and one per-call wrapper for each of `ICollectionDesk`, `IAccountDirectory`,
`IReciboReader`, `IGoLiveSetup`, `IDueAdjustmentQuery`. It registers **no** `DbContext`. `ICollectionDesk`
is registered only when the terms are valid (D16); the shell never opens the desk or voiding otherwise.

### 4.3 Per-call wrappers

**(new)** `src/Inmobiliaria.Infrastructure/Persistence/PerCall/*.cs`. Each wrapper implements its port and
forwards every method through one shared helper:

```csharp
internal static class PerCall
{
    public static async Task<T> RunAsync<T>(ISessionDbContextFactory factory,
        Func<InmobiliariaDbContext, Task<T>> body);   // create, run, dispose
}
```

Why a wrapper rather than injecting `Func<ICollectionDesk>` into view-models: the view-model would then
own disposal of an Infrastructure object, and a transient `IDisposable` resolved from the root provider is
kept alive by the provider until it is disposed. The wrapper keeps disposal in Infrastructure.

Rejected: a scope per screen (a screen lives for minutes and runs many operations; the spec requires one
context per operation); one context per session (not safe across overlapping operations, Requirement 3).

## 5. The three new ports and their adapters

Signatures only. Namespaces follow the existing folders.

### 5.1 `IAccountDirectory` — `src/Inmobiliaria.Domain/Accounts/IAccountDirectory.cs` (new)

```csharp
public interface IAccountDirectory
{
    Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default);
}

public sealed record AccountListing(
    Guid AccountId, Guid ContractId, string ContractShortId,
    string Tenants, string Lessors, string Units,
    ContractStatus Status, DateOnly? CutoverDate, AccountReadiness Readiness);

public sealed record AccountReadiness(bool RateEntered, bool Frozen, bool StartingNumberSet)
{
    public bool IsReady => RateEntered && !Frozen && StartingNumberSet;
}
```

Adapter `src/Inmobiliaria.Infrastructure/Persistence/AccountDirectory.cs` (new), constructor
`(InmobiliariaDbContext, IAgencyClock)`:

- Loads every contract account with its contract, parties and units, **no tracking**, and never
  materialises accruals (unlike `MoraWorklistQuery`, which does): a search writes nothing.
- `RateEntered` = `Contract.HonorariosPercentage is not null` (0% is entered; same test as
  `CollectionPlanner.cs:260`).
- `Frozen` = a `RecargoFrozen` movement on or before the agency's today, the rule of
  `ContractAccount.IsFrozen` (`ContractAccount.cs:320-321`), as one `EXISTS` per account.
- `StartingNumberSet` = `recibo_numbering.starting_number IS NOT NULL`, one query for all listings.
- Names are formatted as the desk formats them (`CollectionDesk.cs:421`: `"{LastName}, {FirstName}"`) so the
  listing and the recibo agree.
- Search folds the query and each candidate field (D17) and matches by substring on tenants, lessors,
  units and the short id (D18). Every contract status is listed (Decision 29). A contract without an
  account is absent here (it cannot be collected) and present in the go-live list.

### 5.2 `IReciboReader` — `src/Inmobiliaria.Domain/Collection/IReciboReader.cs` (new)

```csharp
public interface IReciboReader
{
    Task<IssuedPayment?> FindByNumberAsync(long reciboNumber, CancellationToken ct = default);
    Task<IssuedPayment?> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default);
    Task<IReadOnlyList<RecentCollection>> RecentAsync(int limit, CancellationToken ct = default);
}

public sealed record IssuedRecibo(Guid ReciboId, Recibo Recibo);

public sealed record AnnulmentRecord(string Reason, string AnnulledBy, DateTimeOffset AnnulledAt);

public sealed record IssuedPayment(
    Guid CollectionId, DateOnly RecordedOn, DateTimeOffset RecordedAt,
    IssuedRecibo Locatario, IssuedRecibo Propietario, AnnulmentRecord? Annulment)
{
    public bool IsAnnulled => Annulment is not null;
}

public sealed record RecentCollection(
    Guid CollectionId, long LocatarioNumber, long PropietarioNumber, string Tenant,
    DateOnly Period, DateOnly ReceivedOn, DateTimeOffset RecordedAt, bool IsAnnulled);
```

This is the annulment read model Finding F3 asks for: reason, user and time, read at the moment of the
call (Tests 21, 22). Adapter `src/Inmobiliaria.Infrastructure/Persistence/ReciboReader.cs` (new):

- A number below 1 or above `int.MaxValue` returns null **without** a query; the column is `integer`
  (`20261015204506_AddCollectionsAndRecibos.cs:78`), so the reader never widens what the database holds.
- A number finds its recibo, then both recibos of its collection (either copy names the pair).
- Recibos are rebuilt with the shared `ReciboRows` mapper (D20), so a display equals what the desk
  issued, header snapshot included (`ReciboHeader.cs:3-8`; Test 23).
- `RecordedOn` and `AnnulledAt` are converted through the agency zone, as the desk does
  (`CollectionDesk.cs:38`, `:272`); Presentation never knows a time zone.
- `AnnulledBy` is the display name (D26).
- Recent: newest 50 by `recorded_at` (D19), with an `EXISTS` on `recibo_annulments` for the flag.

### 5.3 `IGoLiveSetup` — `src/Inmobiliaria.Domain/GoLive/IGoLiveSetup.cs` (new)

```csharp
public interface IGoLiveSetup
{
    Task<IReadOnlyList<CutoverListing>> ListContractsAsync(CancellationToken ct = default);
    Task<SetupResult> SetCutoverDateAsync(Guid contractId, DateOnly cutoverDate, CancellationToken ct = default);
    Task<StartingNumberState> GetStartingNumberStateAsync(CancellationToken ct = default);
    Task<SetupResult> SetStartingNumberAsync(int startingNumber, CancellationToken ct = default);
}

public enum CutoverState { Changeable, Locked, NoAccount }

public sealed record CutoverListing(
    Guid ContractId, string Tenants, string Units, DateOnly StartDate, DateOnly? CutoverDate, CutoverState State);

public sealed record StartingNumberState(int? StartingNumber, bool AnyReciboIssued)
{
    public bool IsSet => StartingNumber is not null;
    public bool IsLocked => AnyReciboIssued;
}

public enum SetupRefusal
{
    CutoverAfterToday, CutoverBeforeContractStart, CutoverLocked, CutoverAdminOnly, NoAccount,
    StartingNumberLocked, StartingNumberAdminOnly, StartingNumberOutOfRange,
}

public sealed record SetupResult(SetupRefusal? Refusal) { public bool Succeeded => Refusal is null; }
// plus a SetupRefusalText.OperatorText extension, Spanish, like CollectionRefusalText (CollectionPlanner.cs:57-89)
```

Adapter `src/Inmobiliaria.Infrastructure/Persistence/GoLiveSetup.cs` (new), constructor
`(InmobiliariaDbContext, IAgencyClock)`:

- **List**: every contract, left-joined to its account; `Locked` when the account holds any movement (the
  rule of `Contract.cs:203` and the trigger), `NoAccount` when there is none (Decision 30).
- **Set cutover**: load the contract tracked and its account with movements; `contract.CheckCutoverDate(
  date, account, clock.Today)` → a refusal value, or `SetCutoverDate(...)` and save; map
  `contracts_cutover_locked` → `CutoverLocked` and `contracts_cutover_admin_only` → `CutoverAdminOnly`
  (a lock that arrives between the check and the save is a refusal, Requirement 18 last scenario).
- **Starting-number state**: `starting_number` of the singleton row and `EXISTS (SELECT 1 FROM recibos)`,
  both readable by either role (`20261015204506_AddCollectionsAndRecibos.cs:404-406`).
- **Set starting number**: delegates to the existing `ReciboNumbering` (`ReciboNumbering.cs:24-38`) on the
  same context, and maps `recibo_numbering_cutover_locked` → `StartingNumberLocked`,
  `ck_recibo_numbering_starting_positive` → `StartingNumberOutOfRange`, `42501` → `StartingNumberAdminOnly`.
  `IReciboNumbering` is unchanged.

The Npgsql exception reading follows the desk's shape: an inner-exception walk and a string-in mapping
table (`CollectionDesk.cs:395-412`, `:536-558`), so the table is tested without a database.

## 6. Go-live: the cutover rule, the migration, the starting number

### 6.1 The domain rule

`src/Inmobiliaria.Domain/Leasing/Contract.cs` (modify) and `CutoverRefusal.cs` (new):

```csharp
public enum CutoverRefusal { AfterToday, BeforeContractStart, AccountHasMovement }

public CutoverRefusal? CheckCutoverDate(DateOnly cutoverDate, ContractAccount account, DateOnly today);
public void SetCutoverDate(DateOnly cutoverDate, ContractAccount account, DateOnly today);
```

- Order of checks: own account (still `ArgumentException`, `Contract.cs:212-220`), movement, after today,
  before start.
- `SetCutoverDate` keeps its exception contract: movement → `InvalidOperationException` (as today,
  `Contract.cs:203-207`), bounds → `ArgumentOutOfRangeException`. The adapter calls `Check` first, so the
  screen sees values; a caller that skips the check still cannot store a bad date.
- The two-argument overload is **removed**, not kept with a default: a caller that forgets `today` must not
  compile. The three existing calls in `ContractualFeePlanTests.cs:212`, `:228`, `:240` gain a `today`.
- The database does **not** repeat the bounds (only the lock and, now, the role). Stated as a limit: an
  Admin with raw SQL can store a future date. Low consequence; adding it to the trigger is cheap if wanted.

### 6.2 The migration

**(new)** `src/Inmobiliaria.Infrastructure/Persistence/Migrations/<timestamp>_HardenCutoverDateToAdmin.cs`
and its `.Designer.cs`.

How it is produced, so the model cannot drift:

1. `dotnet ef migrations add HardenCutoverDateToAdmin -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`
   on the **unchanged** model. EF generates empty `Up`/`Down`, a Designer carrying the same target model,
   and rewrites the snapshot identically. Offline: `DesignTimeDbContextFactory.cs:16-27` needs no database.
2. Fill `Up` and `Down` with `migrationBuilder.Sql(...)` only.
3. `git diff` shows **no change** to `InmobiliariaDbContextModelSnapshot.cs`.
4. `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`
   exits 0 with "no changes" (Test 40). It compares the current model with the **last migration's**
   snapshot, which is stronger than the model validator and needs neither database nor Docker. Read the
   exit code, not a grep of the output (the machine prints Spanish).

`Up` replaces the body of `reject_cutover_change_after_movement()`:

```
IF NEW.cutover_date IS DISTINCT FROM OLD.cutover_date THEN
    IF EXISTS (movement on this contract's account) THEN
        RAISE ... USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_locked';      -- unchanged
    END IF;
    IF NOT pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER') THEN
        RAISE ... USING ERRCODE = 'P0001', CONSTRAINT = 'contracts_cutover_admin_only';  -- new
    END IF;
END IF;
RETURN NEW;
```

- **Lock first, role second**, so every existing caller sees exactly the refusal it sees today.
- **Not `SECURITY DEFINER`**, so `current_user` is the logged-in role, as in the annulment trigger
  (`20261015204506_AddCollectionsAndRecibos.cs:233-236`).
- The trigger `contracts_cutover_lock BEFORE UPDATE OF cutover_date` is left as it is: it fires only when a
  statement names the column, so an Empleado's other updates never reach the function, and naming it
  without changing it is not a change (Requirement 22, Test 36).
- A superuser is a member of every role, so the existing Testcontainers tests that update `cutover_date`
  as the container's superuser keep passing; the new tests connect as real login roles.
- `Down` restores the original body from `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:153-169`,
  verbatim. No data changes either way.

### 6.3 The starting number at the screen

`GoLiveViewModel` parses the typed text with `int.TryParse` (invariant, integer style only) and refuses
anything that is not an integer from 1 to `int.MaxValue` **before** calling the port (Decision 32,
Test 30). After any set, the screen re-reads the state; a refusal from the database is shown as its
operator text (Test 31).

### 6.4 The `CutoverNotSet` text (Finding F1)

`CollectionPlanner.cs:81-82` becomes, in substance: "The starting recibo number is not set. An
Administrator must set it in Go-live setup before collecting." (Spanish in the code.) The doc comment at
`:46` is corrected to say the desk raises it when the recibo counter has no starting number. Chosen over a
screen mapping because the text is wrong for every reader, not only for this screen, and two texts for one
refusal is the kind of drift this project has already paid for. Rejected: renaming the member, which would
churn `CollectionDeskAtomicityTests.cs:112-124` and the spec's vocabulary for no behavioural gain.

## 7. The cash desk flow

### 7.1 States

```
NoAccount ─choose─► Owed ─type amount─► Ready ─continue─► Summary ─commit─► Committing ─┬─► Issued
              ▲      │  (preview, 0)     (preview, X,      (frozen pair:                  │
              │      │                    CanCommit)        preview + request)            ├─► Refused (text)
              │      └─ any input change ◄────────────────────┘                           ├─► Stale ─► Owed/Ready
              │                                                                            └─► Failed (generic,
              └──────────────── choose another account ◄─────────────────────────────────        "check recent")
```

- **Choosing an account** loads, once: the listing's readiness (D8), the pending adjustments for the
  contract (D9), then the owed-step preview with `HandedAmount = 0` (Decision 26).
- **Owed** shows period, received date, previous amount owed, ledger figure, resulting amount owed, both
  drafts and every warning. `AmountNotTotal` at this step is shown as figures, not as an error
  (Test 12). Any other refusal is shown in its operator text and blocks the summary.
- **Starting number unset** (readiness) blocks the summary with the corrected text and a pointer to
  Go-live setup (D7, D8).
- **Typing the handed amount** previews again with it. The field starts empty and is never pre-filled
  (Requirement 8). The ledger figure and each recibo total are shown under separate labels; the label of
  the handed amount says it is compared with the ledger figure.
- **Summary** holds the exact `(CollectionPreview, CollectionRequest)` pair that built it. Commit sends that
  pair (`ICollectionDesk.cs:50-51`). Changing any input clears the pair and disables commit (Test 15).
- **Issued** shows both numbers, locatario first (`CollectionResult.Committed`, `ICollectionDesk.cs:23-24`, makes propietario = +1), and
  opens the recibo display by `GetByCollectionAsync(result.CollectionId)`. Nothing prints (Requirement 12).

### 7.2 Stale and concurrent refusals

On `PreviewStale` or `ConcurrentChange`, the view-model calls `PreviewAsync` once with the same request,
computes `PreviewDifference.Between(confirmed, fresh)` (**new**, pure, Presentation), shows it, keeps the
typed amount and clears the summary. It never calls `CommitAsync` again on its own (Test 17 counts the
fake desk's commit calls). `PreviewDifference` lists, when they differ: period, received date, previous
amount owed, ledger figure, resulting amount owed, and each recibo line whose amount changed, appeared or
disappeared, matched by `(copy, kind)`.

### 7.3 Pending adjustment warning

From `GetDueAsync(today)` filtered by `ContractId` (`DueAdjustment.cs:27-35`):

- `Missing` non-empty → one warning per missing value, naming `IndexName` and `Period`
  (`DueAdjustment.cs:10`).
- `Missing` empty → "an adjustment is waiting for confirmation".
- No entry → no warning. The warning never blocks (Decision 39).

Only `Active` contracts are ever reported (`DueAdjustmentQuery.cs:31`); see §12 C4.

### 7.4 Busy, double click, errors

Every state-changing command is an `AsyncRelayCommand` (no concurrent execution by default) whose
`CanExecute` also reads the screen's `IsBusy`; preview, commit, void, set and print-preparation set it
(Test 18). Every operation runs through `OperationRunner` (D24), which resets `IsBusy` in `finally`, so the
commands come back after a failure (Requirement 11).

## 8. Voiding

`VoidingViewModel` (new). Entry by typed number (`FindByNumberAsync`) or from the recent list
(`GetByCollectionAsync`). Flow: found → reason typed → preview → confirm → void.

- **Pre-gate**: `VoidingGate.For(recordedOn, today, role, isAnnulled)` (**new**, pure). Age =
  `today.DayNumber - recordedOn.DayNumber`, the same arithmetic as `VoidingPlanner.cs:109`, with the same
  constants (`CollectionRules.cs:20`, `:26`). The explanation reuses the domain's own texts
  (`VoidingPlanner.cs:27-40`), so the pre-gate and the refusal say the same thing.
- **Reason first**: blank reason → the `ReasonRequired` text, no call to the port (Requirement 16).
- **Preview** calls `PreviewVoidAsync(locatario.ReciboId, reason, session.Role)`
  (`ICollectionDesk.cs:57-58`) and shows corrections, resulting amount owed and both annulments.
- **Stale**: `PreviewStale` from `VoidAsync` re-previews once and requires a new confirmation (Test 19).
- **After a voiding**, the payment is re-read; the screen shows it annulled; nothing prints.
- The database decides whatever the pre-gate allowed (Decision 28): a refusal from `VoidAsync` is shown as
  its operator text.

## 9. Printing

### 9.1 What Presentation builds (all tested on Linux)

| Type (new) | Responsibility |
|---|---|
| `PaperRows` | The ordered row table of D12: Alquiler (+ period), Tasa Municipal, Sellado Provincial, Imp. Inmobiliario, Aguas Prov. Sta. Fe, Litoral Gas, Luz EPE, CUOTA 1 and CUOTA 2 (indented), Dep. en Garantía, Recargo, Reserva, Expensas, Saldo a Favor, Saldo Deudor, Honorarios, Otros Conceptos. Mapped kinds: Rent, TasaMunicipal, Recargo, SaldoDeudor, HonorariosAdministracion, OtrosConceptos (`ReciboLineKind.cs:20-50`). Added-row labels for Seguro and ContractualFeeInstalment |
| `AmountInWords` | D14 |
| `AgencyDetails` | Parsed from the `Agency` section; `Validate()` returns the names of missing items |
| `ReciboSheetBuilder` | `IssuedPayment` + copy + `AgencyDetails` → `ReciboSheet`: two `ReciboHalf` values identical except the caption, each with header data, payer line (tenant for Locatario, lessors for Propietario, CUIT "-"), date from `ReceivedOn`, rows, Importe Neto, words, five unticked I.V.A. boxes, blank Locador line, broker block, and the ANULADO block when annulled |
| `SheetArrangement` | D11: `(printableWidth, printableHeight, halfPageCount) → TwoUp \| OnePerPage` |
| `IReciboPrinter` | `Task<PrintOutcome> PrintAsync(IReadOnlyList<ReciboSheet> sheets)`; `PrintOutcome` is `Printed` or `Cancelled`. Faked in tests (Test 20) |

The sheet carries only what Requirement 24 allows beyond the stored recibo: caption, agency data, words,
empty paper rows, and the annulment. The legend printed is the stored one (`Recibo.Legend`,
`Recibo.cs:15`, `:36`).

### 9.2 What Desktop does (checked by hand, M1 to M3)

**(new)** `src/Inmobiliaria.Desktop/Printing/FlowDocumentReciboPrinter.cs`:

1. Create a `PrintDialog`; preset its ticket to A4 landscape; `ShowDialog()`. Cancel → `Cancelled`, nothing
   else happens (no number is consumed: printing never touches the database).
2. From `PrintableAreaWidth/Height`, build one half alone at half width and read its page count; ask
   `SheetArrangement`.
3. Build one `FlowDocument` for all sheets: `PageWidth/Height` from the printable area; two-up sets
   `ColumnWidth` to half the width, `IsColumnWidthFlexible = false`, and `BreakColumnBefore` on DUPLICADO;
   one-per-page uses one column and `BreakPageBefore` on DUPLICADO. Each further sheet starts with
   `BreakPageBefore`. Content wraps inside table cells, so nothing is clipped.
4. `PrintDocument(paginator, "Recibo N")`.

Missing agency data, or a logo file that does not exist, stops before the dialog with a message naming
the item (Decision 37).

### 9.3 Print actions

`ReciboViewModel` offers **Print this recibo** and **Print both recibos of the payment**. Both first call
`GetByCollectionAsync` again (D13), then build and print. The display itself shows the stored lines in
their stored order with Spanish kind labels; the paper's row order governs only the printed sheet.

## 10. Configuration

`src/Inmobiliaria.Desktop/appsettings.json` (modify) gains:

```json
"Recargo": { "DailyRate": 0.02, "GraceDays": 0 },
"Agency": {
  "Name": "...", "Address": "...", "Phone": "...", "Email": "...", "TaxStatus": "...",
  "RentalActivityLegend": "...", "BrokerName": "...", "BrokerTitle": "...", "LicenceNumber": "...",
  "LogoFile": "Assets/agency-logo.png"
}
```

- The `Agency` values are the agency's public letterhead; Tomás supplies them and the logo file when S4b
  is applied. This design does not copy them from the sample.
- `RecargoSettings.Read(JsonElement root)` (**new**, Infrastructure) returns either `RecargoTerms` or one
  problem naming the key (`Recargo:DailyRate` missing, not a number, or rejected by the `RecargoTerms`
  constructor, `RecargoTerms.cs:33-56`). No fallback value exists anywhere (Decision 33).
- The credential guard (`CompositionGuardTests.cs:20-39`, tokens `Password`, `ConnectionString`, `Pwd=`)
  keeps passing: none of the new keys contains them.
- The logo is copied to the output folder like `appsettings.json` (`Inmobiliaria.Desktop.csproj:10-14`).

## 11. Testing strategy

| Where | What | Docker |
|---|---|---|
| `Inmobiliaria.Domain.Tests` | Cutover rule bounds and order (32, 34 domain half); existing cutover scenarios still pass (33); the `CutoverNotSet` text names the starting number and not the cutover date (13, domain half) | No |
| `Inmobiliaria.Infrastructure.Tests`, **no container** | Session wiring resolves every port for an Admin and an Empleado with a spy factory whose `Create()` count stays 0, and registers no `DbContext` (3); two calls create two distinct contexts, each disposed, via a context whose connection opening an interceptor refuses (4); recargo settings read from the shipped `appsettings.json` give 0.02 and 0, missing or out-of-range values name the key (7); setup error mapping table (31's adapter half); the search fold ("Demaría" ↔ "demaria") | No |
| `Inmobiliaria.Infrastructure.Tests`, **Testcontainers** | Directory search and readiness, as each role (8, 9); reader by number, by collection, recent, annulment read fresh, as-issued after a rate or name change (21, 22, 23, 41); go-live list, set, correct, lock (27, 28, 29, 30's port half); trigger: Admin allowed, Empleado refused, other updates allowed, lock for both, insert-then-refuse, Down restores the body (35 to 39) | **Yes** |
| `Inmobiliaria.Presentation.Tests` | Every view-model and pure function: 1, 2, 5, 6, 10 to 18, 13a, 19, 20, 24, 24a to 24d, 25, 26, 27's display half, 30's before-the-port half, 31; plus a Presentation architecture guard | No |
| Command | `has-pending-model-changes` on S2 (40) | No |
| By hand, Tomás | M1 to M3 on a real printer or Microsoft Print to PDF | No |

Notes for whoever writes the tests:

- Role-based tests need real login roles. `RolePermissionTests.cs:47` (`CreateTestLoginRoleAsync`) and
  `:69` (`BuildConnectionAs`) are private today; S2 moves them to a shared test helper.
- The `Down` test (39) migrates backwards, so it runs on an `IsolatedDatabase`
  (`ReciboSchemaTests.cs:931`, used at `:53-74`), never on the shared container.
- **Prove each new guard by making it fail**: the Admin check is first seen refusing an Admin when the
  `pg_has_role` line is inverted; the session guard is first seen failing with a `DbContext` registered;
  the Presentation guard with a deliberate Infrastructure reference. Then restore.
- A skipped Testcontainers test is an unrun test: S1 already needs Docker, because its own tests run in
  the Infrastructure.Tests project whose other tests need the container.

## 12. Spec claims checked against the code

| # | Claim | Evidence | Verdict and what this design does |
|---|---|---|---|
| C1 | Requirement 7 and Test 13 treat "starting number never set" as something the **preview** reports as `CutoverNotSet` | `PreviewAsync` never reads the counter (`CollectionDesk.cs:55-64`); only `CommitAsync` returns `CutoverNotSet` (`:120-123`) | **Partly false.** The preview cannot report it. D8: the desk blocks the summary from the listing's readiness; the commit's refusal stays as a backstop |
| C2 | Requirement 6: find an account "by contract" | `Contract` has no number, code or name an operator knows (`Contract.cs:25-32`) | **No such key exists.** D18 (weak) and Q2 |
| C3 | Decision 33: invalid recargo terms stop "the cash desk" | Voiding is planned with the same terms (`CollectionDesk.cs:277-278`) | **Wider than stated.** D16: voiding is unavailable too |
| C4 | Decision 39 / Requirement 7: a due, unconfirmed adjustment on the previewed contract warns | The worklist reports `Active` contracts only (`DueAdjustmentQuery.cs:31`) | **True for Active, silent for `PendingTermination`.** Accepted (Decision 39 adds nothing to the domain); a contract with notice given and an adjustment due shows no warning. Low consequence |
| C5 | Proposal §7: `CollectionDesk` is "not touched" | The reader needs the private bag helpers (`CollectionDesk.cs:451`, `:511-527`) | **Overturned, narrowly.** D20 moves them; no behaviour change |
| — | Smaller: the annulment's "user" is a role name, not a person's name | `annulled_by` type `name` = `current_user` (`20261015204506_AddCollectionsAndRecibos.cs:104`, `:317`) | D26 joins `app_users` |

## 13. Slices, sizes and test mapping

Advisory sizes (authored lines including tests), per the ~400-line planning heuristic; not a cap.

| Slice | Content | Tests | Est. | Docker | Supabase |
|---|---|---|---|---|---|
| **S1** shell and plumbing | Two new projects in sln and slnf; Presentation guard; `AddSessionServices` with the per-call wrappers for the **existing** ports (`ICollectionDesk`, `IDueAdjustmentQuery`); recargo settings; `ShellViewModel`, navigation by role, `OperationRunner`; `MainWindow` shell | 1, 2, 3, 4, 5, 6, 7 | ~450 | **Yes** | No |
| **S2** go-live | `CutoverRefusal` and the rule; `CutoverNotSet` text; migration; `IGoLiveSetup` + adapter + wrapper; `GoLiveViewModel` + view; shared role-test helper | 13 (domain), 27 to 40 | ~550 | **Yes** | **Restored by Tomás, after merge, to apply the migration** |
| **S3a** read ports | `IAccountDirectory`, `IReciboReader`, adapters, wrappers, `ReciboRows` move | 8, 9 (flags), 21, 22, 23, 41 (reader) | ~450 | **Yes** | No |
| **S3b** cash desk | Picker and desk view-models and views, `PreviewDifference`, adjustment warning, recibo display (read-only) | 9 (display), 10 to 18, 13a | ~600 | **Yes** (suite) | No |
| **S4a** recent list and voiding | Recent list, `VoidingGate`, `VoidingViewModel` and view | 19, 25, 26 | ~400 | **Yes** (suite) | No |
| **S4b** printing | Print model, `AmountInWords`, `PaperRows`, `AgencyDetails`, `SheetArrangement`, `FlowDocumentReciboPrinter`, Print actions, `Agency` settings and logo | 20, 24, 24a to 24d, M1 to M3 | ~500 | **Yes** (suite) | No |

**Where to split further, if wanted**: S2 into S2a (domain rule, text, migration, trigger tests 32 to 40)
and S2b (port, adapter, screen, 27 to 31); S3b into the picker and the desk flow. **Order**: S1 → S2 →
S3a → S3b → S4a → S4b. S2 is independent of S3 and S4 and may move after them if go-live is not urgent.

Total about 2,950 authored lines, more than the proposal's 2,150: the proposal did not count the paper
template, amount in words, the stale diff, or the role-test helper.

Decision needed before apply: Yes
Chained PRs recommended: Yes
400-line budget risk: High

## 14. Threat matrix, migration and rollback, risks

**Threat matrix**: N/A — no routing, shell command, subprocess, VCS/PR automation, executable-file
classification or process-integration boundary. The security-relevant boundary here is the database's
role enforcement, covered by Tests 35 to 38 and the "prove the guard by making it fail" step.

**Migration and rollback**: one hand-written migration (§6.2); its `Down` restores the previous function
body, lossless at any time. Tomás applies it to Supabase after S2 merges. Every slice reverts by its merge
commit; the new projects revert by removing them from the sln and slnf. Data written through the screens
is agency record and is not rolled back (a recibo is voided, never deleted).

| Risk | Likelihood | Mitigation |
|---|---|---|
| Two-up columns let ORIGINAL's overflow run into the right column | Medium | D11's fit check falls back to one per page before rendering; M1 |
| The VisualBrush watermark does not survive the printer driver | Medium | The banner carries the required content; the watermark is decorative (D13); M2 |
| A preview result arrives after a newer input | Medium | D23 latest-wins with cancellation |
| `DueAdjustmentQuery` is slow on Supabase from the office | Low | One call per account selection; a by-contract method is a small additive change |
| `42501` mapped to "Admin only" hides another permission bug | Low | Mapped only around the one `UPDATE recibo_numbering` statement |
| The in-memory search grows slow with many contracts | Low | Two-person agency; an `unaccent` index is the later option |
| Moving the bag helpers breaks the desk | Low | Move only; the full desk test suite is the proof |

## 15. Questions for Tomás

Ranked. Neither blocks this design.

1. **Answered 2026-10-22: `stacked-to-main`**, as recommended. The original question, kept for the
   record: **Chain strategy for the six slices** (proposal Open Item 1). It blocks `sdd-tasks`. My recommendation
   is **`stacked-to-main`**: each slice PR targets `develop` once the previous one merged, which is how you
   already work in GitHub Desktop and keeps each review diff clean. `feature-branch-chain` only helps if
   you want to review several slices before any reaches `develop`. **Held medium.**
2. **Answered 2026-10-22: no extra field.** Search by tenant, unit and lessor, with a short id shown;
   Nicolás is not asked. The original question, kept for the record: **How does the agency refer to a contract when it is not by tenant or unit?** The system has no
   contract number (C2). I propose searching by tenant, unit and lessor, and showing a short id. If the
   agency uses a folder number or similar, that is a field for the initial data load. **Held weak**; ask
   Nicolás only if you think they use one.

## 16. Parameters left to design: where each is settled

| Parameter (spec) | Settled in |
|---|---|
| Port names and shapes | §5 (D4) |
| Recent list size and paging | D19 |
| Picker minimum search length and limit | D17 (2 characters, 50) |
| Cutover lower bound | D5 (the contract's start, not refined) |
| How the rule gets today and the start | D5 (argument; `StartDate` from the contract) |
| `CutoverNotSet`: Domain text or screen mapping | D7 (Domain) |
| Constraint names | `contracts_cutover_admin_only` (D6); existing names reused (D25) |
| Legible minimum type and the two-up switch | D11 |
| Agency data and logo location | D15, §10 |
| Paper or catalog order on screen | §9.3 (stored order on screen; paper order on the sheet) |
| Number-to-words algorithm | D14 |
| Print test seam | `IReciboPrinter` (§9.1) |
| Operator message wording | Implementation, Spanish, in Presentation; refusal texts stay in Domain |
| Visual layout and captions | §9.2; final look checked in M1 |
| Recargo settings keys | `Recargo:DailyRate`, `Recargo:GraceDays` (§10) |
