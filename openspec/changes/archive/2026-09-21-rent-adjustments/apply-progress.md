# Apply Progress: Rent Adjustments by Index

## Scope of this batch

Slice 1 (`Domain/Indices`, PR 1) — tasks 1.1–1.9 —, Slice 2 (`Domain/Leasing` arithmetic +
`Contract` wiring, PR 2) — tasks 2.1–2.20 —, and Slice 3 (EF configurations + migration, PR 3) —
tasks 3.0–3.16 — are all complete. Slice 4 (worklist read model + adapter) is untouched.

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

## Slice 3 — Completed Tasks

- [x] 3.0 Deleted the four `modelBuilder.Ignore<...>()` calls from `InmobiliariaDbContext.OnModelCreating`
- [x] 3.1 `EconomicIndexConfiguration` — partial unique index on `name` `WHERE discontinued_from IS NULL`; CHECK `successor_index_id <> id`
- [x] 3.2 `IndexValueConfiguration` — UNIQUE `(economic_index_id, period)`; CHECK `level > 0`; `level numeric(18,6)`; `IndexPeriod` → `date` conversion with CHECK `EXTRACT(DAY FROM period) = 1`
- [x] 3.3 `AdjustmentClauseConfiguration` — UNIQUE `contract_id` (via the 1:1 relationship, not a separate index); CHECK `interval_months BETWEEN 1 AND 60` — `combination` column NOT created, see deviation below
- [x] 3.4 `AdjustmentClauseIndexConfiguration` — composite key `(adjustment_clause_id, economic_index_id)`
- [x] 3.5 `RentAdjustmentConfiguration` — `previous_canon`/`new_canon` `numeric(14,2)`; `coefficient numeric(12,6)`; CHECK `(kind='Correction') = (corrects_adjustment_id IS NOT NULL)`; CHECK day-of-month = 1 on `effective_date`; `SetAfterSaveBehavior(PropertySaveBehavior.Throw)` looped over every property
- [x] 3.6 `RentAdjustmentIndexValueConfiguration` — composite key `(rent_adjustment_id, referenced_index_id)` via a shadow `RentAdjustmentId` FK property (the entity has no id/FK property of its own by design); `base_level`/`end_level` `numeric(18,6)`; `variation numeric(12,6)`
- [x] 3.7 `InmobiliariaDbContext.cs` modified: removed the four `Ignore<>()` calls, registered six new `DbSet<T>`s (not five, see deviation below)
- [x] 3.8 Generated migration `AddRentAdjustments` via `dotnet ef migrations add` — confirmed **six `CREATE TABLE`s, zero `ALTER TABLE`** (not five, see deviation below)
- [x] 3.9 Hand-edited the migration's `Up`/`Down`: appended a plain `BEFORE UPDATE OR DELETE ON rent_adjustments` trigger + function that unconditionally raises (NOT a deferred constraint trigger, per design.md Decision 6's explicit distinction from the share-sum trigger); `Down` drops trigger then function first
- [x] 3.10 Guardrail: `20260913215911_InitialSchema.cs` and its `.Designer.cs` are byte-for-byte unmodified (`git diff` empty); `InmobiliariaDbContextModelSnapshot.cs` diff is purely additive (zero removed lines), confirming no pre-existing table definition changed
- [x] 3.11 `SchemaConstraintTests.NumericColumnPrecision_MatchesMoneyAndIndexLevelConventions` — spec test 6 integration half: reads `information_schema.columns`, asserts `numeric(14,2)` on `previous_canon`/`new_canon` and `numeric(18,6)` on `level`/`base_level`/`end_level`
- [x] 3.12 `SchemaConstraintTests.AppendOnlyTrigger_RejectsRawUpdateAndDelete` — spec test 13: raw SQL `UPDATE` and `DELETE` on `rent_adjustments` both throw `PostgresException`
- [x] 3.13 `SchemaConstraintTests.CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection` — spec test 14: correcting an `IndexValue.Level` after a confirmed adjustment leaves the original `rent_adjustments` row's `Coefficient`/`Kind`/`CorrectsAdjustmentId` unchanged; a second, `Correction`-kind row coexists naming the first as `CorrectsAdjustmentId`
- [x] 3.14 Guardrail: `PostgresFixture` unchanged (`git diff` empty after reverting temporary debug logging) — still calls `Database.MigrateAsync()`, never `EnsureCreated()`, still pinned to `postgres:17.6`
- [x] 3.15 Guardrail: `dotnet test Inmobiliaria.Core.slnf -c Release` runs both `Inmobiliaria.Domain.Tests` and `Inmobiliaria.Infrastructure.Tests`; no `.slnf` edit needed
- [x] 3.16 Guardrail: grepped the full diff and every new file for `password`/`secret`/connection-string patterns — none found

## Slice 3 — Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/IndexPeriodValueConverter.cs` | Created | Shared `ValueConverter<IndexPeriod, DateOnly>` (nullable and non-nullable), reused by three configurations |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/EconomicIndexConfiguration.cs` | Created | `economic_indices`: partial unique index, self-successor CHECK |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/IndexValueConfiguration.cs` | Created | `index_values`: UNIQUE `(economic_index_id, period)`, `level > 0` CHECK, day-1 CHECK |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/AdjustmentClauseConfiguration.cs` | Created | `adjustment_clauses`: 1:1 with `Contract` (unique FK), interval CHECK, `Combination` ignored |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/AdjustmentClauseIndexConfiguration.cs` | Created | `adjustment_clause_indices`: composite key, FKs to `AdjustmentClause` (cascade) and `EconomicIndex` (restrict) |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/RentAdjustmentConfiguration.cs` | Created | `rent_adjustments`: money/coefficient precision, two CHECKs, after-save-throw on every property |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/RentAdjustmentIndexValueConfiguration.cs` | Created | `rent_adjustment_index_values`: shadow-FK composite key, level precision |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractConfiguration.cs` | Modified | Added `builder.Navigation(c => c.Adjustments).UsePropertyAccessMode(PropertyAccessMode.Field)` (+1 line) |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modified | Removed the four `Ignore<>()` calls; added six `DbSet<T>` properties |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260918233049_AddRentAdjustments.cs` | Created | Six `CREATE TABLE`s (EF-generated) + hand-written append-only trigger SQL in `Up`/`Down` (authored) |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20260918233049_AddRentAdjustments.Designer.cs` | Created | EF-generated, fully boilerplate |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/InmobiliariaDbContextModelSnapshot.cs` | Modified | EF-generated, purely additive diff |
| `tests/Inmobiliaria.Infrastructure.Tests/SchemaConstraintTests.cs` | Modified | Added two seeding helpers + 3 new tests (spec tests 6/13/14) |

Authored lines (excluding EF-generated `CREATE TABLE`/`CREATE INDEX`/`.Designer.cs`/model-snapshot
boilerplate): 7 new configuration files (329) + `ContractConfiguration.cs` (+1) +
`InmobiliariaDbContext.cs` (+11/−17, net −6 but 28 changed) + `SchemaConstraintTests.cs` (+180) +
hand-written trigger SQL in the migration's `Up`/`Down` (~30) ≈ **568 changed lines**, against the
tasks.md/design.md estimate of 420–520 for PR3. Reported honestly — see Risks below; the
implementation was already complete, tested, and working by the time the count was totalled, so it
was finished and reported rather than discarded mid-task, consistent with how Slice 2's own
overage was handled.

## Slice 3 — Deviations from Design (IMPORTANT for verify)

1. **Six tables, not five.** `design.md`'s summary prose ("five new EF configurations", "the
   migration issues five `CREATE TABLE`s") and `tasks.md`'s own task 3.7/3.8 text ("register the
   five new `DbSet<T>`s", "confirm the diff is exactly five `CREATE TABLE`s") both say **five**.
   But `design.md`'s own Decision 3 table — the detailed mapping table, which the orchestrator
   prompt named as "the authority... on the mapping" — lists **six** distinct tables with full
   column specs: `economic_indices`, `index_values`, `adjustment_clauses`,
   `adjustment_clause_indices`, `rent_adjustments`, `rent_adjustment_index_values`. Both join
   tables (`adjustment_clause_indices`, `rent_adjustment_index_values`) are explicitly described
   with their own composite keys and columns, exactly like the already-existing `contract_units`/
   `contract_parties` join tables from lease-contract (which also each get their own table and
   `DbSet`, per the six *existing* configurations already in this codebase). There is no way to
   satisfy the spec's requirements (one clause holding 1..N ordered indices; one adjustment holding
   N snapshotted per-index values) in fewer than six physical tables without inventing an
   unrequested JSON-column mapping that design.md never mentions and that would contradict the
   explicit relational column types (`numeric(18,6)` etc.) it gives for those very fields.
   Followed the detailed Decision 3 table (six tables) over the summary prose's miscount (five),
   per the instruction to follow design.md as the mapping authority; flagging the internal
   inconsistency rather than silently picking one. The generated migration is exactly six
   `CREATE TABLE`s and zero `ALTER TABLE` — confirmed by reading the generated file.
2. **`AdjustmentClause.Combination` is not a stored column.** `design.md` Decision 3 lists
   `adjustment_clauses.combination text` with `CHECK combination IN ('Single','Average')`, and
   task 3.3 asks for that CHECK. But `AdjustmentClause.Combination` (from Slice 2) is a pure
   expression-bodied getter (`_indices.Count == 1 ? Single : Average`) with no setter and no
   backing field of its own — EF cannot materialize a value into it on load, since there is
   nowhere to write it. Mapping it as a real column would require adding a backing field to
   `AdjustmentClause.cs`, a domain change outside this slice's EF-configuration-only scope (Slice 3
   tasks are exclusively "EF configs + migration"; no domain file is listed). `builder.Ignore(c =>
   c.Combination)` is used instead: the value is always correctly re-derived at runtime from the
   loaded `Indices` collection, with zero risk of a stored copy going stale against the rows that
   are the actual source of truth. The `combination` column and its CHECK do not exist in the
   migration. Flagged for verify: this is an intentional, justified gap, not an oversight, and it
   should be raised with the design as either (a) accept the derived-only reading, or (b) a small
   Slice-2-territory domain follow-up to give `Combination` a constructor-set backing field if a
   physical CHECK is genuinely required later.
3. **`ReferencedIndexId`/`ResolvedIndexId` on `RentAdjustmentIndexValue` have no FK constraint to
   `economic_indices`.** Design explicitly frames them as "ids for display and traceability only
   — the numbers themselves are copies, never re-derived," which reads as a soft reference rather
   than a hard relational integrity requirement. Left as plain `uuid` columns to keep the mapping
   simple and avoid two ambiguous same-target relationships needing extra disambiguation. Not
   requested by task 3.6 either way; noted as a minor, deliberate simplification.
4. **A real EF Core gotcha surfaced and was fixed in the test, not the domain**: appending a
   second `RentAdjustment` to an *already-tracked* `Contract`'s `_adjustments` field (as
   `Contract.ConfirmAdjustment` does, with zero EF awareness by design) is not automatically
   inferred as an `Added` entity by EF's `DetectChanges` — a newly-discovered entity with a
   non-default, client-assigned `Guid` key reached only through a collection-navigation diff (not
   through an explicit `context.Add(...)`) defaults to `Unchanged`, since EF cannot distinguish
   "freshly constructed" from "already exists." The parent `RentAdjustment` insert then gets
   silently dropped from the batch while its child `rent_adjustment_index_values` row still
   attempts to insert, failing its foreign key — no exception at all until that FK violation.
   `SchemaConstraintTests.CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection`
   fixes this with an explicit `context.RentAdjustments.Add(correction);` before the second
   `SaveChangesAsync`. **This is a real, load-bearing finding for the future application/use-case
   layer** (out of scope for this change per design.md's scope guard): any code that confirms a
   correction (or any second adjustment) against a `Contract` that is already tracked in the same
   `DbContext` lifetime MUST make the same explicit `Add` call, or the correction will silently
   fail to persist its parent row. Recorded here so the eventual worklist/confirmation UI change
   does not rediscover this the hard way.
5. Everything else matches `design.md`'s Decision 1 (levels not variations), Decision 2 (`IndexPeriod`
   → `date` with day-1 CHECK), Decision 3 (remaining columns/precisions), Decision 5 (no `contracts`
   column, FK lives on `adjustment_clauses.contract_id`), and Decision 6 (plain, non-deferred
   `BEFORE UPDATE OR DELETE` trigger — explicitly not the share-sum trigger's shape) as written.

## Slice 3 — Issues Found

None beyond the two deviations above (six-vs-five table count, `Combination` not persisted), both
of which are legitimate design/tasks gaps rather than implementation defects, and the EF
change-tracking gotcha (deviation 4), which is fixed and documented rather than an open issue.

## Work Unit Evidence (Slice 3)

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Infrastructure.Tests -c Release --filter "FullyQualifiedName~SchemaConstraint"` → 15/15 passed, 0 skipped (Docker was available; every `[SkippableFact]` ran for real, not skipped) |
| Full solution build | `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors |
| Full test suite | `dotnet test Inmobiliaria.Core.slnf -c Release` → Domain.Tests 52/52 passed; Infrastructure.Tests 15/15 passed; 0 skipped, 0 failed across both projects |
| Architecture guard | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~ArchitectureGuard"` → 1/1 passed |
| Runtime harness command/scenario and exact result | `dotnet ef migrations add AddRentAdjustments -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` (design-time, no live DB — per `DesignTimeDbContextFactory`) generated cleanly with zero model-validation errors; the three `SchemaConstraintTests` additions above then exercised the generated schema for real against `postgres:17.6` via Testcontainers (append-only trigger, correction coexistence, `information_schema.columns` precision) — `dotnet ef database update` was deliberately never run against any real/Supabase connection, per the hard constraint not to touch the live database |
| Rollback boundary | `dotnet ef migrations remove -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` (never applied to any real database, so no `database update` rollback is needed); delete the 7 new `Configurations/*.cs` files, the 2 new `Migrations/20260918233049_AddRentAdjustments*.cs` files; revert `InmobiliariaDbContext.cs`, `ContractConfiguration.cs`, `InmobiliariaDbContextModelSnapshot.cs`, and `SchemaConstraintTests.cs`; PR1/PR2 (pure domain, never persisted before this slice) are entirely unaffected |

## Work Unit Evidence (Slice 2)

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~Adjustment"` → 16/16 passed |
| Full domain suite | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release` → 52/52 passed (35 pre-existing through Slice 1 + 17 new in Slice 2) |
| Build | `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors=true`) |
| Architecture guard | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~ArchitectureGuard"` → 1/1 passed |
| Runtime harness | N/A — pure domain, no DB, no Testcontainers boundary in this slice |
| Rollback boundary | Revert the `Contract.cs` diff; delete every new `Leasing/Adjustment*.cs`, `Leasing/CombinationRule.cs`, `Leasing/RoundingRule.cs`, `Leasing/RentAdjustment*.cs` file and its matching test file; PR1 (`Indices/*`) is unaffected |

## Workload / PR Boundary (Slice 2)

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

## Workload / PR Boundary (Slice 3)

- Mode: chained PR slice (`stacked-to-main`, target branch `develop` per session preflight)
- Current work unit: Unit 3 — EF configurations + migration + trigger (PR 3)
- Boundary: starts from Slice 2's persisted-nowhere domain (already merged) and ends with the
  full domain graph mapped and migrated — six `CREATE TABLE`s, the append-only trigger, and three
  new integration tests proving spec tests 6/13/14 against a real `postgres:17.6`; `dotnet ef
  database update` deliberately never run against any live/Supabase connection
- Estimated review budget impact: **≈568 changed lines against the 420–520 PR3 estimate** (7 new
  configuration files: 329; `ContractConfiguration.cs`: +1; `InmobiliariaDbContext.cs`: +11/−17;
  `SchemaConstraintTests.cs`: +180; hand-written migration trigger SQL: ≈30 — EF-generated
  `CREATE TABLE`/`CREATE INDEX`/`.Designer.cs`/model-snapshot content excluded per instructions).
  About 9% over the top of the estimated range, driven mainly by the two join-table configurations
  and the shadow-FK handling for `RentAdjustmentIndexValue` that `design.md`'s own "five tables"
  summary undercounted (see Deviation 1) plus three real integration tests. Reported honestly per
  the same convention as Slice 2's overage; the work was already complete and fully tested by the
  time the count was totalled, so it was finished rather than discarded mid-task.

## Status

9/9 Slice 1 tasks + 20/20 Slice 2 tasks + 17/17 Slice 3 tasks complete (46/55 total across all four
slices — corrected denominator: 9+20+17+9=55 code tasks; the prior "29/49" note undercounted it).
Ready for verify on Slices 1–3, or for a delivery decision on PR2's and PR3's size before either is
opened (see Risks). Slice 4 (worklist read model + adapter, tasks 4.1–4.9) and the non-code human
follow-ups H.1–H.5 remain.
