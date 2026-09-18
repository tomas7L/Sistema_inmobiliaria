# Apply Progress: Rent Adjustments by Index

## Scope of this batch

Slice 1 (`Domain/Indices`, PR 1) — tasks 1.1–1.9 — and Slice 2 (`Domain/Leasing` arithmetic +
`Contract` wiring, PR 2) — tasks 2.1–2.20 — are both complete. Slices 3–4 are untouched: no EF
configurations, migration, or worklist read model exist in this change.

## Slice 1 — Completed Tasks

- [x] 1.1 `IndexPeriod` readonly record struct (Year, Month, `AddMonths`, `IComparable<IndexPeriod>` with `<`/`>`/`<=`/`>=`)
- [x] 1.2 `EconomicIndex` (Id, Name, `DiscontinuedFrom?`, `SuccessorIndexId?`, `MarkDiscontinued` rejecting self-reference)
- [x] 1.3 `IndexValue` (Id, EconomicIndexId, Period, Level with `level > 0` guard, `Correct(newLevel)`)
- [x] 1.4 `IndexResolution` closed result hierarchy: `Resolved(EconomicIndex)` / `Unresolved(reason)`
- [x] 1.5 `IndexResolver.Resolve(index, period, catalog)`: walks `SuccessorIndexId` while `period >= DiscontinuedFrom`, 8-hop cap
- [x] 1.6 `IndexResolverTests` — spec test 15 (one successor hop; period before discontinuation), plus no-successor and hop-cap/cycle cases
- [x] 1.7 `EconomicIndexTests` — discontinuation without successor accepted; self-successor rejected; blank name rejected; discontinuation leaves stored `IndexValue`s unchanged
- [x] 1.8 `IndexValueTests` — zero/negative level rejected on construction and on `Correct`; `Correct` replaces level in place, id stable
- [x] 1.9 `ArchitectureGuardTests` re-run — passes; new `Indices` files add no EF Core/Npgsql/WPF reference to `Inmobiliaria.Domain`

## Slice 1 — Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/Inmobiliaria.Domain/Indices/IndexPeriod.cs` | Created | Value type: year+month, `AddMonths`, ordering operators |
| `src/Inmobiliaria.Domain/Indices/EconomicIndex.cs` | Created | Catalog entry, discontinuation + successor naming |
| `src/Inmobiliaria.Domain/Indices/IndexValue.cs` | Created | Published level per index/period, in-place correction |
| `src/Inmobiliaria.Domain/Indices/IndexResolution.cs` | Created | Closed `Resolved`/`Unresolved` result hierarchy |
| `src/Inmobiliaria.Domain/Indices/IndexResolver.cs` | Created | Pure successor-chain walk with 8-hop cap |
| `tests/Inmobiliaria.Domain.Tests/IndexResolverTests.cs` | Created | 4 tests: one hop, before-discontinuation, no-successor, cycle/hop-cap |
| `tests/Inmobiliaria.Domain.Tests/EconomicIndexTests.cs` | Created | 4 tests: no-successor accepted, self-successor rejected, values untouched, blank name rejected |
| `tests/Inmobiliaria.Domain.Tests/IndexValueTests.cs` | Created | 4 tests: zero/negative rejected (ctor and `Correct`), `Correct` keeps id stable |

Authored lines: 381 (5 domain files: 212 lines; 3 test files: 169 lines) — within the 400–480
PR1 budget (under, not over).

## Slice 1 — Deviations from Design

- **`IndexResolver.Resolve` signature.** Design's pseudocode and `tasks.md` both write
  `Resolve(index, period)`. That two-argument shape cannot actually walk a multi-hop successor
  chain: after the first hop, resolving the *next* index's own `DiscontinuedFrom`/
  `SuccessorIndexId` requires access to that index object, which a lone `EconomicIndex`
  parameter cannot provide. Implemented as
  `Resolve(EconomicIndex index, IndexPeriod period, IReadOnlyDictionary<Guid, EconomicIndex> catalog)`
  — the caller supplies every index the chain might touch, already loaded in memory. This keeps
  the function pure (design's explicit requirement: "a pure domain function, no
  infrastructure") while making the walk actually possible. Flagging per the instruction to
  follow the spec for *what* and the design for *how*, and note any disagreement: this is a gap
  in the design's *how* rather than a disagreement with the spec, so I resolved it in the
  direction the design's own prose argues for (in-memory chain walk, no infra) rather than
  inventing a hidden static lookup or a service dependency.
- Everything else matches `design.md` Decision 7 and the `economic-index` capability of
  `spec.md` as written.

## Slice 2 — Completed Tasks

- [x] 2.1 `CombinationRule` enum (`Single`, `Average`)
- [x] 2.2 `RoundingRule` enum (`TruncateToWholePeso`, one member)
- [x] 2.3 `AdjustmentClauseIndex` join entity — id-only, mirrors `ContractUnit`
- [x] 2.4 `AdjustmentClause` — `ContractId`, 1..N indices, `Combination` derived from index
      count, `IntervalMonths` 1–60, `RoundingRule`
- [x] 2.5 `AdjustmentKind` enum (`Regular`, `Correction`)
- [x] 2.6 `RentAdjustmentIndexValue` — snapshotted by value, no FK to the live `IndexValue`
- [x] 2.7 `RentAdjustment` — no public mutator/setter, factory-only via `Confirm(...)`,
      `(Kind == Correction) == (CorrectsAdjustmentId != null)` enforced
- [x] 2.8 `AdjustmentMath` — variation per index, rounded once to 6 places; coefficient is the
      average of the rounded variations (or the lone variation for `Single`); pending (null
      coefficient) on a missing value or a cross-index splice
- [x] 2.9 `AdjustmentProposal` — previous canon, per-index snapshot, combination, coefficient,
      both untruncated and truncated new canon, effective date, `MonthsLate`
- [x] 2.10 `AdjustmentSchedule.NextDueDate` — last effective date (or `StartDate`) + interval,
      forced to the first of the month
- [x] 2.11 `Contract.cs` modified: `AdjustmentClause?` navigation + `AttachAdjustmentClause`,
      `_adjustments` list + `Adjustments`, `ConfirmAdjustment(...)` — see deviation note below
      on where the single truncation site actually lives
- [x] 2.12 `AdjustmentMathTests` — spec tests 1 (avg of variations, not levels; worked example
      hits 517,500 exactly) and 2 (single-index skips averaging)
- [x] 2.13 `AdjustmentMathTruncationTests` — spec tests 3, 4, 5 (517,483.73→517,483;
      47,860.80→47,860, never 47,861; 517,483 stays 517,483, never lifted to 517,500)
- [x] 2.14 Added `ConfirmAdjustment_ThroughContract_TruncatesExactlyOnceWithNoProgressiveDrift`
      to the truncation test file — spec test 6 domain half, exercised through the real
      `AdjustmentMath` → `AdjustmentProposal` → `Contract.ConfirmAdjustment` pipeline with a
      repeating-decimal variation (8/7), proving no progressive rounding drift
- [x] 2.15 `AdjustmentMathTests` — spec test 8, IPC present/RIPTE absent leaves `Coefficient`
      null
- [x] 2.16 `ContractAdjustmentLateConfirmationTests` — spec tests 11/12, SCOPED as instructed:
      one adjustment appended and no other row, canon afterward equals the new canon; no
      billing assertion attempted
- [x] 2.17 `ContractShareSurvivesAdjustmentTests` — spec test 17, 60/40 split survives a real
      `ConfirmAdjustment` call and still sums to 100%
- [x] 2.18 `AdjustmentScheduleTests` — spec test 18, a 3-month interval is honoured, never
      assumed semiannual
- [x] 2.19 `AdjustmentMathTests` — extra test from Decision 7, a cross-splice (base resolves to
      index A, end to index B) is refused, not computed
- [x] 2.20 `ArchitectureGuardTests` re-run after the `Contract.cs` edit — passes

Also added `RentAdjustmentTests.cs` (not separately numbered in `tasks.md`, tied to the
invariant described in task 2.7): the `Kind`/`CorrectsAdjustmentId` binding is rejected in both
directions and accepted when consistent.

## Slice 2 — Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/Inmobiliaria.Domain/Leasing/CombinationRule.cs` | Created | `Single`/`Average` enum |
| `src/Inmobiliaria.Domain/Leasing/RoundingRule.cs` | Created | One-member `TruncateToWholePeso` enum |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentKind.cs` | Created | `Regular`/`Correction` enum |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentClauseIndex.cs` | Created | Join entity, id-only |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentClause.cs` | Created | Per-contract clause, derived `Combination` |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustmentIndexValue.cs` | Created | Snapshotted-by-value index contribution |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentMath.cs` | Created | Variation/coefficient/splice-refusal arithmetic |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentProposal.cs` | Created | Confirm-with-a-summary contract |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentSchedule.cs` | Created | Due-date derivation |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs` | Created | Append-only history entry, single truncation site |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modified | `AdjustmentClause?` nav, `_adjustments`, `ConfirmAdjustment` |
| `tests/Inmobiliaria.Domain.Tests/AdjustmentMathTests.cs` | Created | Spec tests 1, 2, 8, 19 (extra) |
| `tests/Inmobiliaria.Domain.Tests/AdjustmentMathTruncationTests.cs` | Created | Spec tests 3, 4, 5, 6 (domain half) |
| `tests/Inmobiliaria.Domain.Tests/ContractAdjustmentLateConfirmationTests.cs` | Created | Spec tests 11/12, scoped |
| `tests/Inmobiliaria.Domain.Tests/ContractShareSurvivesAdjustmentTests.cs` | Created | Spec test 17 |
| `tests/Inmobiliaria.Domain.Tests/AdjustmentScheduleTests.cs` | Created | Spec test 18 |
| `tests/Inmobiliaria.Domain.Tests/RentAdjustmentTests.cs` | Created | `Kind`/`CorrectsAdjustmentId` invariant |
| `openspec/changes/rent-adjustments/tasks.md` | Modified | Marked Slice 2 tasks 2.1–2.20 `[x]` |

Authored lines: 798 (10 domain files: 452 lines; 7 test files: 346 lines) + 51 lines changed in
`Contract.cs` = **849 total**, against the tasks.md/design.md estimate of 450–540 for PR2. See
Risks below — this is reported honestly rather than undercounted.

## Slice 2 — Deviations from Design

- **Where the single truncation site lives.** `design.md` Decision 4 says the one
  `decimal.Truncate` call lives inside `RentAdjustment.Confirm(...)`. `tasks.md` task 2.11
  instead labels `Contract.ConfirmAdjustment` itself as "the single `decimal.Truncate` site."
  These two documents disagree with each other (not spec vs. design — spec is silent on which
  method literally contains the call, only that truncation happens once, at confirmation).
  Per the instruction that `design.md` is "the authority on where truncation happens," I
  implemented the actual `decimal.Truncate` call inside `RentAdjustment.Confirm`, and
  `Contract.ConfirmAdjustment` calls that factory and then `ChangeMonthlyRent` with the
  already-truncated `NewCanon` — so `Contract.ConfirmAdjustment` is still, observably, the one
  path through which a truncated canon reaches `MonthlyRent`, honouring the *intent* of task
  2.11 even though the literal `decimal.Truncate(...)` token sits one call frame lower. Verify
  should treat "truncated exactly once, end to end" as the requirement, not the literal call
  site, given this recorded conflict.
- **`ConfirmAdjustment` signature.** `tasks.md` writes `ConfirmAdjustment(AdjustmentProposal)`.
  Every other id-bearing entity in this codebase (e.g. `Contract`'s own constructor) takes its
  `Guid id` from the caller rather than generating one internally, so
  `ConfirmAdjustment(Guid adjustmentId, AdjustmentProposal proposal, DateTimeOffset confirmedAt, AdjustmentKind kind = Regular, Guid? correctsAdjustmentId = null)`
  follows that existing convention instead of inventing `Guid.NewGuid()`/`DateTimeOffset.UtcNow`
  calls inside the aggregate. `kind`/`correctsAdjustmentId` are optional and default to a
  regular adjustment, so the common case still reads as a one-argument call plus id/timestamp.
- **`AttachAdjustmentClause` was added**, not explicitly named in `tasks.md`. Task 2.11 asks for
  an `AdjustmentClause?` navigation but does not name how it gets set; a private setter with no
  way to assign it would make the navigation permanently null, so a minimal attach method was
  added, validated against the clause's own `ContractId` (mirrors the pattern of every other
  mutator in `Contract.cs`, which validates before assigning).
- **Correction adjustments still call `ChangeMonthlyRent`.** Neither `spec.md` nor `design.md`
  states whether confirming a `Correction`-kind adjustment (correcting a stale index value used
  by a past confirmed adjustment) should update the contract's *current* live canon, or only
  append a corrected history row. This implementation applies the same append + `ChangeMonthlyRent`
  behaviour to both kinds, which is untested for `Correction` in this slice (integration test 14,
  Slice 3, is the first real exercise of a correction) — flagged as an open question for Slice 3.
- Everything else matches `design.md`'s arithmetic pipeline (Decision 4), splice rule
  (Decision 7), and the `rent-adjustment`/`lease-contract` capabilities of `spec.md` as written.

## Slice 2 — Issues Found

None in the implemented code. The line-count overage above is a delivery-process issue, not a
code defect.

## Remaining Tasks (not in this batch's scope)

- [ ] Slice 3 — EF configurations + migration (PR 3, tasks 3.1–3.16)
- [ ] Slice 4 — Worklist read model + adapter (PR 4, tasks 4.1–4.9)
- [ ] Human follow-ups H.1–H.5 (non-code)

## Work Unit Evidence (Slice 2)

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~Adjustment"` → 16/16 passed |
| Full domain suite | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release` → 52/52 passed (35 pre-existing through Slice 1 + 17 new in Slice 2) |
| Build | `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors=true`) |
| Architecture guard | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~ArchitectureGuard"` → 1/1 passed |
| Runtime harness | N/A — pure domain, no DB, no Testcontainers boundary in this slice |
| Rollback boundary | Revert the `Contract.cs` diff; delete every new `Leasing/Adjustment*.cs`, `Leasing/CombinationRule.cs`, `Leasing/RoundingRule.cs`, `Leasing/RentAdjustment*.cs` file and its matching test file; PR1 (`Indices/*`) is unaffected |

## Workload / PR Boundary

- Mode: chained PR slice (`stacked-to-main`, target branch `develop` per session preflight)
- Current work unit: Unit 2 — Clause + adjustment arithmetic + `Contract` wiring (PR 2)
- Boundary: starts from Slice 1's `Indices/*` (already merged) and ends with a complete, tested
  `AdjustmentClause` → `AdjustmentMath` → `AdjustmentProposal` → `RentAdjustment` →
  `Contract.ConfirmAdjustment` pipeline; zero EF/persistence surface added
- Estimated review budget impact: **849 authored lines against the 450–540 PR2 estimate — see
  Risks**. All 20 assigned tasks (2.1–2.20) required real, separately-reviewable entities; the
  estimate in `design.md`'s slicing table appears to have under-counted this unit relative to
  Slice 1 (5 domain files there vs. 10 here, plus the `Contract` wiring and 7 test files with
  the numbered spec-test evidence tasks 2.12–2.19 explicitly require).

## Status

9/9 Slice 1 tasks + 20/20 Slice 2 tasks complete (29/49 total across all four slices). Ready for
verify on Slices 1–2, or for a delivery decision on PR2's size before it is opened (see Risks).
