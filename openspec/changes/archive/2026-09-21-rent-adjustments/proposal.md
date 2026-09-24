# Proposal: Rent Adjustments by Index

Domain vocabulary (Argentine terms, glossed once): **canon** = the total monthly rent of a
contract; **locador** = lessor/owner; **locatario** = tenant; **IPC** = Índice de Precios al
Consumidor, INDEC's consumer price index; **RIPTE** = Remuneración Imponible Promedio de los
Trabajadores Estables, the Ministry of Labour's index of average taxable wages; **ICL** = Índice
para Contratos de Locación, the BCRA lease index.

Second SDD change. Domain rules come from `domain/contrato-locacion-reglas` (cláusula CUARTA of
the real signed lease) — this proposal does not restate them.

## Intent

The canon is a stored number today (`Contract.MonthlyRent`) that nothing ever changes on schedule.
The real lease adjusts it **semiannually** by the **average of IPC and RIPTE**, and the operator
does that arithmetic by hand: he looks the indices up, computes the coefficient, and types the new
figure. Nothing records *why* the rent went from one figure to another.

**Why now:** every remaining change bills against a canon. The current account needs to know which
canon governed a given month, collection charges it, and receipts print it. Adding adjustments
after those exist means retrofitting a price history into code that already assumes one price.
It is also the only remaining change that needs nothing from the agency owner — the rules are all
written in the signed contract.

**Success:** the operator opens a worklist of contracts whose adjustment has fallen due, sees the
new canon already computed from index values he entered once, confirms it, and can later show the
locatario exactly which index periods produced that number.

## Scope

### In Scope
- An index catalog (`EconomicIndex`) with published values per period, **manually entered**
- Index supersession: an index that stops being published names its replacement
- A per-contract adjustment clause: which indices, how they combine, at what interval
- Coefficient computation and the proposed new canon
- Operator confirmation of a due adjustment, and an append-only adjustment history
- Derivation of the next adjustment date from the contract, not a hand-kept field
- EF configurations plus a **new** migration (the existing one is live and MUST NOT be edited)

### Out of Scope

Everything here is planned work that belongs to another change. "Out of scope" means *not in this
pull request*, never *not in the product*. Each item names the change that owns it.

- **Automatic index retrieval** — its own later change (see Approach for why manual comes first)
- Current account, debt and recargo — the current-account change
- Collection, receipts and **PDF output** — the collection change. Every receipt and liquidación
  the agency issues today is a printed document, so PDF generation is a core deliverable of that
  change, not an optional extra. The `pdf-library` decision in `openspec/config.yaml` stays open
  precisely because we know it is coming.
- Notifications that an adjustment is due or overdue — the notifications change. These are alerts
  about a *contract's* adjustment date approaching, not about an index value moving.
- Users and roles — the users-and-roles change
- Deposit, penalty and early-termination arithmetic — still deferred from change 1

## Capabilities

### New Capabilities
- `economic-index`: index identity, published values per period, supersession chain
- `rent-adjustment`: per-contract clause, due detection, coefficient, confirmation, history

### Modified Capabilities
- `lease-contract`: the canon stops being a purely stored value. `ChangeMonthlyRent` gains a
  documented relationship to a confirmed adjustment, and the contract gains its adjustment clause.
  The existing "Share Stored as Percentage, Never Amount" requirement is the invariant this change
  finally exercises for real — it MUST keep passing.

## Approach

### Settled: index values are entered manually

Automatic retrieval is explicitly out of scope and deferred. Four reasons, all load-bearing:

1. **It matches what the agency already does.** The operator looks the index up and computes the
   increase by hand today. Replacing his arithmetic is the win; replacing his data source is a
   separate, larger problem.
2. **There is no clean free API** for IPC or RIPTE. Automation means scraping, which breaks
   whenever the publishing site changes.
3. **It dissolves the publication-lag problem.** IPC for a month publishes around the middle of
   the following month and RIPTE lags further, so on the day an adjustment falls due the value
   very often does not exist yet. With manual entry the operator types the value when he has it
   and the system never blocks waiting on anything.
4. **Manual entry survives automation anyway.** A published value can be corrected afterwards, and
   the contract's own fallback clause requires substituting an index by hand.

**Critical refinement:** manual versus automatic is about *how the table gets filled*, not whether
it exists. Index values are **stored as data**, with their period, in their own entity. This keeps
adjustments reproducible, avoids retyping the same index for every contract it applies to, and
means a future automation change fills the same table instead of forcing a redesign.

### The five design questions

**1. Where does the price history live? → An explicit `RentAdjustment` history.**
Change 1 argued that no honorarios rate history was needed because an issued receipt stores its
already-computed amounts, so the printed document *is* the record. That reasoning does **not**
extend to the canon, for three reasons: receipts do not exist yet, so today nothing snapshots
anything; the canon is a *derived* figure the operator must be able to justify to the locatario,
not a rate he agreed once; and the current-account change will bill months retroactively and must
ask "what did this contract charge in March?" — a question a single current value cannot answer.
`Contract.MonthlyRent` stays as the current canon; each confirmed adjustment appends a row
recording effective date, previous canon, new canon, the coefficient, and the index values used.

**2. How is the adjustment expressed? → A per-contract clause, never a hardcoded formula.**
This contract uses the IPC/RIPTE average; others may use ICL alone. The clause holds 1..N index
references plus a combination rule as a small closed enum (`Single`, `Average`), plus the interval
in months, plus the rounding rule. An expression engine is not warranted — a new real clause shape
extends the enum through a migration.

**Correctness note for design:** IPC is an index number and RIPTE is an amount in pesos. Averaging
their *levels* is meaningless. The average must be of the two **percentage variations** over the
period. Storing levels rather than variations is the recommended direction, because the variation
for any interval is then a ratio of two stored levels — `sdd-design` decides and records it.

**3. Automatic on its date, or operator-confirmed? → Operator-confirmed, and the system proposes.**
An adjustment changes what a tenant owes. On the due date the index value frequently does not
exist yet (reason 3 above), so an automatic apply would either block or invent a number. Entered
values can also be corrected afterwards. The system detects due adjustments, pre-computes the new
canon, and presents a worklist; the operator confirms. This also keeps the change free of any
background execution requirement.

**4. Index supersession → the index is an entity with a lifecycle, not a string.**
An index can be marked discontinued from a period and name its successor. Values already published
stay readable, the contract clause keeps pointing at the index it actually names, and resolution
follows the successor chain for later periods. Rejected alternative: editing the clause in place —
it destroys the record of what the contract said.

**5. Next adjustment date → derived, not stored as a free field.**
Next due = effective date of the last confirmed adjustment + `IntervalMonths`, falling back to
`StartDate + IntervalMonths` when none has been confirmed. The interval lives on the clause
(six months in this contract, never assumed). Because rent is charged per whole month with no
proration, an adjustment always takes effect on the first day of a monthly period.

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/Inmobiliaria.Domain/Indices` | New | `EconomicIndex`, `IndexValue`, supersession |
| `src/Inmobiliaria.Domain/Leasing` | Modified | `AdjustmentClause`, `RentAdjustment`, `Contract` wiring |
| `src/Inmobiliaria.Infrastructure/Persistence` | Modified | New EF configurations, new `DbSet`s |
| `.../Persistence/Migrations` | New | A **new** migration; `20260913215911_InitialSchema` is live and untouchable |
| Supabase Postgres | New | Tables for indices, index values, adjustment clauses, adjustments |
| `openspec/specs/lease-contract` | Modified | Delta: canon gains a clause and a history |

## Settled Decisions

Decided by the Volkode team on 2026-09-16 without waiting for the agency owner. These are *our*
decisions, not his answers: if he contradicts one, he wins.

**A late adjustment is not charged retroactively, but the correct price does resume.** If the
agency forgets to apply an adjustment and loads it late, the missed months are NOT billed — the
mistake was the agency's and it absorbs it, so a tenant never receives a surprise bill for the
past. The correct canon DOES apply from the next period onward, with notice to the tenant.

The alternative considered and rejected was waiting until a new lease is signed. A lease runs 24
months with three adjustments, so waiting for renewal could mean 18 months of undercharging — and
the canon is the **owner's** money, not the agency's. The agency administers it under the express
mandate of cláusula DÉCIMA SEXTA, so declining to apply an adjustment gives away someone else's
income. The tenant also signed accepting semiannual adjustments, so the correct price is the
agreed one, not an imposition.

*Consequence for the current-account change: it does NOT need to generate retroactive debt from a
late adjustment.*

**The honorarios suggestion list starts empty and grows from real use.** The rate may be left
unassigned. When an owner is selected, the system proposes the rate last applied to that owner,
because the rate varies by owner — the 8% on the real receipt is VICO's rate, not a constant. For
a new owner it offers the rates already used in the system, plus free entry. Seeding the list with
2/4/6/8 was rejected: those are our guesses about a fact the system will know for real within two
months of use.

**Liquidación shape is a preference stored on the owner** — consolidated across their properties,
or one per property. A field, not an architecture. Note that the real receipt is per contract,
so a consolidated liquidación spans several contracts and needs a grouping concept that does not
exist yet. That belongs to the collection change.

**The computed amount is charged exactly, with no commercial rounding**, and every money-moving
operation shows a summary before it is confirmed. If the calculation yields $415,617 then $415,617
is charged; it is never rounded up to a tidier figure. This is distinct from monetary precision —
money stores two decimals, so $517,483.7291 becomes $517,483.73 — and the two coexist.

> ⚠️ **SUPERSEDED — do not implement the sentence above.** It was written before this change's own
> design Decision 4 settled truncation. The charged amount is **truncated to a whole peso**, so
> $517,483.7291 becomes **$517,483**, not $517,483.73. Storage precision stays `numeric(14,2)`;
> that part is still correct — what changed is the charged figure. The rule as it stands is in
> `openspec/specs/rent-adjustment/spec.md`, "Whole-Peso Canon, Truncated, With No Commercial
> Rounding", confirmed by the agency owner on 2026-09-23.
>
> The original sentence is left in place on purpose: this file is the record of what the team
> believed on 2026-09-21, not a description of the system.

The confirmation summary (previous canon, index values used, coefficient, new canon) is a **general
principle of this system, not a feature of this change**. Confirmation is the exact point where a
typing mistake becomes money someone owes: one extra zero in an index multiplies the rent. Showing
the result before it is committed costs almost nothing and catches the error while it is still free.

**A correction to an index value never rewrites a confirmed adjustment.** It produces a new
correcting adjustment instead, append-only, the way an accounting entry is never erased but offset.

## Assumptions

Recorded deliberately, not treated as fact.

- **Stale-value assumption (NEEDS CONFIRMATION).** When an adjustment falls due and the index has
  not been published yet, the user *believes* the operator enters the most recent available value
  rather than waiting. **This is his assumption, not an answer from the agency owner.** It must be
  confirmed before the behaviour is treated as a rule. It is on the list for the next owner
  meeting, alongside the four questions still open from change 1.
- **With no value at all**, the system does nothing automatic: the adjustment stays *due and
  pending*, the previous canon keeps applying, and the contract stays on the worklist. The system
  MUST NOT invent a value, MUST NOT silently skip the period, and MUST NOT compute an average from
  a partial index set — for a two-index average, both values are required.

## Open Decisions Touched

Flagged, not resolved (per `openspec/config.yaml`):

- **`scheduler-mechanism`** — an adjustment falling due while the desktop app is closed is exactly
  what this decision covers. The operator-confirmed recommendation (design question 3) deliberately
  confines the problem to a *reminder*, which belongs to the notifications change. Nothing in this
  change needs to execute unattended.
- **`credential-exposure`** — confirming an adjustment changes what a tenant owes, and this change
  adds the first such action. With role separation cosmetic, anyone on that machine can perform it.
  Noted, not resolved.
- `pdf-library` — untouched.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Averaging index levels instead of variations produces nonsense | **High if unspecified** | `sdd-design` MUST settle level-vs-variation storage; spec scenario asserts the IPC/RIPTE average is of variations |
| Change exceeds the 800-line budget | **High** | Chain three PRs; `ask-on-risk` surfaces it at `sdd-tasks` |
| The stale-value assumption is wrong | Med | It shapes UI guidance, not schema. Values are correctable by design, so a wrong guess costs a re-entry, not a migration |
| Adjustment history modelled as an audit log instead of a billing input | Med | Spec it as the source of "canon in force for period P", the question the current-account change will ask |
| Editing the live migration | **High impact** | Explicit rule: new migration only. Verified against the applied Supabase schema |
| Rounding drift across repeated adjustments | Med | `decimal` throughout; the rounding rule is stored on the clause and named in the spec |

## Rollback Plan

1. `dotnet ef database update 20260913215911_InitialSchema`, then remove the new migration. The
   initial schema is the rollback target and is never modified.
2. `git revert` the PRs in reverse order.
3. Delete `openspec/changes/rent-adjustments/`; the merged `lease-contract` delta is the only spec
   unwind and is additive.

No contract loses data: `Contract.MonthlyRent` is untouched by the rollback and keeps its last
confirmed value.

## Dependencies

- `contract-and-parties`, archived — `Contract` and its rent split already exist
- Domain observations: `domain/contrato-locacion-reglas`, `project/estado-y-orden-de-changes`
- Docker running, so the Testcontainers integration tests against `postgres:17.6` actually execute

## Size Forecast

Honest estimate, authored lines (additions + deletions):

| Slice | Est. lines |
|---|---|
| `EconomicIndex`, `IndexValue`, supersession + domain tests | 380–500 |
| `AdjustmentClause`, `RentAdjustment`, coefficient, due-date derivation + tests | 420–560 |
| EF configurations, new migration, integration tests | 400–540 |

**Total 1200–1600 lines — over the 800-line budget.** Targeting the user's 400–500 authored lines
per PR gives **three chained PRs**, in that order (the catalog has no dependency on the clause, so
it ships first). If the generated migration pushes slice 3 past 500, split it into configurations
and migration+tests. `sdd-tasks` plans the split.

## Success Criteria

- [ ] An index value is entered once and reused by every contract that references that index
- [ ] A semiannual IPC/RIPTE-average clause produces the same coefficient as the operator's manual
      arithmetic on the real contract
- [ ] A due adjustment is proposed, not applied, until an operator confirms it
- [ ] After confirmation the canon changes and the per-unit percentages are still untouched and
      still sum to exactly 100%
- [ ] The canon in force for any past month is answerable from the adjustment history
- [ ] A discontinued index resolves through its successor for later periods, with earlier
      published values still readable
- [ ] A due adjustment with a missing index value stays pending, changes no canon, and stays on
      the worklist
- [ ] A corrected index value is reflected without rewriting history silently
- [ ] The new migration applies on top of `20260913215911_InitialSchema` without editing it
