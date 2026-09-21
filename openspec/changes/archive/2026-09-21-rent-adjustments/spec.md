# Spec: Rent Adjustments by Index

Single-file specification for the `rent-adjustments` change. Three capabilities are covered below:
`economic-index` (**NEW**), `rent-adjustment` (**NEW**), and `lease-contract` (**MODIFIED** — a
delta against the existing living spec at `openspec/specs/lease-contract/spec.md`).

Domain vocabulary (Argentine terms, glossed once): **canon** = the total monthly rent of a
contract; **locador** = lessor/owner; **locatario** = tenant; **IPC** = Índice de Precios al
Consumidor, INDEC's consumer price index (a dimensionless index number, currently in the
thousands); **RIPTE** = Remuneración Imponible Promedio de los Trabajadores Estables, the average
taxable wage published by the Ministry of Labour (an amount in pesos, currently over a million);
**ICL** = Índice para Contratos de Locación, the BCRA lease index.

## Decisions

Every decision this spec rests on, and who made it. **CONFIRMED BY OWNER** means Nicolás Demaría of
Del Lago answered it. **TEAM DECISION** means Volkode decided it without him because the work could
not wait; if he later contradicts one, he wins and the spec changes.

| #  | Decision | Status |
|----|----------|--------|
| 1  | Index values are entered **manually**. Automatic retrieval is a later change and MUST fill the same table, not a parallel one. | TEAM DECISION |
| 2  | The coefficient is the average of the **percentage variations**, never of raw index levels. IPC is an index number and RIPTE is an amount in pesos. | Derived from the signed lease |
| 3  | A **late adjustment is not charged retroactively**; the correct canon applies from the next period onward, with notice to the tenant. | TEAM DECISION |
| 4  | An adjustment is **confirmed by an operator**, never applied silently, and confirmation is preceded by a summary. | TEAM DECISION |
| 5  | The canon is **truncated to a whole peso**. No centavos, no commercial rounding. | TEAM DECISION |
| 6  | A correction to an index value **produces a new adjustment**, never rewrites a confirmed one. | TEAM DECISION |
| 7  | Adjustment history is **append-only**, so issued receipts stay reproducible. | TEAM DECISION |
| 8  | The adjustment interval and formula are **per contract**, never assumed semiannual or single-index. | Cláusula CUARTA |
| 9  | An index that stops being published **names a successor**. | Cláusula CUARTA |
| 10 | A contract **MAY have no adjustment clause** (fixed-price lease). | TEAM DECISION |

### Open with the agency owner

Neither blocks this change. Both are recorded as assumptions in the requirements below.

| Question | Current assumption |
|----------|--------------------|
| When an adjustment falls due and the index is not yet published, does the operator enter the most recent available value or wait? | He enters the most recent available value. **Unconfirmed.** |
| Does the agency truncate or round? | Truncate, inferred from the honorarios line reading 47,860 where the exact figure is 47,860.80. One peso either way changes nothing material. |

## Table of Contents

1. [Capability: economic-index (NEW)](#capability-economic-index-new)
   1. [Requirement: Index Identity](#requirement-index-identity)
   2. [Requirement: Published Value Stored Per Period](#requirement-published-value-stored-per-period)
   3. [Requirement: Manual Entry Is How the Table Is Filled](#requirement-manual-entry-is-how-the-table-is-filled)
   4. [Requirement: Discontinuation Names a Successor](#requirement-discontinuation-names-a-successor)
   5. [Requirement: Successor Resolution for Later Periods](#requirement-successor-resolution-for-later-periods)
2. [Capability: rent-adjustment (NEW)](#capability-rent-adjustment-new)
   1. [Requirement: Per-Contract Adjustment Clause](#requirement-per-contract-adjustment-clause)
   2. [Requirement: Adjustment Clause Is Optional](#requirement-adjustment-clause-is-optional)
   3. [Requirement: Coefficient Is the Average of Percentage Variations, Never of Raw Levels](#requirement-coefficient-is-the-average-of-percentage-variations-never-of-raw-levels)
   4. [Requirement: Next Adjustment Date Is Derived](#requirement-next-adjustment-date-is-derived)
   5. [Requirement: Due Adjustment Worklist](#requirement-due-adjustment-worklist)
   6. [Requirement: Missing Index Value Leaves the Adjustment Pending](#requirement-missing-index-value-leaves-the-adjustment-pending)
   7. [Requirement: Operator Confirmation Preceded by a Summary](#requirement-operator-confirmation-preceded-by-a-summary)
   8. [Requirement: Whole-Peso Canon, Truncated, With No Commercial Rounding](#requirement-whole-peso-canon-truncated-with-no-commercial-rounding)
   9. [Requirement: Monetary Storage Uses Decimal, Never Floating Point](#requirement-monetary-storage-uses-decimal-never-floating-point)
   10. [Requirement: The Pending Worklist Is Reminder-Ready](#requirement-the-pending-worklist-is-reminder-ready)
   11. [Requirement: A Late Adjustment Is Not Charged Retroactively](#requirement-a-late-adjustment-is-not-charged-retroactively)
   12. [Requirement: Append-Only Adjustment History](#requirement-append-only-adjustment-history)
   13. [Requirement: A Correction Produces a New Adjustment, Never a Rewrite](#requirement-a-correction-produces-a-new-adjustment-never-a-rewrite)
3. [Capability: lease-contract (MODIFIED)](#capability-lease-contract-modified)
   1. [ADDED Requirement: Contract May Reference an Adjustment Clause](#added-requirement-contract-may-reference-an-adjustment-clause)
   2. [MODIFIED Requirement: Share Stored as Percentage, Never Amount](#modified-requirement-share-stored-as-percentage-never-amount)
4. [Out of Scope](#out-of-scope)
5. [Tests](#tests)

---

## Capability: economic-index (NEW)

### Purpose

`EconomicIndex` is the catalog of named indices (IPC, RIPTE, ICL, ...) a contract's adjustment
clause can reference, and `IndexValue` is a published value for that index in a given period. This
capability stores index data; it does not compute any contract's coefficient.

### Requirement: Index Identity

The system MUST identify each `EconomicIndex` by a stable name (e.g. "IPC", "RIPTE", "ICL") and
MUST reject creating two active indices with the same name.

#### Scenario: IPC and RIPTE coexist as distinct indices

- GIVEN no index named "IPC" exists yet
- WHEN "IPC" and "RIPTE" are each registered as an `EconomicIndex`
- THEN both MUST be stored as distinct, independently referenceable indices

### Requirement: Published Value Stored Per Period

Each `IndexValue` MUST record the `EconomicIndex` it belongs to, the period it was published for,
and the published level. The system MUST reject a second value for the same index and period.

#### Scenario: One value per index per period

- GIVEN IPC already has a published value for period 2026-06
- WHEN a second value is entered for IPC, period 2026-06
- THEN the system MUST reject it as a duplicate for that index and period

#### Scenario: Same period, different indices

- GIVEN IPC has a published value for period 2026-06
- WHEN RIPTE is given a published value for the same period 2026-06
- THEN both MUST be accepted as independent rows

### Requirement: Manual Entry Is How the Table Is Filled

Index values MUST be entered manually by an operator. Automatic retrieval from any publishing
source MUST NOT be required for this capability to function; automatic retrieval is a later,
separate change and, if added, MUST fill the same `IndexValue` table rather than a parallel one.

#### Scenario: Operator enters a published value

- GIVEN IPC's value for period 2026-08 was just published by INDEC
- WHEN the operator types that level and period into the system
- THEN the value MUST be stored and immediately available to any clause referencing IPC

### Requirement: Discontinuation Names a Successor

An `EconomicIndex` MUST support being marked discontinued from a given period, and MUST optionally
name a successor `EconomicIndex` of similar characteristics. Discontinuing an index MUST NOT delete
or alter its previously published values.

#### Scenario: An index is discontinued with a successor

- GIVEN RIPTE has published values through period 2027-03
- WHEN RIPTE is marked discontinued from period 2027-04 with a named successor index
- THEN RIPTE's existing published values MUST remain stored and readable unchanged
- AND RIPTE MUST be flagged discontinued from 2027-04 onward

#### Scenario: Discontinued index without a successor yet

- GIVEN an index is marked discontinued
- WHEN no successor has been named
- THEN the discontinuation MUST still be accepted, leaving successor resolution to fail explicitly
  rather than silently, for any period after the discontinuation

### Requirement: Successor Resolution for Later Periods

Resolving an index for a period at or after its discontinuation date MUST follow its successor
chain until it finds an index that is active for that period, or MUST report that no value can be
resolved if the chain ends without one.

#### Scenario: Resolution follows one successor hop

- GIVEN Index A is discontinued from 2027-01 naming Index B as successor
- AND Index B is active and has a published value for period 2027-02
- WHEN a clause referencing Index A is resolved for period 2027-02
- THEN the system MUST resolve it through Index B's published value

#### Scenario: Resolution before discontinuation uses the original index

- GIVEN Index A is discontinued from 2027-01 naming Index B as successor
- WHEN a clause referencing Index A is resolved for period 2026-11, before the discontinuation date
- THEN the system MUST use Index A's own published value for that period, not Index B's

---

## Capability: rent-adjustment (NEW)

### Purpose

`AdjustmentClause` records how a specific contract's canon is adjusted: which index or indices,
how they combine, and at what interval. `RentAdjustment` is the append-only history of confirmed
adjustments actually applied to a contract's canon. This capability computes and confirms; it does
not bill, collect, or notify.

### Requirement: Per-Contract Adjustment Clause

Each `AdjustmentClause` MUST belong to exactly one contract and MUST hold 1..N index references,
a combination rule (`Single` when there is exactly one index, `Average` when there are exactly
two or more), an interval expressed in whole months, and a rounding rule. The interval MUST be
read from the clause and MUST NOT be assumed to be six months for any contract that does not
explicitly say so.

#### Scenario: IPC/RIPTE semiannual clause

- GIVEN a contract's lease states the canon is adjusted every six months by the average of IPC and
  RIPTE
- WHEN its `AdjustmentClause` is recorded
- THEN it MUST hold references to IPC and RIPTE, combination `Average`, and interval 6 months

#### Scenario: Single-index clause on another contract

- GIVEN a different contract's lease adjusts only by ICL, quarterly
- WHEN its `AdjustmentClause` is recorded
- THEN it MUST hold a reference to ICL alone, combination `Single`, and interval 3 months

### Requirement: Adjustment Clause Is Optional

A `Contract` MAY have no `AdjustmentClause` at all. A contract with no clause MUST be treated as a
fixed-price lease: its canon MUST NOT be proposed for adjustment and MUST NOT appear on the due
adjustment worklist.

#### Scenario: Fixed-price contract has no clause

- GIVEN a contract was signed at a fixed monthly rent with no adjustment language
- WHEN no `AdjustmentClause` is recorded for it
- THEN the contract MUST never appear on the due adjustment worklist
- AND its canon MUST only change through an explicit, manually recorded change

### Requirement: Coefficient Is the Average of Percentage Variations, Never of Raw Levels

For an `Average` clause, the coefficient MUST be computed as the average of the **percentage
variations** of each referenced index over the adjustment period. The coefficient MUST NOT be
computed by averaging the indices' raw published levels. IPC is a dimensionless index number and
RIPTE is an amount in pesos; averaging their levels directly produces a value with no economic
meaning, even though it does not raise an error.

#### Scenario: Semiannual IPC/RIPTE average, worked example

- GIVEN IPC rose 18% over the semester and RIPTE rose 12% over the same semester
- AND the contract's canon before the adjustment is $450,000
- WHEN the coefficient is computed for the clause's `Average` combination
- THEN the coefficient MUST equal the average of the two variations, 15%
- AND the proposed new canon MUST be $517,500

#### Scenario: Single-index clause needs no averaging

- GIVEN a clause references ICL alone with combination `Single`
- AND ICL rose 10% over the clause's interval
- WHEN the coefficient is computed
- THEN the coefficient MUST equal ICL's own variation, 10%, with no averaging step

### Requirement: Next Adjustment Date Is Derived

A contract's next adjustment due date MUST NOT be a free-standing stored field. It MUST be derived
as the effective date of the contract's last confirmed `RentAdjustment` plus the clause's interval
in months, or, when no adjustment has ever been confirmed, as the contract's `StartDate` plus the
interval. Because rent is charged per whole month with no proration, a derived due date MUST fall
on the first day of a monthly period.

#### Scenario: First due date before any adjustment

- GIVEN a contract started 2026-01-15 with a 6-month interval clause and no confirmed adjustments
- WHEN its next due date is derived
- THEN it MUST be 2026-07-01

#### Scenario: Due date after a confirmed adjustment

- GIVEN a contract's last confirmed adjustment took effect 2026-07-01 on a 6-month interval clause
- WHEN its next due date is derived
- THEN it MUST be 2027-01-01, independent of when the confirmation itself was recorded

### Requirement: Due Adjustment Worklist

The system MUST present a worklist of contracts whose derived next adjustment due date has
arrived, each with its coefficient and proposed new canon pre-computed from currently available
index values. A contract MUST leave the worklist only when its adjustment is confirmed, not merely
because it was viewed.

#### Scenario: Contract appears once its due date arrives

- GIVEN a contract's next adjustment due date is today
- WHEN the worklist is opened
- THEN the contract MUST appear with its pre-computed coefficient and proposed canon

#### Scenario: Viewing does not resolve the worklist entry

- GIVEN a due contract appears on the worklist
- WHEN the operator opens and closes it without confirming
- THEN the contract MUST still appear on the worklist afterward

### Requirement: Missing Index Value Leaves the Adjustment Pending

When an adjustment falls due and a required index value is not yet available, the system MUST NOT
invent a value, MUST NOT silently skip the period, and MUST NOT compute an average from a partial
set of index values. The adjustment MUST stay due and pending, the contract's current canon MUST
keep applying unchanged, and the contract MUST remain on the worklist.

#### Scenario: One of two indices missing blocks the average

- GIVEN a contract's clause averages IPC and RIPTE
- AND IPC's value for the due period has been published but RIPTE's has not
- WHEN the worklist is computed
- THEN the adjustment MUST remain pending with no proposed canon
- AND the contract's current canon MUST be unaffected

#### Scenario: Pending adjustment does not disappear

- GIVEN an adjustment is pending for a missing index value
- WHEN a following billing cycle runs before the value is entered
- THEN the contract MUST still be on the worklist and the previous canon MUST still apply

### Requirement: Operator Confirmation Preceded by a Summary

An adjustment MUST NOT change a contract's canon until an operator explicitly confirms it. Before
confirmation, the system MUST present a summary showing the previous canon, the index values used
and their periods, the computed coefficient, and the resulting new canon.

#### Scenario: Summary shown before confirmation

- GIVEN a due adjustment has a pre-computed coefficient of 15% and a proposed canon of $517,500 from
  a previous canon of $450,000
- WHEN the operator opens it to confirm
- THEN the system MUST display the previous canon, the IPC and RIPTE values and periods used, the
  15% coefficient, and the $517,500 result before accepting confirmation

#### Scenario: No silent application

- GIVEN a due adjustment with a pre-computed proposal exists
- WHEN no operator has confirmed it
- THEN the contract's canon MUST remain unchanged

### Requirement: Whole-Peso Canon, Truncated, With No Commercial Rounding

The confirmed adjustment MUST set the canon to the computed amount **truncated to a whole peso**.
The system MUST NOT charge centavos, and MUST NOT apply commercial rounding to a tidier figure
(e.g. lifting $517,483 to $517,500).

Whole pesos is evidence, not preference. Every figure on the agency's real receipt is a whole peso
— alquiler 507,000; tasa municipal 28,900; recargo 91,260; neto 650,542.00 — and the owner
receipt's honorarios line reads 47,860 where the exact arithmetic gives 47,860.80. The agency
already truncates. Argentine cent coins no longer circulate, so a centavo on an invoice names an
amount nobody can hand over in cash.

Truncating rather than rounding is a deliberate team decision inferred from that single honorarios
line. It is on the list to confirm with the agency owner; one peso either way changes nothing
material, so the decision was taken rather than left blocking.

#### Scenario: Computed amount truncated to a whole peso

- GIVEN a computed new canon of $517,483.73
- WHEN the adjustment is confirmed
- THEN the contract's canon MUST become exactly $517,483

#### Scenario: Truncation never rounds up

- GIVEN a computed new canon of $47,860.80
- WHEN the adjustment is confirmed
- THEN the stored canon MUST be $47,860, never $47,861

#### Scenario: No commercial rounding to a tidier figure

- GIVEN a computed new canon of $517,483
- WHEN the adjustment is confirmed
- THEN the canon MUST remain $517,483, and MUST NOT be lifted to $517,500 or any rounder number

### Requirement: Monetary Storage Uses Decimal, Never Floating Point

Every monetary column MUST be `numeric(14,2)` and every monetary computation MUST use `decimal`
arithmetic, never `float` or `double`. Storage precision and the whole-peso charging rule are
different things and coexist: the column can hold centavos, and the canon written into it simply
never has any.

Intermediate values during a computation MAY carry more precision than two decimals; truncation to
a whole peso happens once, at the point the canon is set.

#### Scenario: Intermediate precision is not lost mid-calculation

- GIVEN a coefficient of 1.15 applied to a canon of $450,000 across a two-index average whose
  variations carry several decimal digits
- WHEN the new canon is computed
- THEN the intermediate arithmetic MUST stay in `decimal`, and truncation to a whole peso MUST
  happen only when the result is assigned to the contract

### Requirement: The Pending Worklist Is Reminder-Ready

The due-adjustment worklist MUST be queryable in a form a reminder can consume without any
knowledge of this capability's internals: for each contract whose adjustment is due, it MUST expose
which index and which period the system is still missing.

A reminder can then say *"3 contracts are waiting for August's IPC"* rather than firing a blind
calendar alert every month. It only speaks when there is genuinely something to do, and it names
what. The data already exists as a consequence of the worklist requirement above; this requirement
only fixes that it MUST be reachable from outside.

**Delivering that reminder is the notifications change, not this one.** This change owes the
notifications change the query; it does not send anything, and it does not schedule anything.
Delivery also depends on the open `scheduler-mechanism` decision, because a closed desktop
application sends nothing.

Detecting that INDEC, BCRA or the Ministry of Labour *has* published a value is deliberately NOT
specified here. Knowing a value was published requires reading the publishing source, and anything
that can read it can read the number too — that is the automatic-retrieval change, not a cheaper
halfway step.

#### Scenario: Worklist names the missing index and period

- GIVEN three contracts whose adjustment fell due and whose clauses reference IPC for period 2026-08
- AND that no IPC value for 2026-08 has been entered
- WHEN the pending worklist is queried
- THEN it MUST return those three contracts, each naming IPC and the period 2026-08 as missing

#### Scenario: Worklist is empty once the value arrives

- GIVEN the same three contracts, and the IPC value for 2026-08 subsequently entered
- WHEN the pending worklist is queried again
- THEN none of the three MUST appear as waiting on a missing value

### Requirement: A Late Adjustment Is Not Charged Retroactively

When an adjustment is confirmed after its due date has already passed, the system MUST NOT
generate retroactive charges for the months the previous canon was undercharged during the delay.
The new canon MUST apply from the next billable period after confirmation onward, with the tenant
notified of the new figure.

#### Scenario: Adjustment confirmed three months late

- GIVEN an adjustment fell due 2026-07-01 but was not confirmed until 2026-10-01
- WHEN it is confirmed
- THEN no charge MUST be generated for July, August, or September at the new canon
- AND the new canon MUST apply starting from the next billable period after 2026-10-01

#### Scenario: Correct canon still resumes going forward

- GIVEN the same late-confirmed adjustment from the prior scenario
- WHEN a billing cycle runs for the period after confirmation
- THEN it MUST use the new, corrected canon, not the stale previous one

### Requirement: Append-Only Adjustment History

Each confirmed `RentAdjustment` MUST be recorded as a new row holding the effective date, previous
canon, new canon, coefficient, and the index values used. Confirmed adjustments MUST NOT be
edited or deleted; the history MUST remain reproducible so that any past canon can be reconstructed
without recomputation.

#### Scenario: Confirmed adjustment recorded in full

- GIVEN an adjustment is confirmed with previous canon $450,000, coefficient 15%, new canon $517,500
- WHEN it is recorded
- THEN a `RentAdjustment` row MUST store the effective date, $450,000, $517,500, 15%, and the
  IPC and RIPTE values and periods used

#### Scenario: History answers the canon for a past month

- GIVEN a contract has three confirmed adjustments over its lifetime
- WHEN the canon in force for a specific past month is requested
- THEN it MUST be answerable from the stored adjustment history alone

### Requirement: A Correction Produces a New Adjustment, Never a Rewrite

When a previously used index value is corrected after an adjustment was already confirmed with it,
the system MUST NOT alter the confirmed `RentAdjustment` row. It MUST instead produce a new,
separate correcting `RentAdjustment` that reflects the corrected value, appended to the history.

#### Scenario: Index correction after confirmation

- GIVEN a `RentAdjustment` was confirmed using an IPC value of 18% for a period
- AND that IPC value is later corrected to 17.5%
- WHEN the correction is applied
- THEN the original confirmed `RentAdjustment` row MUST remain unchanged
- AND a new correcting `RentAdjustment` MUST be appended reflecting the corrected coefficient

---

## Capability: lease-contract (MODIFIED)

This is a delta against `openspec/specs/lease-contract/spec.md`. It adds one requirement and
modifies one existing requirement; all other requirements in that living spec are unaffected and
MUST continue to hold exactly as written.

### ADDED Requirement: Contract May Reference an Adjustment Clause

A `Contract` MUST support an optional reference to a single `AdjustmentClause` describing how its
canon is adjusted. A contract with no clause is a valid fixed-price lease, per the
`rent-adjustment` capability's optionality requirement above.

#### Scenario: Contract with a clause

- GIVEN a contract whose lease states a semiannual IPC/RIPTE average adjustment
- WHEN the contract is recorded with its `AdjustmentClause`
- THEN the contract MUST expose that clause for due-date derivation and coefficient computation

#### Scenario: Contract without a clause

- GIVEN a contract whose lease has no adjustment language
- WHEN the contract is recorded with no `AdjustmentClause`
- THEN the contract MUST be valid and MUST be treated as fixed-price

### MODIFIED Requirement: Share Stored as Percentage, Never Amount

`SharePercentage` MUST be stored as a percentage, never as a fixed monetary amount, so it survives
a change to the total canon (adjusted semiannually by IPC/RIPTE, per the `rent-adjustment`
capability) without recalculation. This requirement now applies specifically to canon changes that
originate from a confirmed `RentAdjustment`, not only to an arbitrary manual `ChangeMonthlyRent`
call, because a confirmed adjustment is the mechanism that actually reaches this code path in
production.

(Previously: this requirement described only a generic canon change; it now names the confirmed
adjustment as the real-world trigger and adds a scenario asserting it holds under that trigger
specifically.)

#### Scenario: Canon change leaves percentages untouched

- GIVEN a two-unit contract with shares 50% and 50%, and canon $100,000
- WHEN the contract's total canon is changed to $150,000
- THEN the stored shares MUST remain 50% and 50%
- AND MUST still sum to exactly 100%

#### Scenario: Confirmed adjustment leaves percentages untouched

- GIVEN a two-unit contract with shares 60% and 40%, and canon $450,000
- WHEN a `RentAdjustment` is confirmed raising the canon to $517,500
- THEN the stored shares MUST remain 60% and 40%
- AND MUST still sum to exactly 100%, with no recalculation triggered by the adjustment

---

## Out of Scope

Each item below is *not in this pull request*, not *excluded from the product*. It names the
change that owns it:

- **Automatic index retrieval** — a later, separate change; this change stores index values
  entered manually, in the same table an automation change would later fill
- **Current account, debt, and recargo** (late-payment surcharge) — the current-account change
- **Collection, receipts, and PDF output** — the collection change
- **Notifications that an adjustment is due or overdue** — the notifications change
- **Users and roles** — the users-and-roles change

---

## Tests

Derived from the rules above, not generic CRUD coverage. Each names the requirement it proves.
Integration tests require a real Postgres (`postgres:17.6` via Testcontainers); constraints and
triggers cannot be validated against a fake.

1. **Averaging variations, not levels.** IPC rises from 8,000 to 9,440 (+18%) and RIPTE from
   1,200,000 to 1,344,000 (+12%) over the period; the coefficient MUST be 15%, and a canon of
   450,000 MUST become 517,500. A test that would pass when averaging raw levels MUST fail.
2. **Single-index clause skips averaging.** An ICL-only clause yields ICL's own variation, with no
   averaging step.
3. **Truncation to a whole peso.** A computed 517,483.73 stores 517,483.
4. **Truncation never rounds up.** A computed 47,860.80 stores 47,860, never 47,861. This is the
   exact figure observed on the agency's real owner receipt.
5. **No commercial rounding.** A computed 517,483 stays 517,483 and is never lifted to 517,500.
6. **Decimal, never float.** Monetary columns are `numeric(14,2)`; a computation carrying several
   decimal digits truncates once, at assignment, not progressively.
7. **Missing index leaves the adjustment pending.** With no value entered, the adjustment stays due,
   the previous canon keeps applying, and no value is invented.
8. **A partial index set is never averaged.** With IPC present and RIPTE absent for the period, the
   system MUST NOT compute a coefficient from the one value it has.
9. **Worklist names the missing index and period.** Three contracts awaiting IPC 2026-08 are
   returned, each naming that index and period.
10. **Worklist clears once the value arrives.** After IPC 2026-08 is entered, none of the three
    appears as waiting.
11. **Late adjustment charges nothing retroactively.** An adjustment confirmed two periods late
    generates no charge for the periods that were undercharged.
12. **Late adjustment does resume the correct canon.** The same adjustment sets the canon from the
    next billable period onward.
13. **Adjustment history is append-only.** A confirmed adjustment cannot be updated or deleted;
    enforced at the database level, proven by a raw SQL attempt, not only through the service.
14. **A corrected index value produces a new adjustment.** Correcting a value already used by a
    confirmed adjustment leaves that adjustment intact and records a new correcting one.
15. **Index supersession resolves.** An index discontinued after a period resolves to its named
    successor for later periods.
16. **A contract with no adjustment clause never becomes due.** It never appears on the worklist.
17. **Shares survive an adjustment.** A two-unit contract at 60/40 keeps those percentages after a
    confirmed adjustment, still summing to exactly 100%. This is the existing `lease-contract`
    invariant, and this change is the first thing that exercises it for real.
18. **Per-contract interval is honoured.** A contract with a non-semiannual interval becomes due on
    its own schedule, proving the interval is never assumed.
