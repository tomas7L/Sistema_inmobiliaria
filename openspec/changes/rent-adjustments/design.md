# Design: Rent Adjustments by Index

Domain vocabulary (Argentine terms, glossed once): **canon** = the total monthly rent of a
contract; **locador** = lessor/owner; **locatario** = tenant; **IPC** = INDEC's consumer price
index, a dimensionless index number; **RIPTE** = the Ministry of Labour's average taxable wage, an
amount in pesos; **ICL** = the BCRA lease index. Identifiers and code are English.

## Technical Approach

Five new entities across two domain namespaces, five new EF configurations, one **new, purely
additive** migration, and one read-model port the notifications change will consume. No ViewModel
and no WPF screen ship here: the deliverable is a tested domain that computes and confirms an
adjustment, persisted against a real Postgres.

Scope guard for `sdd-tasks`: the only infrastructure surface is the five
`IEntityTypeConfiguration<T>` classes, the migration, and the single
`IDueAdjustmentQuery` adapter. No general repository layer, no use-case project, no UI.

The migration issues **five `CREATE TABLE`s and no `ALTER TABLE` against any live table**.
`contracts` gains no column — see Decision 5. This is the strongest available guarantee that
`20260913215911_InitialSchema` stays untouched.

## Architecture Decisions

### Decision 1 — Index values are stored as published **levels**, not as variations

**Choice**: `index_values.level numeric(18,6)` holds the number the publisher published. Every
percentage variation is derived: `variation(a→b) = (level_b / level_a - 1) × 100`.

| Scenario | Stored levels *(chosen)* | Stored variations *(rejected)* |
|---|---|---|
| Two contracts, 6-month and 3-month intervals, same index | Both derive from the same rows; any interval is a ratio of two levels | Only the interval actually recorded is answerable. A 3-month clause cannot be served by rows written for a 6-month one, and compounding stored monthly variations reintroduces the rounding drift the proposal named as a risk |
| A level revised after publication | Correct one row. Every interval that spans it recomputes correctly and automatically | The stored variation does not record which levels produced it, so you cannot find the rows a revision invalidates. Silent wrongness |
| An index is rebased (INDEC has done this) | **The weak case.** A ratio across the splice is meaningless — handled by Decision 7, which models the rebased series as a new index and refuses the cross-splice ratio | Survives natively: a variation is base-independent. This is the honest argument for the rejected option |
| A future automation change | Fills the same column with the same published number, exactly as Decision 1 of the spec requires | Automation would have to compute and store a derivative, choosing an interval on the contracts' behalf |

**Rationale**: three of the four scenarios favour levels decisively, and the fourth is not lost — a
rebase is a *new series*, which the supersession mechanism the spec already mandates models
correctly. Storing the published number also means the stored value is auditable against the
source: an operator can compare the row to INDEC's page. A stored variation is already an opinion.

**RIPTE is stored as a level, not as money**, even though it is denominated in pesos. It is an
input to a ratio, never an amount anybody pays, so `numeric(14,2)` and the whole-peso rule
(Decision 4) deliberately do **not** apply to it. `numeric(18,6)` gives 12 integer digits of
inflation headroom and 6 decimals, matching the precision INDEC and the Ministry publish.

### Decision 2 — Period is a first-class value mapped to a first-of-month `date`

**Choice**: `readonly record struct IndexPeriod(int Year, int Month)` in the domain, mapped with
`HasConversion` to a `date` column carrying `CHECK (EXTRACT(DAY FROM period) = 1)`.

**Alternatives**: two `int` columns (composite ordering in every query, no date arithmetic);
`char(7)` `'2026-08'` (orders correctly but supports no month arithmetic, so every due-date
calculation becomes string surgery).

**Rationale**: the spec requires a derived due date to fall on the first day of a monthly period.
Making that the *storage shape* means the requirement cannot be violated by arithmetic — the CHECK
is the requirement. `DateOnly.AddMonths` then gives interval arithmetic for free, and the column
sorts and ranges natively in Supabase Studio.

### Decision 3 — Entities and EF mapping

| Table | Key | Notable columns | Rationale / rejected |
|---|---|---|---|
| `economic_indices` | `id` uuid (UUIDv7) | `name text`, `discontinued_from date NULL`, `successor_index_id uuid NULL` self-FK | **Partial** unique index `ON (name) WHERE discontinued_from IS NULL`. The spec says *two active* indices may not share a name — a plain unique index would forbid a rebased "IPC" succeeding a discontinued "IPC", which is precisely the case Decision 1 leans on. `CHECK (successor_index_id <> id)` catches the trivial cycle free |
| `index_values` | `id` uuid | `economic_index_id`, `period date`, `level numeric(18,6)`, `last_corrected_at timestamptz NULL` | UNIQUE `(economic_index_id, period)` is the spec's one-value-per-period rule. `CHECK (level > 0)`. A surrogate key rather than the natural composite because a value row is **mutable** (a correction updates `level`), and a stable id keeps the correction traceable |
| `adjustment_clauses` | `id` uuid | `contract_id` **UNIQUE** FK, `combination text`, `interval_months int`, `rounding_rule text` | Enums as text + CHECK, per the established convention: `combination IN ('Single','Average')`, `rounding_rule IN ('TruncateToWholePeso')`, `interval_months BETWEEN 1 AND 60`. A one-member rounding enum is deliberate — it is extended by a migration when a second real clause appears, exactly as `CombinationRule` is |
| `adjustment_clause_indices` | `(adjustment_clause_id, economic_index_id)` | `ordinal int` | Explicit join entity holding the **id**, never an `EconomicIndex` reference — identical to `ContractUnit` holding `UnitId`. Keeps the clause from reaching into another aggregate |
| `rent_adjustments` | `id` uuid | `contract_id`, `effective_date date`, `previous_canon numeric(14,2)`, `new_canon numeric(14,2)`, `coefficient numeric(12,6)`, `kind text`, `corrects_adjustment_id uuid NULL`, `confirmed_at timestamptz` | `CHECK ((kind = 'Correction') = (corrects_adjustment_id IS NOT NULL))` binds the two columns so a correction can never lose its antecedent. `CHECK (EXTRACT(DAY FROM effective_date) = 1)` |
| `rent_adjustment_index_values` | `(rent_adjustment_id, referenced_index_id)` | `resolved_index_id`, `base_period`, `base_level`, `end_period`, `end_level`, `variation numeric(12,6)` | Explicit join entity carrying payload. **Levels are snapshotted by value, not referenced by FK** — see below |

**Why the adjustment snapshots levels instead of FK-ing to `index_values`**: an `IndexValue` row
is corrected in place. A foreign key would make a confirmed adjustment silently change meaning when
its source is corrected — the exact outcome the append-only and correction requirements forbid.
`referenced_index_id` stays an FK for display and traceability only; the numbers are copies.

**Coefficient precision is `numeric(12,6)`, not the `numeric(9,6)` used for `share_percentage`.**
A share is bounded by 100 by definition; a variation is not. Four extra integer digits cost nothing
and remove any chance of a hyperinflationary overflow at the point money is set.

Keys are `Guid.CreateVersion7()` client-side, unchanged from the archived design: a valid in-memory
graph exists before `SaveChanges`, which is what makes the domain half of Decision 6 enforceable.

### Decision 4 — Truncation to a whole peso happens exactly once, and where

`decimal` throughout; `float`/`double` appear nowhere. The pipeline, in order:

```
variation_i = (end_level_i / base_level_i - 1) * 100m     full decimal precision
variation_i = decimal.Round(variation_i, 6)               ← rounded ONCE, to the column's precision
coefficient = average(variation_i), rounded to 6          ← from the ROUNDED variations
proposed    = previous_canon * (1 + coefficient / 100m)   full decimal, centavos and beyond
new_canon   = decimal.Truncate(proposed)                  ← THE single truncation
```

**The one truncation lives inside `RentAdjustment.Confirm(...)`**, the only factory that produces a
confirmed adjustment. `Contract.ChangeMonthlyRent` is fed an already-whole number and is **not**
changed to truncate — doing so would retroactively alter the behaviour of an archived capability
from inside this change. That asymmetry is recorded as an open question, not hidden.

The coefficient is averaged from the *rounded* variations, not the raw ones, so that
`previous_canon`, the stored `coefficient` and the stored per-index variations reproduce
`new_canon` exactly from the history alone. Deriving from unrounded intermediates would make the
stored row a number nobody can recompute — which defeats the reason the history exists.

`decimal.Round` uses banker's rounding (`MidpointRounding.ToEven`) at 6 decimals; `decimal.Truncate`
truncates toward zero, and a canon is always positive, so truncate and floor coincide. Both are
stated because a reviewer should not have to infer them.

### Decision 5 — A contract with no adjustment clause

**Choice**: the FK lives on `adjustment_clauses.contract_id` with a UNIQUE constraint. `contracts`
gains **no column**. Absence of a clause is the absence of a row.

**Rejected**: a nullable `contracts.adjustment_clause_id` — it requires an `ALTER TABLE` on the live
table, and it makes "fixed-price lease" a null check that every future query must remember.

**Rationale**: the worklist query (Decision 6) starts *from* `adjustment_clauses` and joins to
`contracts`, never the reverse. A fixed-price contract is therefore absent from the worklist **by
construction of the query**, not by a filter a future maintainer can forget to write. `Contract`
exposes `AdjustmentClause? AdjustmentClause` as an EF navigation — a C# property, not a column.

### Decision 6 — Append-only history: domain **and** database, and one place where only the domain

The previous change's pattern applies here, and for the same reason.

- **Domain (primary)**: `RentAdjustment` has no public mutator and no public setter. `Contract`
  holds `private readonly List<RentAdjustment> _adjustments` exposed as `IReadOnlyCollection`; the
  only mutator is `Contract.ConfirmAdjustment(...)`, which appends and never replaces.
- **EF (cheap middle layer)**: every `RentAdjustment` property gets
  `.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw)`, so a tracked modification fails in
  EF with a readable message instead of surfacing as an opaque Postgres error.
- **Database (backstop)**: a hand-written `BEFORE UPDATE OR DELETE ON rent_adjustments` trigger that
  unconditionally raises. Unlike the share-sum trigger this is **not** a deferred constraint
  trigger — the rule is per row and needs no commit-time view, so the simpler shape is correct.
  Spec test 13 requires a raw SQL attempt, so the database half is a requirement, not belt-and-braces.
- **Residual gap, accepted**: a superuser can `ALTER TABLE ... DISABLE TRIGGER`. Named, not closed.

**Where the pattern is deliberately *not* repeated**: the `Single`⟺one-index /
`Average`⟺two-or-more invariant is enforced in the domain only. It is set once at clause creation
and never mutated row-by-row, so the deferred-trigger machinery that the share sum genuinely needed
would be a second plpgsql function to maintain against a risk two internal users do not carry. The
cheap database half is still taken: the `combination` and `interval_months` CHECKs.

### Decision 7 — Supersession resolution, and the splice it refuses

A pure domain function, no infrastructure:

```csharp
public static IndexResolution Resolve(EconomicIndex index, IndexPeriod period);
// Walks SuccessorIndexId while period >= DiscontinuedFrom, max 8 hops.
// Returns Resolved(index) or Unresolved(reason) — a result, never an exception,
// because "no value can be resolved" is a worklist outcome (pending), not a fault.
```

The hop cap turns a data cycle into a loud, bounded failure rather than a hang; the self-FK CHECK
catches the one-hop case in the database.

**The splice rule, which is the price of Decision 1**: if the base period resolves to index A and
the end period resolves to index B with `A ≠ B`, the ratio is **refused**. Levels from two series
are not comparable, and a plausible-looking wrong number here is worse than a pending adjustment.
The adjustment stays pending with a distinct reason, and closing it needs a splice coefficient that
no real case has yet demanded — recorded as an open question, not invented now.

### Decision 8 — The pending worklist is a read model behind a port

The notifications change consumes this and must not touch an entity. Contract, in
`Inmobiliaria.Domain.Leasing` (the port lives in Domain; the adapter is the only new class in
Infrastructure beyond configurations):

```csharp
public sealed record MissingIndexValue(Guid EconomicIndexId, string IndexName, IndexPeriod Period);

public sealed record DueAdjustment(
    Guid ContractId,
    DateOnly DueDate,
    decimal CurrentCanon,
    IndexPeriod BasePeriod,
    IndexPeriod EndPeriod,
    IReadOnlyList<MissingIndexValue> Missing,  // empty ⇒ computable
    decimal? Coefficient,                      // null ⇔ Missing.Count > 0
    decimal? ProposedCanon);                   // null ⇔ Missing.Count > 0

public interface IDueAdjustmentQuery
{
    Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default);
}
```

`Coefficient is null ⟺ Missing.Count > 0` is one invariant that discharges three requirements at
once: no value is invented, a partial index set is never averaged, and the reminder can say
*"3 contracts are waiting for August's IPC"* by reading `Missing` alone. The query also filters
`Contract.Status == Active` — a derived decision the spec does not state, because an ended lease has
no canon to adjust.

**Which periods a due date measures** — the spec left this open and it must be settled. For due
date `D` and interval `N`, the adjustment covers the `N` months `D-N … D-1`; the base level is the
month *preceding* that window and the end level is its last month. For `D = 2026-07-01, N = 6`:
base period `2025-12`, end period `2026-06`. Any other pairing measures `N-1` months.

A direct consequence worth stating plainly: IPC for `2026-06` publishes around mid-July, so an
adjustment due `2026-07-01` is **normally not computable on its due date**. Pending-with-a-named-
missing-value is the ordinary path, not the exception — which is exactly why the proposal chose
operator confirmation over automatic application.

### Decision 9 — What the confirmation summary carries

No WPF screen ships here, but `confirm-with-a-summary` is a project convention, so the contract is
fixed now and the UI change has nothing to invent. `AdjustmentProposal` carries: previous canon;
per index its name, base period + base level, end period + end level, and variation; the
combination rule; the coefficient; the **untruncated** computed canon **and** the truncated canon
(both, so the operator sees the peso being dropped rather than discovering it later); the effective
date; and, when confirmation is late, the number of whole months elapsed since the due date beside
an explicit statement that no retroactive charge is generated.

The architectural teeth: **`Contract.ConfirmAdjustment` takes the `AdjustmentProposal` object
itself, not loose parameters.** A screen therefore cannot confirm anything other than what it
displayed, and the convention is enforced by the signature instead of by reviewer diligence.

## Data Flow

```
  index_values (levels, entered by hand)
        │
        ▼  IndexResolver.Resolve(index, period)      ← successor chain, refuses a splice
  variation_i  ──► coefficient (avg of variations, NEVER of levels)
        │
        ▼
  AdjustmentProposal ──► [ summary shown ] ──► Contract.ConfirmAdjustment(proposal)
                                                     │
                     ┌───────────────────────────────┤
                     ▼                               ▼
        Contract.ChangeMonthlyRent(truncated)   append RentAdjustment (+ snapshotted levels)
             shares untouched, still 100%            BEFORE UPDATE/DELETE trigger blocks edits

  IDueAdjustmentQuery.GetDueAsync(asOf) ──► DueAdjustment[]  ──► (notifications change)
        starts FROM adjustment_clauses, so a clause-less contract is absent by construction
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Inmobiliaria.Domain/Indices/EconomicIndex.cs`, `IndexValue.cs`, `IndexPeriod.cs`, `IndexResolution.cs`, `IndexResolver.cs` | Create | Catalog, published level, period value type, supersession resolution |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentClause.cs`, `AdjustmentClauseIndex.cs`, `CombinationRule.cs`, `RoundingRule.cs` | Create | Per-contract clause and its enums |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs`, `RentAdjustmentIndexValue.cs`, `AdjustmentKind.cs` | Create | Append-only history plus snapshotted levels |
| `src/Inmobiliaria.Domain/Leasing/AdjustmentMath.cs`, `AdjustmentProposal.cs`, `AdjustmentSchedule.cs` | Create | Variation/coefficient/truncation; summary contract; due-date derivation |
| `src/Inmobiliaria.Domain/Leasing/DueAdjustment.cs`, `IDueAdjustmentQuery.cs` | Create | Read model + port for the notifications change |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modify | `AdjustmentClause?` navigation, `_adjustments` list, `ConfirmAdjustment(proposal)` |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/` (×5) | Create | One `IEntityTypeConfiguration<T>` per new entity |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modify | Five new `DbSet`s |
| `src/Inmobiliaria.Infrastructure/Persistence/DueAdjustmentQuery.cs` | Create | The only new adapter; implements the port |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/*_AddRentAdjustments.cs` | Create | Five `CREATE TABLE`s, partial unique index, CHECKs, append-only trigger; **no `ALTER TABLE`** |
| `tests/Inmobiliaria.Domain.Tests/` (adjustment, resolution, schedule, truncation) | Create | Pure domain coverage |
| `tests/Inmobiliaria.Infrastructure.Tests/` (schema, worklist) | Modify/Create | Testcontainers `postgres:17.6`, `Database.Migrate()` |

## Testing Strategy

The 18 numbered spec tests mapped onto layers. `Database.Migrate()`, never `EnsureCreated()` — the
append-only trigger is hand-written SQL that `EnsureCreated` would silently omit, producing a green
test against a database with no invariant.

| Spec test | Layer | Why there |
|---|---|---|
| 1 averaging variations not levels | Domain unit | Pure arithmetic. The levels in the spec (8,000→9,440 / 1,200,000→1,344,000) give 15% correctly and 12.04% if levels are averaged, so the test genuinely discriminates |
| 2 single-index skips averaging | Domain unit | Branch on `CombinationRule` |
| 3, 4, 5 truncation behaviour | Domain unit | One truncation site, three assertions against it |
| 6 decimal never float | **Both** | Domain asserts no progressive truncation; integration reads `information_schema.columns` for `numeric(14,2)` |
| 7 missing value leaves it pending | **Both** | Domain asserts no coefficient is produced; integration asserts the contract stays in `GetDueAsync` |
| 8 partial set never averaged | Domain unit | The `Coefficient is null ⟺ Missing non-empty` invariant |
| 9, 10 worklist names / clears the missing value | Integration | The query is the thing under test |
| 11, 12 late adjustment | Domain unit, **scoped** | No billing exists in this change. Assertable here: confirming late appends exactly one adjustment and produces no other row (11), and the canon afterwards equals the new canon (12). **The true "no retroactive charge" assertion belongs to the current-account change and must be re-asserted there** — recorded as a known coverage gap rather than claimed as covered |
| 13 append-only | Integration | Raw SQL `UPDATE`/`DELETE` must fail; the spec requires proof at the database level |
| 14 correction produces a new adjustment | Integration | Two rows must coexist after the source value is corrected |
| 15 supersession resolves | Domain unit | `IndexResolver` is pure |
| 16 no clause never becomes due | Integration | Decision 5's claim is about the query's shape, so only the query can prove it |
| 17 shares survive an adjustment | Domain unit | The archived `lease-contract` invariant, exercised for real for the first time |
| 18 per-contract interval honoured | Domain unit | `AdjustmentSchedule` derivation with a 3-month clause |

One extra test not in the spec's list, from Decision 7: a cross-splice adjustment must be refused,
not computed. It falls out of a design decision rather than a spec line, so it is named here.

## Threat Matrix

N/A — no routing, shell command, subprocess, VCS/PR automation, executable-file classification or
process-integration boundary. No credential enters a tracked file; Testcontainers supplies its own
ephemeral connection string and CI gains no secret, unchanged from the archived design.

## Migration / Rollout

`dotnet ef migrations add AddRentAdjustments -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.
The append-only trigger and its function are appended to `Up` via `migrationBuilder.Sql(...)`, with
matching `DROP TRIGGER` / `DROP FUNCTION` first in `Down`. Rollback target is
`dotnet ef database update 20260913215911_InitialSchema`; because the migration only creates
tables, the rollback drops them and leaves every existing row untouched. One developer applies it
manually against Supabase; the WPF app never calls `Database.Migrate()` on startup.

**Delivery slicing** — honest forecast **1,650–2,000 authored lines**, above the proposal's
1,200–1,600. It grew because the reminder-ready query, its read models and the summary contract are
a real deliverable surface the proposal folded into its other slices. Four chained PRs at the
requested 400–500; `sdd-tasks` owns the formal guard lines.

| PR | Contents | Est. authored lines | Seam rationale |
|---|---|---|---|
| 1 | `Domain/Indices`: `EconomicIndex`, `IndexValue`, `IndexPeriod`, resolver + result, domain tests (15, splice) | 400–480 | The catalog depends on nothing. Decision 1 is settled and provable before anything consumes it |
| 2 | `Domain/Leasing`: clause, `RentAdjustment`, `AdjustmentMath`, schedule, proposal, `Contract` wiring, domain tests (1–5, 8, 11–12 scoped, 17, 18) | 450–540 | All arithmetic and every invariant, reviewable with zero infrastructure |
| 3 | Five EF configurations, `DbSet`s, the new migration incl. trigger SQL, schema tests (6, 13, 14) | 420–520 | The generated migration inflates the diff but not authored risk; hand-written SQL lands with the tests that prove it |
| 4 | `IDueAdjustmentQuery` adapter + worklist integration tests (7, 9, 10, 16) | 380–460 | The contract the notifications change consumes, shipped and proven on its own |

## Open Questions

- [ ] **`credential-exposure` — TOUCHED, NOT RESOLVED.** Confirming an adjustment is the first
      action in this system that changes what a tenant owes. The connection string sits on the
      office PC, so any future Admin/Empleado separation over this action is cosmetic. Nothing here
      depends on resolving it; flag again at users-and-roles.
- [ ] **`scheduler-mechanism` — TOUCHED, NOT RESOLVED.** Decision 8 exists so a scheduler *can*
      consume the worklist, but nothing in this change schedules or sends anything. Resolving it is
      the notifications change's problem.
- [ ] `pdf-library` — untouched.
- [ ] Cross-splice variation (Decision 7). Refused for now. Closing it needs a splice coefficient;
      no real case has demanded one, so none is invented.
- [ ] `Contract.ChangeMonthlyRent` does not truncate (Decision 4). Applying `whole-peso-amounts` to
      the manual path would change an archived capability's behaviour from inside this change.
      Belongs to its own change.
- [ ] The stale-value assumption is still unconfirmed with the agency owner. This design ships only
      the pending path and offers no substitution mechanism, so a confirmed "he enters the most
      recent value" answer is a UI guidance change, not a schema change.
- [ ] Tests 11 and 12 are only partially assertable here (no billing exists). The current-account
      change must re-assert them.
