# Tasks: Rent Adjustments by Index

Domain vocabulary: canon = total monthly rent; locador = lessor; locatario = tenant; IPC/RIPTE/ICL =
economic indices (see spec.md). Delivery is sequential: each slice below is one PR to `develop`,
merged before the next begins (fixed by session preflight, not re-litigated here).

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | 1,650–2,000 authored (design's revised forecast, above the proposal's 1,200–1,600) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 400–480 → PR2 450–540 → PR3 420–520 → PR4 380–460 |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main (sequential merges; target branch is `develop`, per session preflight, not `main`) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

`Decision needed before apply: Yes` covers per-slice actual line-count confirmation and the two open
owner questions (H.2, H.3 below) — the chain strategy itself is already fixed and not being asked.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| 1 | Index catalog + supersession | PR 1 | `dotnet test tests/Inmobiliaria.Domain.Tests --filter FullyQualifiedName~Index` | N/A — pure domain, no DB | Delete `Domain/Indices/*` + its tests; nothing else depends on it yet |
| 2 | Clause + adjustment arithmetic + Contract wiring | PR 2 | `dotnet test tests/Inmobiliaria.Domain.Tests --filter FullyQualifiedName~Adjustment` | N/A — pure domain, no DB | Revert `Contract.cs` diff; delete new `Leasing/Adjustment*`, `Leasing/RentAdjustment*` files + tests; PR1 unaffected |
| 3 | EF configs + new migration + trigger | PR 3 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~SchemaConstraint` | `dotnet ef database update` then run tests; requires Docker Desktop for Testcontainers `postgres:17.6` | `dotnet ef database update 20260913215911_InitialSchema`, delete the new migration + 5 configuration files + `DbSet` registrations; PR1/2 domain untouched (never persisted before this) |
| 4 | Worklist read model + adapter | PR 4 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~DueAdjustmentQuery` | Requires Docker Desktop for Testcontainers `postgres:17.6` (PostgresFixture) | Delete `DueAdjustmentQuery.cs`, `IDueAdjustmentQuery.cs`, `DueAdjustment.cs` + integration tests; PR1-3 unaffected |

## Slice 1 — Domain/Indices (PR 1, est. 400–480 lines)

- [ ] 1.1 Create `IndexPeriod` readonly record struct (Year, Month, `AddMonths`) — `src/Inmobiliaria.Domain/Indices/IndexPeriod.cs`
- [ ] 1.2 Create `EconomicIndex` (Id, Name, `DiscontinuedFrom?`, `SuccessorIndexId?`, `MarkDiscontinued(period, successor?)` rejecting self-reference) — `.../Indices/EconomicIndex.cs`
- [ ] 1.3 Create `IndexValue` (Id, EconomicIndexId, Period, Level with `level > 0` guard, `Correct(newLevel)`) — `.../Indices/IndexValue.cs`
- [ ] 1.4 Create `IndexResolution` result type: `Resolved(EconomicIndex)` / `Unresolved(reason)` — `.../Indices/IndexResolution.cs`
- [ ] 1.5 Create `IndexResolver.Resolve(index, period)`: walk `SuccessorIndexId` while `period >= DiscontinuedFrom`, 8-hop cap — `.../Indices/IndexResolver.cs`
- [ ] 1.6 **[Spec test 15]** `IndexResolverTests`: one successor hop resolves; period before discontinuation still uses the original index — `tests/Inmobiliaria.Domain.Tests/IndexResolverTests.cs`
- [ ] 1.7 `EconomicIndexTests`: discontinuation with no successor still accepted; self-successor rejected
- [ ] 1.8 `IndexValueTests`: level ≤ 0 rejected; `Correct` replaces level in place, id stable
- [ ] 1.9 Guardrail check: run `ArchitectureGuardTests` — new files add no EF Core/Npgsql/WPF reference to `Inmobiliaria.Domain`

## Slice 2 — Domain/Leasing arithmetic (PR 2, est. 450–540 lines)

- [ ] 2.1 Create `CombinationRule` enum (`Single`, `Average`) — `src/Inmobiliaria.Domain/Leasing/CombinationRule.cs`
- [ ] 2.2 Create `RoundingRule` enum (`TruncateToWholePeso`, one member) — `.../Leasing/RoundingRule.cs`
- [ ] 2.3 Create `AdjustmentClauseIndex` join entity (AdjustmentClauseId, EconomicIndexId, Ordinal) — `.../Leasing/AdjustmentClauseIndex.cs`
- [ ] 2.4 Create `AdjustmentClause` (ContractId unique, 1..N `AdjustmentClauseIndex`, `CombinationRule` derived from index count, `IntervalMonths` 1–60, `RoundingRule`) — `.../Leasing/AdjustmentClause.cs`
- [ ] 2.5 Create `AdjustmentKind` enum (`Regular`, `Correction`) — `.../Leasing/AdjustmentKind.cs`
- [ ] 2.6 Create `RentAdjustmentIndexValue` (ReferencedIndexId, ResolvedIndexId, BasePeriod, BaseLevel, EndPeriod, EndLevel, Variation — snapshotted by value, no FK to the live `IndexValue`) — `.../Leasing/RentAdjustmentIndexValue.cs`
- [ ] 2.7 Create `RentAdjustment` (no public mutator/setter; factory bound to `Confirm(...)`; `(Kind == Correction) == (CorrectsAdjustmentId != null)`) — `.../Leasing/RentAdjustment.cs`
- [ ] 2.8 Create `AdjustmentMath`: variation `(end/base - 1) * 100m`, `decimal.Round(.., 6)` once per index; coefficient = average of the rounded variations; returns pending (no coefficient) when base/end periods resolve to different indices (splice refused) — `.../Leasing/AdjustmentMath.cs`
- [ ] 2.9 Create `AdjustmentProposal` (previous canon; per-index name/base/end period+level/variation; combination; coefficient; **both** untruncated and truncated new canon; effective date; months-late + "no retroactive charge" note) — `.../Leasing/AdjustmentProposal.cs`
- [ ] 2.10 Create `AdjustmentSchedule`: due date = last confirmed effective date + `IntervalMonths`, else `StartDate + IntervalMonths`; first-of-month guard — `.../Leasing/AdjustmentSchedule.cs`
- [ ] 2.11 Modify `Contract.cs`: add `AdjustmentClause?` navigation, private `_adjustments` list + `IReadOnlyCollection<RentAdjustment>`, `ConfirmAdjustment(AdjustmentProposal)` — **the single `decimal.Truncate` site**, calling the existing (unchanged) `ChangeMonthlyRent`
- [ ] 2.12 **[Spec tests 1, 2]** `AdjustmentMathTests`: IPC 8,000→9,440 / RIPTE 1,200,000→1,344,000 average to 15%, $450,000→$517,500 (averaging raw levels must fail this test); ICL-only clause skips averaging
- [ ] 2.13 **[Spec tests 3, 4, 5]** `AdjustmentMathTruncationTests`: 517,483.73→517,483; 47,860.80→47,860 (never 47,861); 517,483 stays 517,483, never lifted to 517,500
- [ ] 2.14 **[Spec test 6, domain half]** Assert intermediate arithmetic stays `decimal` and is truncated exactly once, at `Contract.ConfirmAdjustment` — no progressive truncation
- [ ] 2.15 **[Spec test 8]** `AdjustmentMathTests`: IPC present, RIPTE absent — coefficient stays null, never averaged from a partial set
- [ ] 2.16 **[Spec tests 11, 12 — SCOPED, partial coverage]** `ContractAdjustmentLateConfirmationTests`: confirming late appends exactly one `RentAdjustment` and no other row (11, partial only); canon afterwards equals the new canon (12). Explicitly assert nothing about retroactive billing — no billing exists yet; **the current-account change owns the real "no retroactive charge" assertion and must re-prove it**
- [ ] 2.17 **[Spec test 17]** `ContractShareSurvivesAdjustmentTests`: 60/40 two-unit contract keeps shares and 100% sum after `ConfirmAdjustment`
- [ ] 2.18 **[Spec test 18]** `AdjustmentScheduleTests`: a 3-month-interval clause becomes due on its own schedule, never assumed semiannual
- [ ] 2.19 **[Extra test, Decision 7]** `AdjustmentMathTests`: base period resolves to index A, end period resolves to index B (post-supersession) → refused, not computed
- [ ] 2.20 Guardrail check: run `ArchitectureGuardTests` again after the `Contract.cs` edit

## Slice 3 — EF configurations + migration (PR 3, est. 420–520 lines)

- [ ] 3.1 Create `EconomicIndexConfiguration`: partial unique index on `name` `WHERE discontinued_from IS NULL`; CHECK `successor_index_id <> id` — `src/Inmobiliaria.Infrastructure/Persistence/Configurations/EconomicIndexConfiguration.cs`
- [ ] 3.2 Create `IndexValueConfiguration`: UNIQUE `(economic_index_id, period)`; CHECK `level > 0`; `level numeric(18,6)` (index level, not money); `HasConversion` for `IndexPeriod` → `date` with CHECK `EXTRACT(DAY FROM period) = 1`
- [ ] 3.3 Create `AdjustmentClauseConfiguration`: UNIQUE `contract_id`; CHECK `combination IN ('Single','Average')`; CHECK `interval_months BETWEEN 1 AND 60`
- [ ] 3.4 Create `AdjustmentClauseIndexConfiguration`: composite key `(adjustment_clause_id, economic_index_id)`
- [ ] 3.5 Create `RentAdjustmentConfiguration`: `previous_canon`/`new_canon` `numeric(14,2)`; `coefficient numeric(12,6)`; CHECK `(kind='Correction') = (corrects_adjustment_id IS NOT NULL)`; CHECK day=1 on `effective_date`; `SetAfterSaveBehavior(PropertySaveBehavior.Throw)` on every property
- [ ] 3.6 Create `RentAdjustmentIndexValueConfiguration`: composite key `(rent_adjustment_id, referenced_index_id)`; `base_level`/`end_level` `numeric(18,6)` (levels, never `numeric(14,2)`); `variation numeric(12,6)`
- [ ] 3.7 Modify `InmobiliariaDbContext.cs`: register the five new `DbSet<T>`s
- [ ] 3.8 Generate migration: `dotnet ef migrations add AddRentAdjustments -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`; confirm the diff is exactly **five `CREATE TABLE`s, zero `ALTER TABLE`**
- [ ] 3.9 Hand-edit the generated migration's `Up`: append `migrationBuilder.Sql(...)` for the `BEFORE UPDATE OR DELETE ON rent_adjustments` trigger + function that unconditionally raises; `Down` drops trigger and function first — `.../Migrations/*_AddRentAdjustments.cs`
- [ ] 3.10 **Guardrail**: diff `20260913215911_InitialSchema.cs`, its `.Designer.cs`, and `InmobiliariaDbContextModelSnapshot.cs` — confirm the pre-existing migration file is byte-for-byte unmodified (the snapshot legitimately grows, the historical migration must not change)
- [ ] 3.11 **[Spec test 6, integration half]** `SchemaConstraintTests`: read `information_schema.columns`, assert `numeric(14,2)` on canon columns and `numeric(18,6)` on level columns
- [ ] 3.12 **[Spec test 13]** `SchemaConstraintTests`: raw SQL `UPDATE` and `DELETE` on `rent_adjustments` MUST fail — proves the append-only trigger at the database level
- [ ] 3.13 **[Spec test 14]** `SchemaConstraintTests`: correcting an `IndexValue.Level` after a confirmed adjustment leaves the original `rent_adjustments` row unchanged; a second correcting row coexists
- [ ] 3.14 **Guardrail**: confirm `PostgresFixture` still calls `Database.Migrate()` (never `EnsureCreated()`) and the image stays pinned to `postgres:17.6` — verification only, no fixture edit expected
- [ ] 3.15 **Guardrail**: `dotnet test Inmobiliaria.Core.slnf` still runs both test projects; no new test project was created this slice, so no `.slnf` edit is needed
- [ ] 3.16 **Guardrail**: grep the full diff for connection strings/secrets before opening the PR — none expected, Testcontainers supplies its own

## Slice 4 — Worklist read model + adapter (PR 4, est. 380–460 lines)

- [ ] 4.1 Create `MissingIndexValue` and `DueAdjustment` records — `src/Inmobiliaria.Domain/Leasing/DueAdjustment.cs`
- [ ] 4.2 Create `IDueAdjustmentQuery` port: `Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default)` — `.../Leasing/IDueAdjustmentQuery.cs`
- [ ] 4.3 Create `DueAdjustmentQuery` adapter: query starts **from** `adjustment_clauses` (never `contracts`), joins `contracts` filtered `Status == Active`; computes coefficient/proposed canon only when every referenced index value resolves, else fills `Missing` and leaves `Coefficient`/`ProposedCanon` null — `src/Inmobiliaria.Infrastructure/Persistence/DueAdjustmentQuery.cs`
- [ ] 4.4 **[Spec test 16]** `DueAdjustmentQueryTests`: a contract with no `AdjustmentClause` never appears in `GetDueAsync` — `tests/Inmobiliaria.Infrastructure.Tests/DueAdjustmentQueryTests.cs`
- [ ] 4.5 **[Spec test 9]** `DueAdjustmentQueryTests`: three contracts awaiting IPC 2026-08 are returned, each naming IPC + period 2026-08 in `Missing`
- [ ] 4.6 **[Spec test 7]** `DueAdjustmentQueryTests`: with the required value missing, `Coefficient`/`ProposedCanon` stay null and the previous canon is unaffected
- [ ] 4.7 **[Spec test 10]** `DueAdjustmentQueryTests`: once IPC 2026-08 is entered, none of the three contracts appears as waiting
- [ ] 4.8 Assert directly, in at least one test: `Coefficient is null ⟺ Missing.Count > 0`
- [ ] 4.9 **Guardrail**: confirm CI's `build` and `core` job names are unchanged in `.github/workflows/ci.yml` — this slice adds files, not jobs

## Human Follow-Ups (non-code, not assigned to any agent)

- [ ] H.1 **User applies the new migration to the live Supabase project** with their own connection string, the same way the previous change handled it — no agent connects to Supabase
- [ ] H.2 **Owner confirms the stale-value assumption**: does the operator enter the most recent published value when an index is late, or wait? Currently an unconfirmed TEAM DECISION assumption
- [ ] H.3 **Owner confirms truncate vs. round** for the whole-peso rule. Currently inferred from one honorarios line (47,860 vs. exact 47,860.80); the team decided to truncate, one peso either way changes nothing material
- [ ] H.4 Re-flag `credential-exposure` at the users-and-roles change — this change ships the first canon-changing action, unresolved here by design
- [ ] H.5 Re-flag `scheduler-mechanism` at the notifications change — nothing in this change schedules or sends anything

## Known Coverage Gap (carried forward, not closed)

Spec tests **11** and **12** ("a late adjustment charges nothing retroactively") are only
**partially** provable in this change (task 2.16): no billing exists yet, so there is nothing to
assert *no charge was generated*. Only "one adjustment appended, no other row" (11) and "canon
equals new canon afterward" (12) are assertable now. **The current-account change must re-assert
both tests for real** once billing exists. Do not report 11/12 as fully covered at verify time.
