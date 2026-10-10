# Archive Report: cuenta-corriente

**Change**: `cuenta-corriente`
**Archived to**: `openspec/changes/archive/2026-10-10-cuenta-corriente/`
**Archive Date**: 2026-10-10
**Status**: CLOSED. Implementation complete and applied to production.

## What This Change Delivered

A running account per contract: an append-only ledger whose sum answers what is owed and why. It
accrues debts and reports balances. It takes no money, issues no receipt and sends no reminder —
collection and notifications own those, and call this capability to find out what to charge and who
to chase.

## Capability Outcomes

### NEW Capability

| Capability | Location | Requirements | Scenarios | Tests |
|------------|----------|--------------|-----------|-------|
| contract-account | `openspec/specs/contract-account/spec.md` | 14 | 34 | 1–33 |

### MODIFIED Capability

| Capability | What changed | Tests |
|------------|--------------|-------|
| lease-contract | `Contract` now carries its own due day, defaulting to 10. Mora is counted from it. | 2–6 (34–38 in this change) |

## Delivery

Six chained PRs on `ramatomy`, each merged to `develop` after review.

| PR | Commit | What shipped |
|----|--------|--------------|
| 1 | `8c98e1f` | `Domain/Accounts`: the movement kinds, the immutable movement, the aggregate |
| 2 | `70af7f9` | `RecargoMath` and `RecargoTerms` — the surcharge as a pure function |
| 3 | `8cfdf66` | `Contract.DueDay` and `AccrualSchedule` |
| 4 | `b52ce18` | Two tables, the `due_day` column, the migration, the trigger, the grants |
| 5 | `6be361b` | Lazy materialisation and `AmountOwed` |
| 6 | `2377e15` | The mora worklist |

Planning: `eba88a1` (explore), `9cb88d5` (proposal), `6902418` (spec), `ed8db54` (design),
`8955c48` (tasks), `37401bb` (ports moved to Domain before any code was written).

## Verification at Close

- Full solution Release build (`Inmobiliaria.sln`, Desktop included): **0 warnings, 0 errors**
- `dotnet test Inmobiliaria.sln --configuration Release`: **236 passed, 0 failed, 0 skipped**
  (141 domain, 95 infrastructure), Docker running so every Testcontainers test actually executed
- `dotnet ef migrations has-pending-model-changes`: model in step with the last migration
- Architecture, EF model and composition guards: all pass
- Migration `20261010145210_AddContractAccounts` applied to Supabase production and verified in its
  SQL editor

## Tasks

67 of 69 complete. The two open items are the user's, not an agent's, and are recorded as open
rather than closed:

- **H.2** — how a tenant's spreadsheet figure splits across contracts at load time. A data-entry
  decision for whoever loads the go-live data.
- **H.3** — Supabase Cron as an additional materialisation trigger. Explicitly optional and never a
  dependency: the lazy path is contained inside any correct scheduled one.

**H.1 closed**: the Supabase project was restored and the migration applied with the user's own
credentials on 2026-10-10.

## Decisions Worth Carrying Forward

1. **Lazy accrual, no scheduler.** A period is written the first time anything asks about the
   account. A scheduled job surviving a missed run must compute the missed periods anyway, so the
   lazy path is contained inside any correct scheduled one — and a ledger that silently stops being
   correct when a subscription lapses is a trap. Supabase Cron was investigated, works, and was
   rejected as a *dependency* for exactly that reason.
2. **Two figures, named apart.** The ledger balance is what is written; the amount owed adds the
   surcharge projected to the same date. One name for both was a real defect found in self-review.
3. **The novación freeze is a date, never a flag.** A boolean would have rewritten every past
   balance to whatever the freeze later turned out to be.
4. **The account outlives its contract**, and carries no closed flag. An ended contract simply has
   no further period coming due, which is what makes the flag unnecessary rather than merely
   absent.
5. **Recargo takes its terms from the obligation it measures.** No rate constant exists anywhere in
   the capability, guarded by a source-text assertion that was proven by injecting the literal.

## Honest Limits

- **Spec test 37 was not exercised against real rows.** "Every pre-existing contract carries
  `due_day = 10`" could not be, because the production database holds zero contracts. The column
  default and `NOT NULL` are what guarantee it, and there was nothing to migrate.
- **No screen exists.** Entering an opening balance and reading a balance are deliberately left to
  a later UI pass, and neither `IAccountMaterialiser` nor `IMoraWorklistQuery` is registered in DI
  yet — `IDueAdjustmentQuery` is not either. The wiring arrives with that pass.
- **The payment movement does not exist.** It belongs to the collection change. This capability
  owns only the rule that decides which period a payment settles, and the cases that reduce a
  balance are tested with corrections.
- **The mora worklist is linear in accounts**, not one statement, because it materialises before it
  reads. At this agency's scale that is a second or two. If it ever needs to be cheaper, the fix is
  to batch the materialisation, never to stop materialising.

## Defects Found and Deliberately Not Fixed Here

**`RentAdjustmentConfiguration` lacks `ValueGeneratedNever`.** Confirming a rent adjustment on a
contract loaded in an earlier save fails with
`23503: insert or update on table "rent_adjustment_index_values" violates foreign key constraint`.
EF marks the new adjustment `Modified` instead of `Added`, updates nothing, and the child row has no
parent. Proven by hitting it in this change's own test suite, not theorised. The fix is
schema-neutral — verified in PR 4 — but it belongs to the rent-adjustment capability and deserves
its own change with its own tests.

**`RentAdjustment.cs:76` claims to be "the single `decimal.Truncate` site in the whole pipeline".**
`AdjustmentProposal.cs:64` also truncates. Harmless — both truncate the same value to the same
result — but the comment is inaccurate.

## Deviations from Tasks

Each is recorded beside its own task in `tasks.md`:

- **Task 2.2** told the implementation to truncate through `RoundingRule.TruncateToWholePeso`. That
  helper does not exist: the enum is stored on `AdjustmentClause` and no code reads it. Followed the
  project's real pattern, `decimal.Truncate` at the edge.
- **Task 4.4** specified the accrual uniqueness index over every kind carrying a period. The design
  says "for accrual movements", and the broad form would have forbidden correcting one month twice
  and — worse — rejected a second payment against one period, which is precisely what a payment plan
  is. Shipped scoped to `kind = 'RentAccrual'`.
- **Tasks 5.1 and 6.1** placed the adapters in `Infrastructure/Accounts/`. That folder does not
  exist; adapters live in `Infrastructure/Persistence/` beside `DueAdjustmentQuery`.
- **Task 2.6** was shipped stronger than written. A reflection-only guard was tried first and is
  strictly weaker: it sees declared members, so it catches a `const` and misses a literal written
  inside a method.
- **Several task labels cite the wrong spec-test numbers.** All 38 tests are covered; only the
  labels were wrong, and they are corrected in place.

## Things This Change Proved by Breaking Them

Every guard below was watched failing before it was trusted:

- Injecting `0.02m` into `RecargoMath` fails the no-rate-constant guard.
- Making `Contract.DueDay` settable puts the EF model out of step with the last migration, which is
  what would have failed every Testcontainers test — checked both ways with
  `has-pending-model-changes`, which needs no database.
- Replacing the canon reconstruction with `contract.MonthlyRent` re-prices history and fails two
  tests.
- Neutralising the materialiser's pre-check forces the unique-violation path, and the catch handles
  it: one row, zero reported writes.
- Removing the worklist's materialisation loop fails four tests. The two asserting an account is
  *absent* still passed, because a broken worklist also returns absence — which is why the risk's
  own test had to be a presence assertion over an account with no movements.
