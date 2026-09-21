# Archive Report: Rent Adjustments by Index

**Change**: rent-adjustments  
**Archived**: 2026-09-21  
**Status**: CLOSED AND MERGED

## Final State Summary

The rent-adjustments change has been completed, verified, and merged to the main specification. All 55 implementation tasks were delivered across four sequenced PRs (#12, #14, #15, #16) plus two follow-up PRs (#17 CI-caught EF model fix, #18 verification fixes). Both critical issues discovered during verification were resolved in PR #18, which confirmed all test suites passing.

### Verification Outcome (as of 2026-09-18, confirmed closed 2026-09-21)

**Per orchestrator final state (post-PR #18):**
- `dotnet build Inmobiliaria.sln -c Release` → **0 warnings, 0 errors**
- `Inmobiliaria.Domain.Tests` → **52/52 PASS**
- `Inmobiliaria.Infrastructure.Tests` → **22/22 PASS** (0 skipped, real `postgres:17.6` via Testcontainers)
- **All prior CRITICAL issues CLOSED**

**Test Coverage**: 16 of 18 spec requirements fully proven. Tests 11 and 12 remain **scoped/partial** (see Known Limitations below).

### Artifacts Merged to Living Specification

| Domain | Action | Details |
|--------|--------|---------|
| economic-index | **Created** | New spec at `openspec/specs/economic-index/spec.md`. Five requirements defining index identity, published-value storage, manual entry, discontinuation, and successor resolution. All 5 tested in domain and integration layers. |
| rent-adjustment | **Created** | New spec at `openspec/specs/rent-adjustment/spec.md`. Thirteen requirements covering per-contract clauses, optionality, coefficient math, due-date derivation, worklist, pending-value handling, operator confirmation, truncation, decimal storage, reminder-readiness, late-adjustment handling, append-only history, and correction mechanics. Tests 1-10, 13-16 fully proven. |
| lease-contract | **Modified** | Spec at `openspec/specs/lease-contract/spec.md` extended with one ADDED requirement (contract may reference adjustment clause) and one MODIFIED requirement (share-percentage survival now explicitly covers confirmed RentAdjustment changes). New test scenario proves adjustment behavior. |

## Delivered Implementation

### Four Sequential Work Units (PRs #12, #14, #15, #16)

1. **PR #12 (Slice 1 — Domain/Indices, ~400–480 lines)**: `IndexPeriod`, `EconomicIndex`, `IndexValue`, `IndexResolver` with 8-hop limit. Pure domain, no database. All indexing and supersession tests included.

2. **PR #14 (Slice 2 — Domain/Leasing, ~450–540 lines)**: `AdjustmentClause`, `RentAdjustment`, `AdjustmentMath`, `AdjustmentProposal`, `AdjustmentSchedule`, `Contract` wiring. Coefficient math proven (averaging variations, not levels). Late confirmation, share survival, and truncation tested. Note: Slice 2 delivered whole at ~908 lines (vs. planned split), deliberately accepted as complete.

3. **PR #15 (Slice 3 — Infrastructure/EF, ~420–520 lines)**: Five new table configurations, new migration, append-only trigger at database level, schema constraint tests. Byte-for-byte verification of historical `20260913215911_InitialSchema` confirmed (unchanged). Note: Slice 3 came in ~9% over estimate, deliberately accepted as complete.

4. **PR #16 (Slice 4 — Adapter/Worklist, ~380–460 lines)**: `IDueAdjustmentQuery` port and `DueAdjustmentQuery` adapter implementing the read model. Worklist correctly filters to active contracts with clauses, surfaces missing index values, computes coefficient only when all values present.

### Two Follow-Up PRs

- **PR #17**: CI-caught EF model snapshot update (routine housekeeping).
- **PR #18**: Tests for both critical findings (index uniqueness and partial-index constraint), plus guard for `Contract.AttachAdjustmentClause` against silent reassignment.

## Critical Issues Resolved

Both critical findings from the verify report were addressed in PR #18:

1. **Untested unique index on `(EconomicIndexId, Period)`** ✓ CLOSED  
   Test: `SchemaConstraintTests.SameIndexAndPeriodTwice_RejectedByUniqueIndex`

2. **Untested partial unique index on active economic index names** ✓ CLOSED  
   Tests: `DuplicateActiveIndexName_RejectedByPartialUniqueIndex` and `DiscontinuedIndexFreesItsNameForASuccessor` (proves the partial scope that enables index supersession)

3. **WARNING also closed**: `Contract.AttachAdjustmentClause` now guards against reassignment (per design Decision 6 intent).

## Known Limitations Carried Forward

### Test Coverage Gaps (Honest Disclosure)

**Spec tests 11 and 12** ("a late adjustment charges nothing retroactively") are only **partially provable in this change**:
- Test 11: Confirmed late adjustment generates no charge for undercharged months — **provable only as "one adjustment appended, no other row"**; no billing exists to assert *no charge was created*.
- Test 12: Correct canon resumes from next period — **provable as "canon equals new canon afterward"**; no billing exists to assert what the next bill *would* be.

**The current-account change MUST re-assert both tests fully** once billing logic exists. This is not a failure; it is a deliberate, documented scope boundary matching the change definition. The technical mechanism (no retroactive effective date, correct canon forward) is proven; the financial consequence (no charge) awaits the billing change.

### Open Technical Decisions (Not Closed)

Three architecture decisions remain open in `openspec/config.yaml`:

- **`credential-exposure`**: Confirming an adjustment is the first canon-changing action; role-based access control remains cosmetic given the office PC environment. Flagged for users-and-roles change.
- **`scheduler-mechanism`**: The worklist is ready for consumption by a reminder/scheduler; this change does not schedule or send anything. Flagged for notifications change.
- **`pdf-library`**: Untouched by this change.

### Human Follow-Ups (Non-Code)

- **H.1 — Live Supabase Migration**: The new migration has NOT been applied to the live Supabase project. The user applies it himself with his own connection string (same pattern as change 1). Six new tables do not yet exist in production.
- **H.2 — Stale-Value Assumption**: Needs owner confirmation — does the operator enter the most recent *available* index value when the period is not yet published, or wait? Currently a team decision assumption.
- **H.3 — Truncate vs. Round**: Needs owner confirmation — inferred from one honorarios line (47,860 where exact is 47,860.80). Team decided to truncate; one peso either way is immaterial, so the decision was taken rather than left blocking.
- **H.4 — Re-flag credential-exposure**: At the users-and-roles change.
- **H.5 — Re-flag scheduler-mechanism**: At the notifications change.

## Design Document Discrepancy (Recorded)

**Finding**: `design.md`'s summary prose (Decision 3) states "five new tables" while its detailed entity listing includes six tables:
1. `economic_indices`
2. `index_values`
3. `adjustment_clauses`
4. `adjustment_clause_indices`
5. `rent_adjustments`
6. `rent_adjustment_index_values`

**Correct**: The implementation follows the detailed six-table list and is correct. The summary-prose count is wrong. **Recommendation**: Correct the prose in the design doc at the next review cycle.

## Archive Contents

```
openspec/changes/archive/2026-09-21-rent-adjustments/
├── proposal.md
├── specs/
│   ├── economic-index/
│   │   └── spec.md
│   ├── rent-adjustment/
│   │   └── spec.md
│   └── lease-contract/
│       └── spec.md
├── design.md
├── tasks.md (all 55 tasks marked [x])
└── verify-report.md
```

## Source of Truth Updated

The living specifications now include:

- `openspec/specs/economic-index/spec.md` — **NEW**, 5 requirements, fully tested
- `openspec/specs/rent-adjustment/spec.md` — **NEW**, 13 requirements (tests 1–10, 13–16 proven; 11–12 scoped)
- `openspec/specs/lease-contract/spec.md` — **UPDATED**, 1 ADDED + 1 MODIFIED requirement, new scenario for adjustment behavior

All delta specs from this change have been merged into the main specification. No other living specs in `openspec/specs/` were affected.

## Artifact Observation IDs (Engram Traceability)

| Artifact | Observation ID | Persisted |
|----------|---|---|
| Proposal | #24 | 2026-09-14 11:09:09 |
| Spec | #26 | 2026-09-16 12:14:29 |
| Design | #27 | 2026-09-17 18:06:31 |
| Tasks | *Engram: NOT found; filesystem: openspec/changes/archive/2026-09-21-rent-adjustments/tasks.md* | 2026-09-16 (initial version) |
| Verify Report | #30 | 2026-09-18 21:17:58 |
| Archive Report | *This file* | 2026-09-21 |

## SDD Cycle Complete

The rent-adjustments change has been fully:
- ✓ Proposed (change intent, scope, decisions)
- ✓ Specified (three capabilities, 20 requirements, 18 test cases)
- ✓ Designed (nine architecture decisions, 1,650–2,000 authored lines, four PR slices)
- ✓ Implemented (four chained PRs merged to develop)
- ✓ Verified (all tests passing, both critical issues resolved, honest disclosure of coverage gaps)
- ✓ Archived (change folder moved, delta specs merged to living specifications)

**Ready for the next change.**

---

**Archive date**: 2026-09-21  
**Archived by**: sdd-archive executor  
**Mode**: hybrid (filesystem + Engram)
