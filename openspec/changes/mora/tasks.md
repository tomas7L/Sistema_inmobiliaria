# Tasks: mora (the overdue-accounts worklist)

Turns `design.md` §7 (two slices, M1 and M2) into ordered, checkable work. It does not re-plan the change and does not edit
`spec.md` or `design.md`. Where applying exposes a gap, it is listed under **Issues found**.

Slice to phase map: M1 = Phase 1 (one PR now), M2 = Phase 2 (with `gestion-de-alquileres` S12's screens).

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, **Issues found** and **Deviations** | Everything inside the phases except the **Verification** table at the end of each. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 500 authored (additions + deletions, tests included): M1 about 350, M2 about 150, design §7 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Low: each slice is under it |
| Chained PRs recommended | No: one PR per slice, M2 waits on Mateo |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No

## Issues found

- **Follow-ups from the M1 gentle-ai review** (`review-81c8a8a04dbdfda3`, approved 2026-11-11, all non-blocking), taken into
  Phase 2 as task 2.0:
  - R2-001: `AccountListings.ReadAllAsync`'s `today` only dates the readiness the worklist never reads; say so in a comment.
  - R3-difference-due-date-nondeterministic: a period with no rent and several differences takes whichever difference EF
    loads first; take the earliest `OccurredOn`, with a test.
  - R3-mistaken-contract-row-unopenable: cannot happen today, since cancelling nets every period to zero (design F8) and a
    cancelled contract accrues nothing; pin it with a worklist test.
  - R3-open-callback-unguarded: `openAtDesk` runs outside `OperationRunner`; guard it when M2 wires `ChooseAccountAsync`.
- **Verifier advisory, fixed in M1:** a failed refresh kept the earlier rows or the empty state beside the error.
  `MoraViewModel.LoadAsync` now clears both; `AFailedRefresh_ClearsTheEarlierRowsAndTheEmptyState` (RED seen first).
- **Verifier advisory, recorded:** when one period holds both an opening balance and a difference, D1 takes the difference's
  date. The spec does not say which; the two only differ after a due-day correction.

## Deviations

- **1.2, spec Test 2 passes against today's code** (2026-11-11). A difference on a period whose rent was accrued, paid or not,
  already put the account on the list: the row was built from that rent accrual. Spec Finding F1's "difference-only period"
  half is narrower than stated: it drops an account only when the period has no rent accrual at all, which happens when that
  month was carried by an opening balance. Test 2 stays as written; `ADifferenceOnAPaidOpeningBalanceMonth_Appears` was added
  and fails against today's code. Its difference is dated the 12th on a contract whose due day is the 10th (as after a due-day
  correction), because a difference dated on the contract's current due day would read the same through either branch of D1,
  and the mutation that removes the difference branch would go unseen (1.8).
- **1.2, opening balances seeded with `ContractAccount.Append`, not "the way `GoLiveSetup` writes them"** (design §5). Nothing
  in `src` writes a monthly opening balance yet (`carga-inicial` will); `GoLiveSetup` only sets the cutover date and the
  starting number. The tests seed them as `MaterialiserCutoverTests.cs:187` does: a cutover contract, then one movement per month.
- **1.5, Test 6 compares with `ContractAccount.AmountOwedForPayment`**, the figure the desk's planner reads
  (`CollectionPlanner.cs:217`), not with `AmountOwed`, which the worklist reads. The two differ only for movements dated after
  the day asked or differences on a month not yet begun, neither of which exists as of today; Test 6 and the differential (1.9)
  confirm they are equal for every movement kind.
- **1.7, the view-model tests were written after the view-model**, not RED first. Compensated in 1.8: each of their rules was
  removed in turn and the matching test seen failing.

---

## Phase 1: The worklist and its view-model (M1, PR 1, est. about 350 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 1 to 8, 10 to 14, and the `FindAsync` tests. Requirements 1 to 4,
6, 7. Design anchors: D1 to D7, §2, §3, §4.1, §5, F6 to F9. Needs: nothing.

- [x] 1.1 **[Second inspection, read-only]** Anchors match: `MoraWorklistQuery.cs:77-96`, `MoraAccount.cs`, `AccountDirectory.cs:34-89`, `SessionServices.cs:62-66`, `CashDeskViewModel.cs:188`, `App.xaml.cs:175-182`. Baseline at `33dd8e2`: 2110 tests (Domain 723, Presentation 523, Infrastructure 864), 2109 passed, 1 failed, 0 skipped. The failure was `ContractCreationRoleTests.ALoginInBothRoles_SettingEmpleado_IsRefused` with `SocketException: Solo se permite un uso de cada dirección de socket`; it passed alone (6 of 6). `TIME_WAIT` was 4 before the run and 3,348 after it, not measured during it
- [x] 1.2 **[RED, Tests 1, 2 and 4]** In `tests/Inmobiliaria.Infrastructure.Tests/MoraWorklistQueryTests.cs`, opening-balance months seeded the way `GoLiveSetup` writes them. Seen failing against today's code, with the failure reason read. If Test 1 passes, stop: spec Finding F1 is wrong. Observed: Test 1, Test 4 (both due days) and the added difference test failed with `Assert.Single() Failure: The collection did not contain any matching items`; Test 2 passed (see Deviations); Tests 3 and 5 passed, as they pin existing behaviour
- [x] 1.3 **[Refactor under green]** `AccountListings.ReadAllAsync` in `src/Inmobiliaria.Infrastructure/Persistence/` (design §3.1); `AccountDirectory.SearchAsync` calls it. Existing `AccountDirectoryTests` pass unchanged
- [x] 1.4 **[RED then GREEN, `FindAsync`]** `IAccountDirectory.FindAsync`, `AccountDirectory`, `PerCallAccountDirectory`; tests: found, unknown id, loaded by mistake. Fakes implementing `IAccountDirectory` gain the method
- [x] 1.5 **[GREEN, Tests 1 to 8]** `MoraAccount` gains its four fields (design §2.1); `MoraWorklistQuery` per §3.2 (D1 due date, D4 order, the stale comment replaced); `IMoraWorklistQuery` doc (§2.2). Tests 3, 5, 6, 8 and the order part of Test 10 written RED first; the existing tests (Test 7) unchanged
- [x] 1.6 **[RED then GREEN, Test 14]** `PerCallMoraWorklistQuery` in `src/Inmobiliaria.Infrastructure/Persistence/PerCall/`, registered inside the terms `if` of `SessionServices`; `SessionServicesTests` resolves it with valid terms and finds none without them
- [x] 1.7 **[RED then GREEN, Tests 10 to 13]** `MoraViewModel`, `MoraRow`, `MoraTexts` in `src/Inmobiliaria.Presentation/Mora/`, `tests/Inmobiliaria.Presentation.Tests/MoraViewModelTests.cs` with fakes of the three ports. No view and no shell entry: M2
- [x] 1.8 **[Prove the guards by making them fail]** Each mutation run and reverted: D1's `DueDateFor` fallback removed → Tests 1, 4 (both), 6 fail; D1's difference branch removed → the added difference test fails; the tenant tiebreak reversed → the order test fails; the query registered outside the terms `if` → `InvalidRecargoTerms_...` fails; `Rows.Clear()` removed → the refresh test fails; `IsEmpty` never set → the empty-list test fails
- [x] 1.9 **[Differential]** Scratch test run against `33dd8e2` extracted to a `mktemp -d` folder and against the change, never committed and removed after: eight accounts covering every movement kind (rent only, opening months with payment, recargo charged, difference and correction, fee plan, frozen, ended owing a difference, no rate, loaded by mistake), dumping ledger, `AmountOwed`, `AmountOwedForPayment`, oldest unpaid period, and per period net and both recargo projections, plus six `SearchAsync` texts (24 listings). 60 lines each, **identical**
- [x] 1.10 **[Guardrails]** Build: exit 0, 0 Advertencia(s), 0 Errores. Suite: exit 0, 2129 passed (Domain 723, Presentation 528, Infrastructure 878), 0 failed, 0 skipped: baseline 2110 plus 19 new. `has-pending-model-changes`: exit 0, no changes. `TIME_WAIT` sampled every 20 s during the run: 9 before, peak 3,466
- [x] 1.11 **[Independent verification and gentle-ai review]** Verifier: no blockers, every in-scope requirement passes, four probes of its own passed (due day 31 in September and February, opening balance plus difference on one period, accent and case ties), workspace untouched; two advisories (see Issues found), one fixed with a test. Presentation suite after the fix: 529 passed, 0 failed. gentle-ai: `review-81c8a8a04dbdfda3`, risk medium (item 3 escalated: the desk's search moved onto the shared loader), lenses risk, readability, reliability, consent granted by Tomás, **approved**, acknowledged and burned; four non-blocking findings, taken into task 2.0
- [x] 1.12 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant. Handed over 2026-11-11

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit code 0; **Advertencia(s)** and **Errores** both 0. MSB3027 means the app is open |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + new tests |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: The Mora screen (M2, PR 2, est. about 150 lines)

**Docker: yes for the full suite. Supabase: no. Model change: no.** Spec tests: 9. Requirements 5, 6 (on screen), 7 (wiring).
Design anchors: D8, §4.2. Needs: Phase 1 merged, Mateo's screens.

- [ ] 2.0 **[Review follow-ups]** The four findings under Issues found, with tests
- [ ] 2.1 **[Second inspection, read-only]** Phase 1 landed; S12's navigation entries checked for where Mora sits
- [ ] 2.2 **[RED then GREEN, Test 9]** `NavigationTarget.Mora`, `NavigationTitles.Mora`, the entry in `ShellViewModel.BuildNavigation` with the desk's `termsReason`; `ShellViewModelTests`
- [ ] 2.3 **[Wiring]** `App.xaml.cs` builds `MoraViewModel` only when `ICollectionDesk` resolves, with the callback that shows the desk and calls `ChooseAccountAsync`; `MainWindow` hosts the view
- [ ] 2.4 **[View]** Mateo's layout; `MoraTexts` final wording
- [ ] 2.5 **[Guardrails]** As Phase 1
- [ ] 2.6 **[Handover]** As Phase 1

### Verification, PR 2 (Docker: yes)

As Phase 1, plus Tomás opens Mora once on the test database (optional, spec Preconditions).
