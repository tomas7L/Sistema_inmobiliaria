# Design: mora (the overdue-accounts worklist)

Technical design for the `mora` change, from `spec.md` (Requirements 1 to 7, Decisions P1 to P7 and 8 to 14, Tests 1 to 14,
approved by Tomás on 2026-11-11) and `proposal.md` (merged as PR #108) in this folder. Written 2026-11-11. Nothing here is code;
nothing here edits the spec. Every existing type, member and file named was read and is cited `path:line`; anything new is
marked **(new)**. Paths use `Domain` = `src/Inmobiliaria.Domain/`, `Infra` = `src/Inmobiliaria.Infrastructure/Persistence/`,
`Pres` = `src/Inmobiliaria.Presentation/`, `Desk` = `src/Inmobiliaria.Desktop/`.

**How complete the audit was.** Read in full: `Infra/MoraWorklistQuery.cs`, `Domain/Accounts/MoraAccount.cs`,
`Infra/AccountDirectory.cs`, `Domain/Collection/IAccountDirectory.cs`, `Pres/Shell/ShellViewModel.cs`,
`src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`. Read in part: `Domain/Accounts/ContractAccount.cs`,
`Domain/Accounts/AccrualSchedule.cs`, `Domain/Collection/ContractCancellation.cs`, `Infra/AccountSearch.cs`,
`Infra/AdjustmentDesk.cs`, `Pres/CashDesk/CashDeskViewModel.cs`, `Desk/App.xaml.cs`, the test list of
`MoraWorklistQueryTests.cs`. **Not run:** no build and no test. Test 1 failing against today's code is still a prediction.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** rows are where you can change my mind |
| **§6 Findings** | What the code does that the spec did not say. F7 moves part of spec Test 10 to another project |
| **§7 Slices** | One PR now, one with Mateo's screens, and what each needs |

**Skip** §1 to §5 unless you want the detail: they are for whoever implements and for the second inspection.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | **One due-date rule inside the query, for every period.** The oldest unpaid period's due date is its `RentAccrual`'s date when it has one, else its `AdjustmentDifference`'s date, else `AccrualSchedule.DueDateFor(period, contract.DueDay)`. The first branch is today's behaviour, unchanged; the last one covers the opening-balance month and anything else, so no unpaid period can ever drop an account again | Strong |
| D2 | **`MoraAccount` gains four plain fields: `ContractShortId`, `Tenants`, `Units`, `Status`.** Not an `AccountListing`: that type lives in `Collection` and carries the desk's readiness, and `Accounts` depends on nothing but `Leasing` today. A reminder can name the debtor from the row alone (spec Requirement 4) | Strong |
| D3 | **The text comes from the same code the cash desk search uses.** The listing construction inside `AccountDirectory.SearchAsync` moves into one internal loader **(new)**, `AccountListings.ReadAllAsync`, called by the directory and by the worklist. Two copies of "how a tenant's name is written" would drift; one cannot | Strong |
| D4 | **Ordering lives in the query, not in the view-model**: days overdue descending, then tenant name folded as the search folds it (`AccountSearch.Fold`, `Infra/AccountSearch.cs:21`), then account id so ties never shuffle | Strong on the place; the tenant tiebreak stays **weak**, as spec Decision 14 |
| D5 | **The port keeps its signature** `GetAccountsInMoraAsync(asOf, terms)`. The view-model always passes `IAgencyClock.Today`, which is how spec Decision 9 holds. Changing the signature would rewrite the eleven existing tests that spec Test 7 says must pass unchanged | Medium |
| D6 | **The query is registered only with valid recargo terms**, inside the same `if` that registers the desk (`SessionServices.cs:62-66`), through a new per-call wrapper **(new)** `PerCallMoraWorklistQuery`. No terms, no worklist, no view-model, a disabled entry: the desk's rule (design D16 of `ui-de-cobranza`) | Strong |
| D7 | **A row opens the cash desk through a callback**, as Recientes already opens Anular (`Desk/App.xaml.cs:175-182`). The desk needs a full `AccountListing` (`CashDeskViewModel.cs:188`), so `IAccountDirectory` gains `FindAsync(accountId)` **(new)**, which reads the listing fresh, readiness included. Spec Requirement 7 is kept, not dropped | Medium: it adds one method to a port |
| D8 | **The shell entry and the view ship together in M2, with Mateo's screens.** An entry with no screen behind it would be a button that does nothing; the view-model and its tests ship in M1 | Medium |

## Table of Contents

1. Technical approach
2. Domain
3. Infrastructure
4. Presentation and the shell
5. Testing strategy
6. Findings
7. Slices, sizes and test mapping
8. Threats and rollback
9. Parameters the spec left to design
10. Questions

## 1. Technical approach

The hole is one `return null` (`Infra/MoraWorklistQuery.cs:93-96`): a row is built only from a `RentAccrual` on the oldest
unpaid period. The fix replaces that lookup with D1's rule, which needs the contract's due day; the query does not load
contracts today (spec Finding F3). The listing loader of D3 already loads every contract with its parties and units
(`Infra/AccountDirectory.cs:38-46`), so the worklist reads contracts once, through it, and gets the due day and the
identifying text from the same read.

Every figure stays `ContractAccount`'s: `LedgerBalance`, `AmountOwed` and the frozen movement, exactly as
`Infra/MoraWorklistQuery.cs:103-115` reads them now. Nothing in `Domain/Accounts/ContractAccount.cs` changes (spec P3). An
opening-balance month projects no recargo because `RecargoProjectedForPayment` looks for a `RentAccrual` on the period
(`ContractAccount.cs:306`) and finds none; that is the desk's figure already, and spec Requirement 3 holds by construction.

## 2. Domain

### 2.1 `MoraAccount` (modified)

`Domain/Accounts/MoraAccount.cs` gains, after `ContractId`:

| Field | Type | Source |
|---|---|---|
| `ContractShortId` | `string` | `AccountListing.ContractShortId` |
| `Tenants` | `string` | `AccountListing.Tenants` |
| `Units` | `string` | `AccountListing.Units` |
| `Status` | `ContractStatus` (`Domain.Leasing`) | `AccountListing.Status` |

Lessors and readiness are left out: the spec does not ask for them, and readiness is the desk's question
(`IAccountDirectory.cs:8-11`). The doc comment's "There is no screen in this change" line is rewritten to point to the
Mora screen.

### 2.2 `IMoraWorklistQuery` (doc only)

The summary's "oldest debt first" gains the tiebreak of D4, and a line records spec Finding F4: asking about any day but
today is not supported.

### 2.3 `IAccountDirectory.FindAsync` **(new)**

`Task<AccountListing?> FindAsync(Guid accountId, CancellationToken ct = default)`: the listing the search would show for that
account, or null when there is none (an unknown id, or a contract loaded by mistake, which the search also hides,
`AccountDirectory.cs:69`). It reads and never writes, like the search.

## 3. Infrastructure

### 3.1 `AccountListings` **(new)**, `Infra/AccountListings.cs`

`internal static Task<IReadOnlyList<(AccountListing Listing, int DueDay)>> ReadAllAsync(InmobiliariaDbContext context, DateOnly today, CancellationToken ct)`.
It is lines 34-89 of `AccountDirectory.SearchAsync` moved as they are: accounts, contracts with parties and units, parties,
units, the frozen set, `recibo_numbering`, and the listing built per account. It returns every account, loaded-by-mistake
ones included, with the contract's due day beside each listing. The exact shape (a tuple, a small record) is the
implementer's.

`AccountDirectory.SearchAsync` becomes: the `IsSearchable` check, `ReadAllAsync`, the loaded-by-mistake filter it applies
today, then `AccountSearch.Select`, unchanged. `FindAsync` is `ReadAllAsync` filtered to the one account and to the same
loaded-by-mistake rule. Reading every account to find one is wasteful in principle and invisible at a hundred contracts;
spec Finding F5 already accepts the worklist being linear, and this is far cheaper than that.

### 3.2 `MoraWorklistQuery` (modified)

1. The materialisation loop stays exactly as it is (`MoraWorklistQuery.cs:36-48`).
2. After it, `AccountListings.ReadAllAsync(context, asOf, ct)` into a dictionary by account id, beside the existing read of
   accounts with movements (`:52-55`).
3. `Project` takes the listing and the due day. The `RentAccrual` lookup and its `return null` (`:90-96`) are replaced by D1:

   ```text
   dueDate = rent accrual on the period, its OccurredOn
          ?? adjustment difference on the period, its OccurredOn
          ?? AccrualSchedule.DueDateFor(period, dueDay)
   ```

   `daysOverdue <= 0` still returns null (spec Requirement 2, "the due date itself is not mora"). The old comment about
   opening balances carrying no period is false since `carga-inicial`'s opening-balance months carry one, and is replaced.
4. The order is D4's.

An account whose contract the loader did not return cannot happen (`contract_accounts.contract_id` is a foreign key), so a
missing entry throws rather than being skipped: a skipped row is the exact failure this change exists to remove.

### 3.3 `PerCallMoraWorklistQuery` **(new)**, `Infra/PerCall/`

Same shape as `PerCallAccountDirectory` (`Infra/PerCall/PerCallAccountDirectory.cs:16`): a context per call, a materialiser
built on it, the call forwarded. `PerCallAccountDirectory` gains `FindAsync` the same way.

### 3.4 `SessionServices` (modified)

Inside `if (recargo.Terms is { } terms)` (`SessionServices.cs:62`), beside the desk:
`services.AddSingleton<IMoraWorklistQuery>(_ => new PerCallMoraWorklistQuery(factory));`. The terms are not captured by the
wrapper: the port takes them per call, and the view-model passes the same `RecargoSettings.Terms` the desk was built with.

## 4. Presentation and the shell

### 4.1 `MoraViewModel` **(new)**, `Pres/Mora/`

Constructor: `IMoraWorklistQuery`, `RecargoTerms`, `IAgencyClock`, `IAccountDirectory`, and
`Func<AccountListing, Task> openAtDesk`.

| Member | What it does |
|---|---|
| `Rows` | `ObservableCollection<MoraRow>` **(new)**, replaced whole on every read, never patched (spec Requirement 6) |
| `LoadCommand` | Reads `GetAccountsInMoraAsync(clock.Today, terms)` through the `OperationRunner` every screen uses, so a failure becomes a message and no exception (spec Requirement 6, `desktop-shell`'s rule) |
| `IsLoading` | True while a read runs; the view shows it (spec Finding F5) |
| `IsEmpty` | True after a successful read with no rows; the view shows `MoraTexts.Empty` |
| `CoverageText` | `MoraTexts.Coverage`: rent, instalments and recargo, never tasa municipal, seguros or otros conceptos |
| `OpenCommand` | `FindAsync(row.AccountId)`, then `openAtDesk(listing)`; a null listing shows `MoraTexts.AccountGone` |

`MoraRow` formats one `MoraAccount`: tenant, units, short id, status through `Formats.Status` (as `AccountRow` does,
`Pres/CashDesk/AccountPickerViewModel.cs:118`), days overdue, amount owed, recargo and, when frozen, the frozen date. It
exposes no sum of the two amounts. `LoadCommand` runs when the screen is shown and when the operator presses refresh;
there is no timer (spec Decision 13).

`MoraTexts` **(new)** holds the Spanish texts. Their final wording waits for Mateo, as every S12 text does.

### 4.2 Shell and main window (M2)

`NavigationTarget.Mora` and `NavigationTitles.Mora = "Mora"` **(new)**. `ShellViewModel.BuildNavigation` adds the entry for
both roles after Anular un cobro, with the same `termsReason` the desk uses (`ShellViewModel.cs:36-45`). `App.xaml.cs` builds
the view-model only when `ICollectionDesk` resolves, as it does the desk (`App.xaml.cs:159-167`), with a callback that calls
`mainWindow.ShowScreen(NavigationTarget.CashDesk)` and then `cashDeskViewModel.ChooseAccountAsync(listing)`. The view itself
is Mateo's layout.

Where Mora sits relative to S12's five entries is decided when S12 adds them; until then it goes after Anular un cobro.

## 5. Testing strategy

Test-first per spec rule. Infrastructure tests use the existing `MoraWorklistQueryTests` fixture against `postgres:17.6`;
opening-balance months are seeded the way `GoLiveSetup` writes them, not by inserting movements by hand, so the test
proves what production will hold. The presentation tests use fakes of the three ports, as `CashDeskTestKit.cs` does.

- **RED first.** Tests 1, 2 and 4 are run against today's code and seen failing before the query is touched. If Test 1
  passes, spec Finding F1 is wrong and the work stops for the spec, as the spec says.
- **Guard proven.** After GREEN, D1's fallback is commented out and Tests 1, 2 and 4 are seen failing again.
- **Differential check.** Before and after, a dump of every cash desk figure for accounts covering every movement kind, and of
  every `AccountDirectory.SearchAsync` result for the same accounts, must match byte for byte (spec Requirement 3, plus D3's
  move). This is a scratch program in a `mktemp -d` folder, never committed, as S10's was.
- `dotnet ef migrations has-pending-model-changes` must stay clean: the change touches no model.

## 6. Findings

| # | Finding | Consequence |
|---|---|---|
| F6 | **The desk opens an account only from a full `AccountListing`** (`CashDeskViewModel.cs:188`), readiness included, and nothing could fetch one listing by id. | D7: `IAccountDirectory.FindAsync`. Without it, spec Requirement 7 would have to be dropped. |
| F7 | **Spec Test 10 asks the view-model to order the rows**, but D4 puts the order in the query, where a reminder also gets it. | Test 10's ordering part is written against the real query, in `Infrastructure.Tests`; the view-model test asserts that it keeps the port's order. The rest of Test 10 stays in `Presentation.Tests`. |
| F8 | **A contract loaded by mistake cannot appear.** Its cancellation writes a `Correction` per period so each nets to zero (`Domain/Collection/ContractCancellation.cs:94-99`), and `OldestUnpaidPeriod` nets per period (`ContractAccount.cs:364-372`). | Nothing to build. Recorded because D1 makes the query take every unpaid period, and this is why that is safe. |
| F9 | **The query's existing comment says an opening balance carries no period** (`MoraWorklistQuery.cs:77-81`), which stopped being true when opening-balance months got one. | Replaced in §3.2. A stale comment there is how F1 went unseen. |

## 7. Slices, sizes and test mapping

| Slice | What | Lines (forecast) | Docker | Supabase |
|---|---|---|---|---|
| **M1** | §2, §3, §4.1: the hole, the fields, the loader, `FindAsync`, registration, the view-model | ~350 with tests | **Yes** | No |
| **M2** | §4.2: the shell entry, the main window wiring and Mateo's view | ~150 | **Yes** for the full suite | No |

M1 runs a little over the proposal's 200–300, mostly tests: the loader move is lines that change place, and `FindAsync`
brings its own tests. It stays one PR. No migration in either slice.

| Spec test | Slice | Project | Note |
|---|---|---|---|
| 1, 2, 4 | M1 | Infrastructure.Tests | RED first, then the guard proven by reverting D1 |
| 3, 5, 6 | M1 | Infrastructure.Tests | |
| 7 | M1 | Infrastructure.Tests | The existing tests, unchanged; the one tie between equal days, if any, is checked against D4 |
| 8 | M1 | Infrastructure.Tests | Compared with `SearchAsync`'s result for the same account |
| 10 | M1 | Infrastructure.Tests (order) and Presentation.Tests (the rest) | F7 |
| 11, 12, 13 | M1 | Presentation.Tests | 13 with a fake directory and a fake callback |
| 14 | M1 | Infrastructure.Tests, `SessionServicesTests` | Resolves with valid terms, absent without them |
| 9 | M2 | Presentation.Tests, beside `ShellViewModelTests` | Ships with the entry (D8) |
| — | M1 | Infrastructure.Tests, `AccountDirectoryTests` | `FindAsync`: found, unknown id, loaded by mistake; the existing search tests unchanged |

## 8. Threats and rollback

| Threat | Guard |
|---|---|
| The loader move changes what the desk search shows | Existing `AccountDirectoryTests` unchanged, plus the differential dump of §5 |
| D1's fallback hides a period whose due date is wrong | It is `DueDateFor`, the rule rent itself uses (`AdjustmentDesk.cs:101` uses it the same way); Test 4 pins due days 10 and 5 |
| A figure changes for some account | Spec Test 6 and the differential dump; `ContractAccount` is untouched |
| Reading the list writes accruals | Unchanged behaviour (spec Decision 10); both roles hold the grants |

Rollback: revert the PR. There is no migration and no stored format change.

## 9. Parameters the spec left to design

| Parameter | Decided |
|---|---|
| How the query reads the due day and the text | D3, §3.1 |
| `MoraAccount` gains the fields or a second type carries them | D2: `MoraAccount` gains them |
| The view-model's name, loading state and refusals | §4.1 |
| Where Mora sits in the navigation | §4.2: after Anular un cobro until S12 |
| How the desk opens on an account | D7 |
| The Spanish texts and the layout | Wait for Mateo, as S12 |

## 10. Questions

None for Tomás and none for Nicolás. D5, D7 and D8 are where a different preference would change the plan.
