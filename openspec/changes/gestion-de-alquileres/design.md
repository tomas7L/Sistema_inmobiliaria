# Design: gestion-de-alquileres (the missing rental screens)

Technical design for the `gestion-de-alquileres` change, from `spec.md` (merged in PR #89, Requirements 1 to 25, Decisions P1 to P14
and 15 to 41, Tests 1 to 92) and `proposal.md` in this folder. Written 2026-11-05. Nothing here is code; nothing here edits the spec.
Every existing type, member and file named was read and is cited `path:line`; anything new is marked **(new)**. Paths use
`Domain` = `src/Inmobiliaria.Domain/`, `Infra` = `src/Inmobiliaria.Infrastructure/Persistence/`, `Pres` = `src/Inmobiliaria.Presentation/`,
`Mig` = `Infra/Migrations/`.

**How complete the audit was.** Read in full: `Contract.cs`, `RentAdjustment.cs`, `AdjustmentProposal.cs`, `ContractAccount.cs`,
`AccountMovement.cs`, `CollectionPlanner.cs`, `ReciboBuilder.cs`, `VoidingPlanner.cs`, `CollectionDesk.cs`, `AccountDirectory.cs`,
`MoraWorklistQuery.cs`, `DueAdjustmentQuery.cs`, `AccrualSchedule.cs` and the configurations cited. Read in part: migrations, `GoLiveSetup.cs`,
`AccountMaterialiser.cs`, `App.xaml.cs`, `CashDeskViewModel.cs`, `PaperRows.cs`, and test files. **Not run:** no build, test or migration; the
live APIs were queried earlier this session and their shape is as seen then. Fixture effects are from searches, not from compiling, so S3 to
S10 may each find a few more tests to adjust.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** and **weak** rows are where you can change my mind |
| **§10 Findings** | What the code does that the spec did not say. F1 and F2 change two existing tests or methods |
| **§11 Slices** | Eleven PRs of logic plus the views, and what each needs |
| **§12 Migrations** | Seven, and exactly what you apply to production and when |

**Skip** §1 to §9 unless you want the detail: they are for whoever implements and for the second inspection at the start of each session.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | **`effective_date` keeps meaning the adjustment due date, and a new nullable `applies_from` holds the applied-from period.** No row is rewritten. A row with null keeps today's pricing rule, so existing rows and tests read the same | Strong |
| D2 | **`CanonFor` prices from `applies_from`.** A period takes the row with the latest `applies_from` not after it; ties go to the latest `confirmed_at`; before any, the earliest row's previous canon. That fixes spec Finding F2 for written and unwritten months alike | Strong |
| D3 | **The window is `end = due − K`, `base = end − N`, with `K` a new `adjustment_clauses.window_offset_months`, smallint, 0 to 3, default 1.** The default keeps existing clauses at today's window | Strong on the shape; the suggestions stay **weak** until N1 (spec Decision 15) |
| D4 | **Three pure Domain calculators**: `AdjustmentTiming` (applied-from), `AdjustmentDifference` (both formulas) and `AdjustmentSchedule` gaining the cutover. All **(new)** or extended, tested on the Linux job | Strong |
| D5 | **New ledger kind and recibo line kind, both `AdjustmentDifference`.** `ContractAccount` gains `DifferenceNet`, and the recargo base and the planner's rent subtract it. Nothing else about `PeriodNet` changes | Strong |
| D6 | **The charge is linked from the adjustment**: `rent_adjustments.difference_amount` (what was offered) and `difference_movement_id` (what was charged). Offered and refused is amount with no movement; not offered is both null. That is the "recorded choice" of spec R16 | Medium |
| D7 | **No unique index per period on the new kind.** Two late steps can land on one period (spec Test 55); their amounts add into the one recibo line `ux_recibo_lines_kind` allows | Strong |
| D8 | **A double confirmation is refused by two partial unique indexes**: one Regular row per `(contract_id, effective_date)` and one correction per `corrects_adjustment_id`. Confirming takes the account's lock and rebuilds the summary, as the desk does | Strong |
| D9 | **Cancelling is the enum member `LoadedByMistake`; the screen says "Cargado por error".** `AccrualSchedule` returns nothing for such a contract, unwritten periods included. Nothing new on the contract row | Strong |
| D10 | **Corrections reuse `LedgerDating.CorrectionDate`**, already the later of today and the reversed movement's date. It gives spec R19's "no credit" with no new rule | Strong |
| D11 | **Voidability comes from one shared function** extracted from `VoidingPlanner`. The database keeps its own check, as today | Strong |
| D12 | **A new append-only table `contract_events` holds reason, who and when** for cancelling, undoing and removing a party. Who and when are stamped by the database, as annulments are. A contract row cannot hold this: undoing clears its columns | Medium |
| D13 | **Removing a party is `contract_parties.removed_at`, with an EF query filter.** Neither role gains DELETE, and every existing read of `Contract.Parties` keeps working unedited. The partial tenant index ignores removed rows | Medium: a filter hides rows, so Test 80 pins it |
| D14 | **Replacing a unit and correcting shares are raw `UPDATE`s, not `Contract.SetUnitShares`**, which issues a DELETE neither role may run (Finding F1). A replaced unit keeps its row. Dropping one of several units is a cancel and create | Medium |
| D15 | **Clause indices change through one `SECURITY DEFINER` function, `app_replace_clause_indices`**, refusing once an adjustment exists. It is the same shape as the `app_*` functions, and neither role gains DELETE | Medium |
| D16 | **Terms are locked twice.** `contracts_terms_lock` (trigger) covers `start_date`, `nominal_end_date` and `due_day`. `monthly_rent` is guarded by the port and domain only, because a confirmed adjustment legitimately changes it | Strong |
| D17 | **The cutover INSERT rule is a new trigger raising `contracts_cutover_insert_admin_only`**, with the same `pg_has_role(current_user, ...)` test. It closes spec Decision 23, so an existing test that pins the limit is rewritten (Finding F2) | Strong |
| D18 | **Six new ports in Domain with typed refusals**, one per area, each with a per-call wrapper: the Users shape. No exception for anything an operator can cause | Strong |
| D19 | **Form rules are pure Domain functions**, separate from `Contract`'s constructor, which keeps tolerating what existing rules and tests use. The start-on-the-1st rule lives there (spec F9) | Strong |
| D20 | **Search reuses `AccountSearch` in memory; no extension.** No migration in this repository creates one, and the agency has hundreds of rows at most | Strong |
| D21 | **HTTP lives in Infrastructure behind `IIndexValueSource` (new)**, using the framework's `HttpClient`, with no new package. A fake in tests means nothing touches the network | Strong |
| D22 | **Timeout 10 seconds; addresses in `appsettings.json`, section `IndexSources`.** A new API address needs a config edit, a new format needs a release | Medium; **weak** on the 10 |
| D23 | **The plausibility warning fires beyond 25% per month elapsed since the nearest earlier value.** Real movement seen was 1.7% to 3.6% a month, so it catches a slipped digit and not a small typo | **Weak** on the number |
| D24 | **Eleven logic slices, then views.** Each is mergeable alone, because nothing reaches an operator until its view exists; every slice whose Risk names an item gets an independent verifier: S3, S4, S5, S7, S8, S9, S10 and S11 | Medium |
| D25 | **Migrations are additive and applied before the new version is installed.** The installed version keeps working against the new schema, since nothing it reads is removed | Strong |
| D26 | **A database trigger writes `index_value_corrections`, not the port.** The trigger reads the level it really overwrote, even when two people correct at once, and no code path can skip it. Inserting runs as the logged-in role, as on `contract_events`, so both roles hold `INSERT`: someone using a database client with their own login could add a false row, under their own name only. Answers spec Decision 42 | Medium |

## Table of Contents

1. Technical approach
2. Domain: parties, units, contracts
3. Domain: adjustments and the difference
4. Infrastructure and ports
5. The index source button
6. Presentation
7. Desktop and the shell
8. Testing strategy
9. Spec claims checked against the code
10. Findings
11. Slices, sizes and test mapping
12. Migrations
13. Threats and rollback
14. Parameters the spec left to design
15. Questions

---

## 1. Technical approach

```
Views (Desktop, S12) ──> view-models (Presentation) ──> ports (Domain) <── per-call adapters (Infrastructure)
                                                          │
                       pure rules: PartyIdentifiers, ContractDraftRules, AdjustmentTiming, AdjustmentDifference,
                                   ContractCancellation, IndexEntryRules (Domain, Linux CI)

IAdjustmentDesk.Confirm ── account lock ── rebuild summary, compare ── movement + adjustment + canon, one transaction
AccrualSchedule ── reads applies_from and LoadedByMistake ── used unchanged by the desk and the materialiser
```

Same structure as the Users screen and the desk: refusals as values, adapters translating `PostgresException` by SQLSTATE and constraint name
(`DeskErrorMap`, `CollectionDesk.cs:453-475`; `SetupErrorMap`, `GoLiveSetup.cs:161-169`), one context per call
(`PerCallDueAdjustmentQuery.cs:21-22`), wired in `SessionServices.cs:26-54`. `Pres` references Domain only, which
`PresentationArchitectureGuardTests.cs:14-22` enforces.

## 2. Domain: parties, units, contracts

### 2.1 Parties

- `PartyIdentifiers` **(new)**, `Domain/Parties/`: `NormalizeDni`, `NormalizeCuil` (digits only), `IsValidDni` (6 to 8), `IsValidCuil` (11 digits; check digit
  by weights 5,4,3,2,7,6,5,4,3,2, where 11 − sum mod 11 gives the digit, 11 means 0, 10 is invalid). Empty means absent, stored null.
- `Party.CorrectIdentity(first, last, dni, cuil)` **(new)**, beside `UpdateContactInfo` (`Party.cs:42`). The properties already have private setters (`:11-14`).
- `IPartyRegistry` **(new)**: `SearchAsync`, `CreateAsync(draft, acceptNoIdentifier)`, `CorrectAsync`. `PartyResult` carries a refusal and the holder's name.
  `PartyRefusal`: `NameMissing`, `DniInvalid`, `CuilInvalid`, `DniTaken`, `CuilTaken`, `NoIdentifierNotAccepted`. Duplicates are read from `ix_parties_dni` and `ix_parties_cuil`
  (`PartyConfiguration.cs:16-24`), with the holder looked up after the refusal. No model change.

Existing rows may hold dotted values, since nothing normalized them before. There are none in production by the proposal's statement; §12 gives the check.

### 2.2 Units

`IUnitRegistry` **(new)**: `ListAsync` (with availability), `CreateAsync`, `RelocateAsync`, using `PropertyUnit`, `ParkingUnit` and `Unit.Relocate` (`Unit.cs:38`). Availability is one query:
a unit is unavailable when a `contract_units` row joins a contract in `Active` or `PendingTermination`. `UnitListing` includes the covering contract's tenant and move-out date, which
contract creation needs. Address warnings compare folded text in memory. No model change.

### 2.3 Contracts

- `ContractDraft` **(new)** and `ContractDraftRules.Check(draft, today)` **(new)**: every refusal of spec R7 and R8 except those needing data. New refusals are values; `Contract`'s constructor
  (`Contract.cs:119-169`) is unchanged except that it now validates cutover through `CheckCutoverBounds` **(new)**, a part extracted from `CheckCutoverDate` (`:227-247`).
- `Contract.Correct(start, nominalEnd, rent, dueDay, plan, account)` **(new)**: refuses once `account.Movements.Count > 0`, the same test as `:184`. `StartDate`, `NominalEndDate` and `DueDay` go from `{ get; }`
  to `{ get; private set; }` (`:26-27`, `:49`). They are already named by `ContractConfiguration.cs:51-52,61-65`, so the model does not change; **`has-pending-model-changes` proves it**.
- `Contract.SetHonorariosPercentage(decimal?)` **(new)**: 0 to 100, at any time. An entered rate cannot return to null through the form.
- `Contract.GiveNotice` (`:414-425`) and `End` (`:430-445`) stay. Date bounds are in `ContractLifecycleRules` **(new)**, since Domain has no clock. `End` newly throws on `LoadedByMistake`, so only `Cancel` can set it.
- `Contract.UndoNotice()` **(new)**: `PendingTermination` to `Active`, both dates null.
- `Contract.UndoEnd()` **(new)**: refused when `EndReason` is `LoadedByMistake`; back to `PendingTermination` if `NoticeGivenDate` is set, else `Active`, with end date and reason null.
- `Contract.Cancel(today)` **(new)**: from any state, refused when already cancelled. `EndReason = LoadedByMistake`, `Status = Ended`, and `ActualEndDate` keeps an earlier end or becomes today.
- `EndReason.LoadedByMistake` **(new)** after `EndReason.cs:9`. The check list is a literal (`ContractConfiguration.cs:20-23`), edited with its migration.

`ContractParty` gains `RemovedAt` **(new)**. `Contract.RemoveParty(partyId, role, at)` **(new)**: a tenant or lessor only with no movement, a codeudor at any time, and never the last tenant or lessor unless the same operation adds a replacement. `AssignParty`
(`Contract.cs:337-353`) restores a removed row with the same person and role instead of adding a second one, because the key is `(contract, party, role)` (`ContractPartyConfiguration.cs:21`).

### 2.4 Cancelling: `ContractCancellation.Plan` **(new)**

Pure, like `VoidingPlanner`. Input: account, contract, the contract's live collections with their recorded date and recibo numbers, reason, today, role.

1. Empty or spaces-only reason: `ReasonRequired`. Already cancelled: `AlreadyCancelled`.
2. For each live collection, `CollectionRules.VoidingWindowOf(age)` **(new)**, extracted from `VoidingPlanner.cs:128-136`, which now calls it. `Free` or `AdminOnly` refuses with `MustVoidFirst`, naming the recibos and whether an Admin is needed. `Closed` does not block.
3. A movement is reversed when its period is still owed (`PeriodNet(period) > 0`) or it has no period and is a `RecargoFrozen`. Reversed kinds: `RentAccrual`, `OpeningBalance`, `ContractualFeeInstalment`, `RecargoCharged`, `AdjustmentDifference`, `RecargoFrozen`, each for its net after earlier corrections.
4. Each reversal is `AccountMovement.Correction(id, -net, LedgerDating.CorrectionDate(today, movement.OccurredOn), movement.Id, movement.Period)` (`AccountMovement.cs:172`, `LedgerDating.cs:41-42`).

A period holding a live payment nets to zero, so it is left alone (spec Test 76). Corrections never name a payment, so `HasLivePaymentFor` (`ContractAccount.cs:359-363`) still says it stands.

Result: `CancellationPreview(Corrections, AmountStoppedBeingOwed, RetainedPayments, Refusal)`. The adapter takes `AccountRowLock` (`AccountMaterialiser.cs:150`), reloads, plans again and compares, then writes corrections, the contract update and the event in one transaction, as `VoidAsync` does (`CollectionDesk.cs:168-224`).

### 2.5 What excludes a cancelled contract

| Reader | How | Evidence |
|---|---|---|
| Accrual | `AccrualSchedule.IsCancelled` **(new)**: `PeriodsDue` and `PeriodsBegun` return empty, `IsScheduled` returns false, so unwritten earlier periods never accrue | `ActualEndDate` alone would still accrue them (`AccrualSchedule.cs:31,46`) |
| Worklist | Already: `Ended` is filtered, and stays so under D25 of the spec | `DueAdjustmentQuery.cs:31` |
| Availability | Already: only `Active` and `PendingTermination` cover a unit | §2.2 |
| Desk picker | `AccountDirectory` drops listings whose contract is cancelled; other ended contracts stay | `AccountDirectory.cs:38-41,68-84`; amends `ui-de-cobranza` D29 |
| Collection | Already: no scheduled period, every other period nets to zero, so `NothingPayable` | `ContractAccount.cs:396-412` |
| Go-live list | `GoLiveSetup` omits cancelled contracts | `GoLiveSetup.cs:48-50` |
| Mora | Already: balance zero gives no row | `MoraWorklistQuery.cs:82-86` |
| Recibos already issued | Unchanged and readable, since `IReciboReader` does not read contract state | spec R23 |

## 3. Domain: adjustments and the difference

### 3.1 The two dates

`RentAdjustment` gains `AppliesFrom` (`DateOnly?`), `DifferenceAmount`, `DifferenceMovementId` **(new)**, all set through `Confirm` (`RentAdjustment.cs:80-112`). `AdjustmentProposal` gains an optional `appliesFrom`
and drops nothing: `MonthsLate` (`AdjustmentProposal.cs:21`, no production caller) becomes derived in the summary only.

`rent_adjustments` is append-only, so no existing row can be backfilled. Hence nullable, the same reasoning as `ConfirmedBy` (`RentAdjustmentConfiguration.cs:65-69`). `ck_rent_adjustments_effective_date_first_of_month` (`:25-27`) stays and now only ever receives the due date, which `AdjustmentSchedule` already floors to the 1st (`AdjustmentSchedule.cs:23`).

`AccrualSchedule.CanonFor` (`AccrualSchedule.cs:198-226`) becomes:

- `Starts(a)` = `a.AppliesFrom` when present, else the old comparison on `a.EffectiveDate <= dueDate`. Null rows behave exactly as today, including `ContractAdjustmentLateConfirmationTests.cs`.
- In force: the rows that have started by the period, ordered by start, then by `ConfirmedAt`. The last wins.
- None started: the earliest row by the same order gives its `PreviousCanon`, else `MonthlyRent`.

Checked against the spec: unwritten months keep the old canon (R15); two steps on one period take the last confirmed (Test 48); in the two-late-steps example, August reads 100,000 and September 150,000; a correction from January leaves November and December at the original's figure (Test 91).

### 3.2 `AdjustmentTiming.AppliesFrom(contract, account, confirmedOn)` **(new)**

Start at the first of `confirmedOn`'s month. While the period's due date is on or before `confirmedOn`, or `account.IsChargedFor(period)` (`ContractAccount.cs:449-452`), move a month forward. The result is never before the adjustment's month, because the worklist shows it only once due. `IsLate` is `appliesFrom > effectiveDate`.

### 3.3 `AdjustmentDifference` **(new)**

`Regular(contract, account, adjustmentMonth, appliesFrom, previous, new)`: the count of periods `p` from `adjustmentMonth` up to and excluding `appliesFrom` where `AccrualSchedule.IsChargedPeriod(contract, p)` **(new, public)** holds (scheduled, after the cutover) and no `OpeningBalance` carries `p`, times `new − previous`. Null when not positive. It compares with each step's own previous canon and not the charged amount, which is what prevents double counting (Test 55).

`ForCorrection(chain, ...)`: the chain is the corrected row back to its root. For each counted period up to the correction's `appliesFrom`, the baseline is the `NewCanon` of the latest chain row whose start is on or before that period, else the root's `PreviousCanon`; the amount is the sum of `corrected − baseline`. For one correction this **is** the spec's two-stretch formula (R18, 330,000 in its scenario), and it stays right for a correction of a correction. Null when not positive.

### 3.4 Confirmation

`IAdjustmentDesk` **(new)**: `PreviewAsync(contractId)`, `ConfirmAsync(preview, chargeDifference)`, `PreviewCorrectionAsync(contractId)`, `ConfirmCorrectionAsync(preview, chargeDifference)`, `HistoryAsync(contractId)`.

A correction's row reuses the corrected row's `EffectiveDate`, so `DueAdjustmentQuery`'s `Max(EffectiveDate)` (`:44-49`) keeps the calendar with no change. Its `PreviousCanon` is the corrected row's, so `previous × coefficient = new` stays reproducible.

`Contract.ConfirmAdjustment` (`Contract.cs:315-330`) newly refuses an `Ended` contract and, when `AppliesFrom` is given, one that is not a first of month or precedes the adjustment's month. **It does not check** that the previous canon equals `MonthlyRent` or that a correction names the latest row, because existing fixtures pass arbitrary values (`ContractTestFactory.cs:39-52`). The adapter enforces both by rebuilding the summary inside the transaction and comparing it (`AdjustmentRefusal.PreviewStale`), and by `CorrectsLatest`. The database backs it with D8.

Reversing an unpaid charge uses §2.4's correction shape. "Unpaid" is `!HasLivePaymentFor(period)`. A paid charge offers no new one.

### 3.5 Schedule and worklist

`AdjustmentSchedule.NextDueDate` (`AdjustmentSchedule.cs:12-24`) gains optional `cutoverDate`. With no last effective date and a cutover: the first `start + k × interval`, `k ≥ 1`, strictly after the cutover, floored to the 1st. Existing calls are unchanged.

`DueAdjustmentQuery`: status filter `Active` or `PendingTermination` (`:31`); projects `CutoverDate`; `endPeriod = due − K` (`:71`); `DueAdjustment` gains `IndexValues` with a default of empty, from the `mathResult.IndexValues` it now discards (`:86-88`). The missing-value logic is untouched (R13's `spec.md:91`).

The desk's warning needs no change: `AdjustmentWarning.For` filters whatever the query returns (`AdjustmentWarning.cs:14-32`), so a `PendingTermination` contract starts warning. That replaces `ui-de-cobranza/design.md:578` (C4) as spec Decision 25 accepts.

### 3.6 The difference on the ledger and the recibo

| Member | Change | Proves |
|---|---|---|
| `MovementKind.AdjustmentDifference` **(new)** | Factory `AccountMovement.AdjustmentDifference(id, amount, period, dueOn)`, positive only, dated on the period's due date | R19 |
| `ContractAccount.DifferenceNet(period[, asOf])` **(new)** | Sum of the kind on the period and of corrections naming those movements | — |
| `RecargoProjectedFor` (`ContractAccount.cs:226`), `RecargoProjectedForPayment` (`:278`) | `owing` subtracts `DifferenceNet` | R16, Test 52 |
| `PeriodNet` (`:248-249`, `:258-259`) | **Unchanged**: it answers "owed or settled", which must include the charge | R19, voiding |
| `IsChargedFor` (`:449-452`) | **Unchanged**: lists rent and opening balance, so the charge does not stop a period accruing | Test 55 |
| `CollectionPlanner.FiguresFor` (`CollectionPlanner.cs:415-421`) | `rent = PeriodNet − heldInstalment − heldDifference + newRent`; `Total` adds it | Test 66 |
| `FiguresFor` (`:394-397`) | Before returning null: no accrual, no scheduled period, and `DifferenceNet > 0` gives figures with no rent | R19 ended contract |
| `MovementsToWrite` (`:331-333`) | Same branch: charge date is the charge's own, no accrual written, avoiding `.Single` throwing | R19 ended contract |
| `ReciboLedgerFigures` (`ReciboBuilder.cs:27-45`) | `ForRentPeriod` gains `difference`; `ForDifferenceOnly` **(new)** has null rent | R20 |
| `HonorariosBase` (`:68`) | `SaldoDeudor ?? (Rent + Recargo + Difference)`; the instalment stays out | Tests 65 |
| `ReciboBuilder.Build` (`:105-111`) | Adds the line | R20 |
| `ReciboLineKind.AdjustmentDifference` **(new)** after `Recargo` | Only relative order is validated (`ReciboDraft.cs:53`), and recibos store text and position, so issued ones are unaffected | Test 67 |
| `ReciboLineCatalog.IsLedgerDerived` (`ReciboLine.cs:19-23`) | Adds it; `AppearsOn` already defaults to both copies (`:29-34`) | Test 65 |
| `ReciboLineLabels.For` (`ReciboLineLabels.cs:9-20`) | "Diferencia de ajuste" | spec D20 |
| `PaperRows` (`PaperRows.cs:60-64`) | No change: a kind with no fixed row prints on an added row | spec D20 |
| `CollectionPreview` | Gains the months covered, from `contract.Adjustments` linked by `DifferenceMovementId`; part of equality | R20 |
| `DeskTexts.cs:19` | The label names the difference too | R19 |

Voiding needs no change: `VoidingPlanner.cs:139-149` reverses only the payment and its recargo, so the period owes rent and charge again (Test 64). Paying again is rent plus charge by the `FiguresFor` line above.

`AmountOwedForPayment` (`ContractAccount.cs:296-314`) sums every movement whatever its date, so a charge attached to November would inflate the desk's "previous amount owed" while October is collected (Finding F8). It therefore leaves out the charge, and any correction naming it, while its period has not begun as of the received date. From the 1st of that month it counts, the same moment `PayabilityOf` allows paying it (`:391-394`), so the desk's figures agree. Spec R19's dated scenario reads `LedgerBalance` (`:157-158`), which honours the date.

## 4. Infrastructure and ports

| Port (Domain, **new**) | Adapter (`Infra`, **new**) | Notes |
|---|---|---|
| `IPartyRegistry` | `PartyRegistry` | §2.1 |
| `IUnitRegistry` | `UnitRegistry` | §2.2 |
| `IContractRegistry` | `ContractRegistry` | Create, list, detail, correct, set clause, notice, end, undo, cancel, remove party, replace unit, suggest rate |
| `IIndexRegistry` | `IndexRegistry` | List, add, correct, affected adjustments |
| `IIndexValueSource` | `IndexSources/*` | §5 |
| `IAdjustmentDesk` | `AdjustmentDesk` | §3.4 |

Each is registered in `SessionServices.cs:39-45` with a `PerCall*` wrapper, except that `IIndexValueSource` holds no context.

**Creating (R7).** One `SaveChangesAsync` inside a transaction: `Contract`, its parties, units, optional clause and `ContractAccount`. `ContractAccounts` is insertable by both roles (`20261010145210_AddContractAccounts.cs:137`). The unit-availability check runs inside that transaction. The rate suggestion is the lessor's contract with the latest `StartDate` and a rate; there is no creation timestamp.

**One tenant (R7, D34 of the spec).** Filtered unique index `ux_contract_parties_one_tenant` on `contract_id`, `WHERE role = 'Tenant' AND removed_at IS NULL`. A search of tests found no raw insert of a second tenant: `SchemaConstraintTests.cs:368-372,391` uses `Codebtor` and `Garante`, and `ContractPartyAssignmentTests` is domain-only.

**Cutover on insert (R21).** `contracts_cutover_insert_guard` `BEFORE INSERT` `WHEN (NEW.cutover_date IS NOT NULL)`, not `SECURITY DEFINER`, like `reject_cutover_change_after_movement` (`20261023131755_HardenCutoverDateToAdmin.cs:18-28`). A superuser passes, so the migration owner and fixtures do; proving the limit needs a real login role, as `CutoverTriggerTests.cs` does.

**Terms lock (D16).** `contracts_terms_lock` `BEFORE UPDATE OF start_date, nominal_end_date, due_day`, the shape of `contracts_fee_plan_lock` (`20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:130-151`), raising `contracts_terms_locked`. Neither `contracts_fee_plan_lock` nor `contracts_cutover_lock` changes. `ck_contracts_end_reason` changes (§12). `ck_contracts_status` does not, and no constraint ties `end_reason` or the dates to the status (`ContractConfiguration.cs:14-23`), so undoing an end needs none.

**`contract_events` (D12).** Property-bag entity like `recibo_lines` (`ReciboLineConfiguration.cs:20-57`): `id`, `contract_id`, `kind` (`Cancelled`, `NoticeUndone`, `EndUndone`, `PartyRemoved`), `reason`, `detail` (what was undone, the amount reversed or the party), `recorded_by`, `recorded_at`. A check requires a non-blank reason except on `PartyRemoved`. `BEFORE INSERT` stamps `recorded_at := now()` and `recorded_by := current_user`, as `stamp_recibo_annulment` does (`20261015204506_AddCollectionsAndRecibos.cs:296-347`). Append-only by reusing `reject_recibo_table_mutation` (`:205-229`). `GRANT SELECT, INSERT` to both roles.

**Contract list.** All in memory after one query, as `AccountDirectory.cs:35-46` does. The query filter of D13 applies to the `Include` in every reader; `ContractRegistry` uses `IgnoreQueryFilters` only when correcting.

**Seed (R11).** Raw `INSERT ... SELECT gen_random_uuid(), name WHERE NOT EXISTS (... name = ... AND discontinued_from IS NULL)`, respecting `ix_economic_indices_name_active` (`EconomicIndexConfiguration.cs:34-37`). `HasData` would conflict with the row already on the test database. `Down` removes nothing, because values may reference the rows.

**Index values.** `IndexEntryRules` **(new)**, Domain: refuses a future month and non-positive level, and computes the plausibility warning (D23). More than six decimals is refused, since the column is `numeric(18,6)`. `IndexValue.Correct` (`IndexValue.cs:32`) is reused. `AFTER UPDATE OF level` on `index_values`, `WHEN` the level really differs, inserts into `index_value_corrections` (`id`, `index_value_id`, `previous_level`, `new_level`, `recorded_by`, `recorded_at`): stamped by `stamp_contract_event`, append-only by `reject_recibo_table_mutation`, `GRANT SELECT, INSERT` to both roles (D26). Affected adjustments are `rent_adjustment_index_values` rows where `resolved_index_id` matches and `base_period` or `end_period` is the period (`RentAdjustmentIndexValue.cs:15-20`), with the old level.

## 5. The index source button

`IIndexValueSource.FetchAsync(indexName, period, ct)` **(new, Domain)** returns `IndexFetch`: `Found(level)`, `NotPublishedYet`, `Unreachable(timedOut)`, `SourceFailed(serverError)`. Only ICL and IPC have a source; `Supports(indexName)` is what hides the button for RIPTE.

| Source | Request | Response seen 2026-11-05 | Parsing |
|---|---|---|---|
| ICL | `GET {Bcra}/monetarias/40?desde=YYYY-MM-01&hasta=YYYY-MM-01` | `results[0].detalle[] = {fecha, valor}`, for example `2026-07-01`, `34.24`, including a Sunday and 1 January | The row for that exact `fecha`. None: `NotPublishedYet`. A row with another date, or a non-positive value: `SourceFailed` |
| IPC | `GET {DatosGob}/series/?ids=148.3_INIVELNAL_DICI_M_26&start_date=YYYY-MM-01&limit=1&format=json` | `data[] = [date, value]`, for example `["2026-08-01", 12276.766]` | The row dated that month. None, or only later months: `NotPublishedYet`. An earlier month or non-positive value: `SourceFailed` |

The exact IPC combination was queried on 2026-11-07 and works. **ICL answers a date after today with HTTP 400**, and a date with no row with 200 and an empty `detalle`, so the view-model never asks for a future month. Values are read as `decimal`, never floating point.

Failures map as: `HttpRequestException` to `Unreachable`, and a timeout to `Unreachable` with `timedOut`; a non-success status to `SourceFailed` with `serverError`, and invalid JSON, an unknown charset or missing fields to `SourceFailed` without it. That gives Test 84 its four messages. Nothing throws out of the adapter. The request has no client data and no credentials, and none of the existing keys go near it. `HttpClient` is created once with a 10-second timeout. Calls are `async`, so `OperationRunner.RunAsync` (`OperationRunner.cs:68`) keeps the window responsive.

`IndexSourceSettings` **(new)**, read like `RecargoSettings`, with addresses in `appsettings.json` under `IndexSources`. The configuration guard that fails on anything credential-shaped (`appsettings.json:7`) is not affected, since these are public addresses. Invalid addresses read as a failing source: unlike recargo, they close nothing but the button.

Storing goes through `IIndexRegistry`, so the checks and the plausibility warning are the typed path's. The view-model never overwrites: a stored level that differs is shown beside the source's, pointing to correct.

## 6. Presentation

New folders under `Pres`: `Parties/`, `Units/`, `Contracts/`, `Indices/`, `Adjustments/`. One view-model per screen on the `UsersViewModel` pattern (`UsersViewModel.cs:17`), each with `OperationRunner`, refusal text, and a re-read after every action.

- `ContractFormViewModel`: the form, with the cutover section offered only to an Admin; the role is passed in, and the database still decides.
- `ContractDetailViewModel`: detail and history, notice, end, undo, cancel; each opens a summary, then requires a reason where the spec does.
- `IndexLevelParser` **(new)**: strict Argentine format, `1.915.878,76`, using an invariant rule and not the machine's culture.
- `AdjustmentsViewModel`: worklist, summary, the choice defaulting to no, the post-confirmation notice reminder.

Refusal text is Spanish, one `OperatorText` per refusal, with no refusal reaching the generic error (Test 71).

## 7. Desktop and the shell

`NavigationTarget` gains `Parties`, `Units`, `Contracts`, `Indices`, `Adjustments` (`NavigationEntry.cs:4-11`). `ShellViewModel.BuildNavigation` adds them **before** the `if (role == UserRole.Admin)` at `ShellViewModel.cs:48-52`, so the Admin gating is not copied. `ShellViewModelTests.cs:42,56` pin the exact lists and are updated with it. The entries are added only in S12, so no earlier slice shows an empty screen. `App.xaml.cs:148-183` and `MainWindow.xaml.cs:18-36` gain the wiring. Views and the theme dictionary wait for Mateo's mockups (spec P8, P11).

## 8. Testing strategy

Pure rules go in `Inmobiliaria.Domain.Tests`, gating the Linux job. View-models go in `Inmobiliaria.Presentation.Tests` with fakes. Adapters and every database rule go in `Inmobiliaria.Infrastructure.Tests`, by raw SQL against `postgres:17.6` as a real login role where a role matters. `Inmobiliaria.Desktop` holds no tests (`CLAUDE.md`).

Nothing touches the network: source adapters are tested with a fake `HttpMessageHandler` replaying the shapes in §5, including malformed ones.

Every slice closes with the suite with Docker running, `dotnet ef migrations has-pending-model-changes`, and the count of errors, warnings and skipped tests read from the totals and not by pattern. Each guard is first seen failing. Test 45 must fail on today's `CanonFor`, and Test 52 must fail if the new kind joins the recargo base.

## 9. Spec claims checked against the code

| # | Claim | Checked |
|---|---|---|
| C1 | F2: a late confirmation overcharges unwritten months | True: `AccrualSchedule.cs:208-211`, `DueAdjustmentQuery.cs:87` |
| C2 | F3: a charge would earn recargo and read as rent | True: `ContractAccount.cs:226,278`; `CollectionPlanner.cs:415` |
| C3 | F4: the schedule ignores the cutover | True: `AdjustmentSchedule.cs:20-23` |
| C4 | F7: both roles lack DELETE | True for the tables named: `20260924202549_AddUsersAndRoles.cs:157-162` |
| C5 | R19: a Correction can be dated with the charge | True, already: `LedgerDating.cs:41-42` |
| C6 | R23: nothing accrues after cancelling, including unwritten periods | False today; §2.5 adds it |
| C7 | R16: a confirmed correction keeps the calendar | True once it reuses the date: `DueAdjustmentQuery.cs:44-49` |
| C8 | R25: both sources exist | True as queried on 2026-11-05 (§5); the exact IPC query combination is untested |
| C9 | R20: a kind with no paper row prints on an added row | True: `PaperRows.cs:60-64` |
| C10 | R18: adjustments hold a copy | True: `RentAdjustmentIndexValue.cs:6-11` |

## 10. Findings

| # | Finding | What this design does |
|---|---|---|
| F1 | **`Contract.SetUnitShares` cannot run in production.** It clears `_units` (`Contract.cs:363`), which EF sends as a DELETE, and neither role holds DELETE on `contract_units` (`20260924202549_AddUsersAndRoles.cs:157`). No production code calls it today, so nothing has failed. Spec R9 needs units and shares correctable | D14: raw `UPDATE`s, under the deferred share-sum trigger (`20260913215911_InitialSchema.cs:229`) |
| F2 | **A test pins the limit the spec closes.** `CutoverTriggerTests.cs:162`, `AnEmpleadoInsertsAContractWithADate_AndCannotChangeIt`, inserts as an Empleado with a date and expects success | S3 rewrites it to expect `contracts_cutover_insert_admin_only`. The "cannot change it" half moves to a contract inserted as superuser |
| F3 | **`adjustment_clause_indices` has the same problem as F1**, and correcting a clause from two indices to one needs a removal (`20260924202549_AddUsersAndRoles.cs:159`) | D15 |
| F4 | **The spec says "no rent line" for a difference-only period, and the paper prints one at 0.** The stored recibo has no rent line, as R19 asks, but `PaperRows.cs:48-52` prints every modelled row, 0 when absent, by `ui-de-cobranza` Decision 35. An opening-balance month already prints the same way | Kept, so the paper stays one layout. **Spec wording only; no spec change proposed** |
| F5 | **The next change, mora, must read the new kind.** `MoraWorklistQuery.cs:90-96` drops an account whose oldest unpaid period has no accrual, so a difference-only period on an ended contract shows no row | Out of this change; note for the mora spec |
| F6 | **The spec's Preconditions table names slices S1 to S6 from the proposal**, and this design numbers S1 to S12 (`spec.md`, "Preconditions") | §11 is the current table; the spec is not edited |
| F7 | **R15's "previous canon" means the chain's, not always the row's.** With two late steps, July is priced 100,000 though July's own `PreviousCanon` is 120,000: January's step had not started | §3.1 follows the spec's scenarios and Test 55. Wording only |
| F8 | **The desk's "previous amount owed" ignores dates.** `AmountOwedForPayment` sums every movement (`ContractAccount.cs:300`), so a charge attached to a month not yet begun would read as owed today, and an operator answering "how much do I owe" could quote it early. Paying it early was already impossible (`:391-394`) | §3.6 leaves it out until its month begins, pinned by one test in S10 beyond the spec's Tests |

No spec statement proved unimplementable.

## 11. Slices, sizes and test mapping

Advisory, about 400 authored lines per slice. Each is merged before the next starts. **Docker** is needed for every slice's full suite; the column says what else.

| Slice | Content | Spec tests | Estimate | Migration | Risk |
|---|---|---|---|---|---|
| **S1 Personas** | `PartyIdentifiers`, `IPartyRegistry`, adapter, view-model | 1-7 | ~350 | None | None |
| **S2 Propiedades** | `IUnitRegistry`, availability, view-model | 8, 9 | ~250 | None | None |
| **S3 Crear contratos** | Rules, `IContractRegistry` create, list, detail; tenant index; cutover INSERT rule; `CutoverTriggerTests` rewrite | 10-22, 68, 69 | ~500 | M1 | Item 2 (who may set a cutover): verifier |
| **S4 Corregir, avisar, terminar** | `Correct`, rate, notice, end, codeudor removal, `contract_events`, terms lock | 23-29, 80 | ~400 | M2 | Item 1 (changes stored contract terms): verifier |
| **S5 Cancelar y deshacer** | `LoadedByMistake`, accrual guard, `ContractCancellation`, shared voiding window, undo, picker and go-live exclusion | 72-79, 89, 90 | ~450 | M3 | Item 1 (erases debt, irreversibly): verifier |
| **S6 Índices** | Seed, correction record, `IIndexRegistry`, rules, parser, view-model | 30-34, 94, 95 | ~700 | M4, M4b | None |
| **S7 Traer índices** | `IIndexValueSource`, two adapters, settings, button logic | 82-88 | ~250 | None | Item 6 (no test sees the live API): verifier, plus one manual press of each button against the live source |
| **S8 Cláusula y calendario** | Offset, window, cutover calendar, `PendingTermination`, clause correction | 35-38, 40, 56, 81 | ~350 | M5 | Item 1 (decides when rent moves): verifier |
| **S9 Confirmar ajustes** | `applies_from`, `CanonFor`, `AdjustmentTiming`, `IAdjustmentDesk`, double-confirmation indexes, summary, history | 39, 41-48 | ~450 | M6 | Item 1 (what is charged): verifier |
| **S10 La diferencia** | Ledger and recibo kinds, §3.6, the choice | 49-55, 61-67, 92 | ~450 | M7 | Item 1 (what is owed), item 3 (`PeriodNet` consumers): verifier |
| **S11 Correcciones** | From index and history, chain formula, reversal | 57-60, 91 | ~300 | None | Item 1: verifier |
| **S12 Pantallas** | Theme, views, shell entries, restyling. **Waits for Mateo**, and splits per screen | 70, 71 | ~950 | None | None |

Spec requirements by slice: R1-R3 in S1; R4, R5 in S2; R6-R8, R21 in S3; R9, R10 in S4; R23, R24 in S5; R11, R12 in S6; R25 in S7; R13, R17 in S8; R14, R15 in S9; R16, R19, R20 in S10; R18 in S11; R22 in S12.

**Total about 5,000 authored lines**, more than the proposal's 3,800: it did not count cancelling, undoing and the button, which the spec added afterwards. S1, S2 and S6 are independent of each other, and S7 needs S6, because the button stores through `IIndexRegistry`. S3, S4, S5 chain, as do S8, S9, S10, S11. S8 needs S4, since Test 81 ends a contract and creates another. S10 needs S5, which S10 extends so that cancelling also reverses the new kind; Test 92's correction half closes in S11. Test 71 is proven per slice and audited at S12.

Slices S1 to S11 reach no operator until S12's views exist. If Mateo runs long, Tomás may choose plain views for some screens and restyle them later (proposal §7).

The specs under `openspec/specs/` are updated when the change is archived, as before.

## 12. Migrations

All additive. A model change and its migration ship in one PR. Each is applied to the test database, smoke-tested, then applied to production by Tomás, **before** installing a version that needs it; the assistant never handles the password.

| # | Migration **(new)** | Slice | EF model | What it does | Existing data |
|---|---|---|---|---|---|
| M1 | `AddContractPartyRemovalAndGuards` | S3 | Yes | `contract_parties.removed_at`; `ux_contract_parties_one_tenant`; cutover INSERT trigger | None rewritten. **Fails if a contract has two tenants** |
| M2 | `AddContractEventsAndTermsLock` | S4 | Yes | `contract_events` with stamp and append-only; `contracts_terms_lock` | None |
| M3 | `AddLoadedByMistakeEndReason` | S5 | Yes | Widens `ck_contracts_end_reason` | None |
| M4 | `SeedEconomicIndices` | S6 | No | Adds missing IPC, RIPTE, ICL | Adds up to three catalog rows |
| M4b | `AddIndexValueCorrections` | S6 | Yes | `index_value_corrections`, with stamp, append-only and the trigger on `index_values` (D26) | None |
| M5 | `AddClauseWindowOffset` | S8 | Yes | `window_offset_months`, default 1 **in the database only**, since on the model EF would take 0 as unset; check 0 to 3; `app_replace_clause_indices`, with `search_path` ending in `pg_temp` and revoked from Supabase's API roles; clause lock, which waits on the contract's row so a write cannot slip in beside an adjustment being written | Existing clauses read 1, today's window |
| M6 | `AddAdjustmentAppliesFrom` | S9 | Yes | `applies_from`, with first-of-month check; both unique indexes | None rewritten. **Fails on duplicate Regular rows** |
| M7 | `AddAdjustmentDifference` | S10 | Yes | Widens both kind checks and `ck_account_movements_period_required`; adds a sign check; `difference_amount`, `difference_movement_id` with a unique index | None |

**None changes an existing row.** The proposal says no real contracts are loaded in production (§8), but that was not checked against the live database here. So before M1 and M6, Tomás runs these on production, expecting no rows:

```sql
SELECT contract_id FROM contract_parties WHERE role = 'Tenant' GROUP BY 1 HAVING count(*) > 1;
SELECT contract_id, effective_date FROM rent_adjustments WHERE kind = 'Regular' GROUP BY 1, 2 HAVING count(*) > 1;
```

The installed version keeps working after each migration: it names only columns that remain, and new columns are nullable or defaulted. **The limit:** once a version that writes a new enum value runs (`LoadedByMistake`, `AdjustmentDifference`), an older version on another PC cannot read that row, so every PC at the agency must be updated together from M3 on. `AddUsersAndRoles` set default SELECT privileges only (`20260924202549_AddUsersAndRoles.cs:148`), so M2's `GRANT INSERT` is explicit.

## 13. Threats and rollback

| Threat | Mitigation |
|---|---|
| Wrong rent charged after a late confirmation | §3.1, Test 45 seen failing first, month-by-month scenarios |
| Debt erased on the wrong contract | Summary with name and amount, reason, `contract_events`. **Remains possible and final** (spec D35); the event is what lets anyone see it happened |
| Two contracts on one unit by two operators at once | The check is in the transaction but no database rule enforces it. Accepted for two users at one agency; it would show on the availability list |
| A query filter hides a tenant from a reader | The filter only hides rows with `removed_at`; recibos need a tenant and refuse without one (`CollectionDesk.cs:420-424`) |
| A wrong index value from a source | Shown and confirmed first; range and month checks; plausibility warning |
| A source changes or disappears | Own message, manual entry unaffected; BCRA already retired v3.0 |
| Security-definer function misuse | `app_replace_clause_indices` sets `search_path`, touches one clause's indices, refuses after an adjustment; `EXECUTE` to the two roles only |
| A real client's data in fixtures | Fictional names only; the repository is public |

Rollback: until real contracts exist, reverting PRs and running `Down` loses no agency data. After that, migrations move forward only, because `rent_adjustments` and `account_movements` are append-only (proposal §8). S7 can be reverted alone at any time. M4's `Down` removes nothing.

## 14. Parameters the spec left to design

| Parameter | Settled |
|---|---|
| How the applied-from period is stored | D1 |
| Whether R9's lock is also a trigger | D16: yes, except `monthly_rent` |
| How a double confirmation is refused by the database | D8 |
| Accent-insensitive search without an extension | D20 |
| Plausibility threshold | D23 |
| Timeout and where addresses live | D22 |
| Exact Spanish wording and the recibo row's position | Wording when writing each `OperatorText`; the row is an added one, after the fixed rows (§3.6) |

## 15. Questions

### For Tomás

None open.

### For Nicolás

No new ones. N1 to N4 stay in `spec.md`. N1 and N2 change only form suggestions and which day the ICL button asks for, each a small edit. Spec Decisions 15 and 41 hold until then.
