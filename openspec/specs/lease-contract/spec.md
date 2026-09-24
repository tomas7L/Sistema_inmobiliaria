# Lease Contract Specification

## Purpose

`Contract` records a signed lease: its lifecycle state and dates, its total rent (**canon**), its
honorarios (agency fee) rate, its party roles with their multiplicity, and — for multi-unit
leases — the per-unit rent split. Rent index adjustment math and collection are out of scope;
canon and honorarios are stored values only.

## Requirements

### Requirement: Party Role Multiplicity

The system MUST support 1..N Lessors (**locadores**), exactly 1 Tenant (**locatario**), and 0..N
Co-debtors (**codeudores**, solidarily liable for the whole debt) per contract, via role
assignments with no hardcoded upper bound.

#### Scenario: Condominio — two lessors

- GIVEN a contract is being recorded for a jointly owned unit
- WHEN two lessors are assigned to it (e.g. "VICO LESLIE" and "VICO ALEJANDRA")
- THEN both assignments MUST be accepted

#### Scenario: Exactly one tenant enforced

- GIVEN a contract already has one Tenant assigned
- WHEN a second Tenant assignment is attempted on the same contract
- THEN the system MUST reject it

#### Scenario: Zero or many codebtors

- GIVEN a contract with no codebtors
- WHEN it is saved
- THEN it MUST be accepted
- AND a contract with two codebtors MUST also be accepted with no upper bound enforced

### Requirement: Role-Scoped Uniqueness

The uniqueness key for a party assignment MUST include Role, so one `Party` MAY hold two
different roles on the same contract, but the same Party+Role pair MUST NOT repeat.

#### Scenario: Same party as lessor and codebtor

- GIVEN a Party is already a Lessor on a contract
- WHEN the same Party is also assigned as Codebtor on that same contract
- THEN both assignments MUST be accepted

#### Scenario: Duplicate Party+Role rejected

- GIVEN a Party is already assigned as Codebtor on a contract
- WHEN the same Party is assigned as Codebtor again on that same contract
- THEN the system MUST reject the duplicate

### Requirement: Codebtor Terminology

The system MUST use "Codebtor" (**codeudor**) for the solidary co-debtor role. "Garante"
(guarantor — a distinct liability regime) MUST NOT appear in role names, enum values, or
generated artifacts.

#### Scenario: Role enum has no Garante value

- GIVEN the set of valid party roles
- WHEN it is inspected
- THEN it MUST contain only Lessor, Tenant, Codebtor
- AND MUST NOT contain any value named or derived from "Garante"

### Requirement: No Draft State

Contract lifecycle states MUST be limited to `Active`, `PendingTermination`, and `Ended` (with a
reason). There MUST NOT be a pre-signature/Draft state, since the lease is signed on paper and
recorded afterward.

#### Scenario: New contract recorded directly as Active

- GIVEN a lease has already been signed
- WHEN it is recorded in the system
- THEN it MUST be created in `Active` state, never in a Draft state

#### Scenario: Future start date still Active

- GIVEN a lease signed today has a `StartDate` next month
- WHEN it is recorded
- THEN its state MUST be `Active` (the future `StartDate` alone represents "signed, not yet
  started")

### Requirement: Past Nominal End Date Is Not Ended

A contract whose `NominalEndDate` has passed MUST remain `Active` unless a party has actively
initiated termination. Continuation past `NominalEndDate` MUST NOT auto-transition the contract
to `Ended` (no **tácita reconducción** — the lease continues on the same terms, not via automatic
renewal).

#### Scenario: Contract past nominal end date without termination

- GIVEN a contract's `NominalEndDate` is in the past
- AND no `NoticeGivenDate` or termination has been recorded
- WHEN its state is evaluated
- THEN it MUST remain `Active`
- AND it MUST be reported as the governing contract of its unit

#### Scenario: Termination initiated after nominal end date

- GIVEN a contract's `NominalEndDate` is in the past and it is still `Active`
- WHEN a party serves notice, setting `NoticeGivenDate` and `PlannedMoveOutDate`
- THEN the contract MUST transition to `PendingTermination`

### Requirement: Lifecycle Dates

`Contract` MUST store `StartDate`, `NominalEndDate`, `NoticeGivenDate` (nullable until notice is
given), `PlannedMoveOutDate` (nullable until notice is given), and `ActualEndDate` (nullable
until ended).

#### Scenario: Contract created with only start and nominal end dates

- GIVEN a new contract is recorded
- WHEN only `StartDate` and `NominalEndDate` are provided
- THEN `NoticeGivenDate`, `PlannedMoveOutDate`, `ActualEndDate` MUST be null

### Requirement: Ended Requires a Reason

A contract transitioning to `Ended` MUST record an end reason (expiry, early termination by
tenant, termination for cause, or mutual agreement).

#### Scenario: Ending without a reason rejected

- GIVEN an Active or PendingTermination contract
- WHEN it is set to `Ended` without a reason
- THEN the system MUST reject the transition

### Requirement: Multi-Unit Coverage With a Single Stored Canon

A `Contract` MUST support 1..N units via `ContractUnit`. A single-unit contract is the N=1 case
at 100% share — there is no separate "multi-unit mode". The total rent (**canon**) MUST be stored
once, on `Contract`.

#### Scenario: Single-unit contract is implicitly 100%

- GIVEN a contract covers exactly one unit
- WHEN it is saved
- THEN it MUST have one `ContractUnit` row with `SharePercentage = 100%`

#### Scenario: Multi-unit contract splits shares

- GIVEN a contract covers two units
- WHEN shares are assigned
- THEN two `ContractUnit` rows MUST exist, one per unit

### Requirement: Shares Must Sum to Exactly 100%

The sum of `SharePercentage` across all `ContractUnit` rows of a contract MUST equal exactly
100%. A split that does not sum to 100% MUST be rejected.

#### Scenario: Valid split accepted

- GIVEN a two-unit contract
- WHEN shares of 60% and 40% are assigned
- THEN the split MUST be accepted

#### Scenario: Invalid split rejected

- GIVEN a two-unit contract
- WHEN shares of 60% and 30% are assigned
- THEN the system MUST reject the split

### Requirement: A Unit Appears at Most Once in a Split

A unit MUST NOT appear more than once in a contract's rent split. This MUST be checked before the
100% sum rule, because two 50% rows for the same unit total exactly 100% and would otherwise pass.

The composite primary key on `contract_units` rejects this case as well, but only at save time and
with an opaque database error. The domain check exists so the caller learns which rule it broke
while it can still correct the input.

#### Scenario: Same unit listed twice rejected

- GIVEN a contract and a single unit
- WHEN shares of 50% and 50% are assigned, both to that same unit
- THEN the system MUST reject the split, even though the percentages total 100%

### Requirement: A Contract Covers at Least One Unit From Creation

A `Contract` MUST NOT exist without at least one unit. The rent split MUST be supplied when the
contract is constructed, so a lease that leases nothing cannot be represented even in memory.

The database cannot enforce this rule. The rent-split constraint trigger is row-level and never
fires for a contract with no `contract_units` rows at all, so a contract with zero units is
database-legal. The domain is therefore the only place this rule can live, and it MUST actually
enforce it rather than merely be documented as doing so.

#### Scenario: Contract constructed without units rejected

- GIVEN a request to create a contract
- WHEN no unit shares are supplied
- THEN construction MUST be rejected

#### Scenario: Contract constructed with a valid split accepted

- GIVEN a request to create a contract covering one unit at 100%
- WHEN it is constructed
- THEN the contract MUST be created Active with one `ContractUnit` row

### Requirement: Contract May Reference an Adjustment Clause

A `Contract` MUST support an optional reference to a single `AdjustmentClause` describing how its canon is adjusted. A contract with no clause is a valid fixed-price lease.

#### Scenario: Contract with a clause

- GIVEN a contract whose lease states a semiannual IPC/RIPTE average adjustment
- WHEN the contract is recorded with its `AdjustmentClause`
- THEN the contract MUST expose that clause for due-date derivation and coefficient computation

#### Scenario: Contract without a clause

- GIVEN a contract whose lease has no adjustment language
- WHEN the contract is recorded with no `AdjustmentClause`
- THEN the contract MUST be valid and MUST be treated as fixed-price

### Requirement: Share Stored as Percentage, Never Amount

`SharePercentage` MUST be stored as a percentage, never as a fixed monetary amount, so it survives a change to the total canon (adjusted semiannually by IPC/RIPTE) without recalculation. This requirement applies specifically to canon changes that originate from a confirmed `RentAdjustment`, not only to an arbitrary manual `ChangeMonthlyRent` call, because a confirmed adjustment is the mechanism that actually reaches this code path in production.

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

### Requirement: Deterministic Residue on Equal Split

When units are split equally and the total does not divide evenly, the residue MUST be assigned
deterministically: the largest resulting share absorbs the residue; when shares are tied, the
lowest unit id receives it. All share values MUST use `decimal` arithmetic, never floating point.

#### Scenario: Equal three-way split with residue

- GIVEN a contract covers three units with an equal-split request
- WHEN 100% is divided by 3
- THEN the unit with the lowest unit id among the tied shares MUST receive the residual hundredth
- AND the three shares MUST still sum to exactly 100%

### Requirement: Split Persisted, Not Re-Derived Monthly

The stored `SharePercentage` values MUST persist unchanged across billing cycles. They MUST NOT
be recalculated automatically each month; only an explicit change to the contract's units or
total canon reopens the split for edit.

#### Scenario: Split reused across months

- GIVEN a contract has a saved split
- WHEN two consecutive monthly liquidations reference that contract
- THEN both MUST read the same stored split, unchanged

#### Scenario: Adding a unit reopens the split

- GIVEN a contract has a saved split for two units
- WHEN a third unit is added to the contract
- THEN the split MUST be re-opened for an explicit edit before it is valid again

### Requirement: Honorarios Percentage Is Stored, Nullable

`Contract` MUST store a nullable honorarios (agency management fee) percentage rate. This
capability stores the value only; it does not compute fees.

> **"Honorarios" names TWO different things in this agency. This requirement is about the first.**
>
> 1. **Honorarios de administración** — a **PERCENTAGE** of the rent (8%, 6%, it varies by owner),
>    paid by the **owner**, **every month** for the life of the contract, printed on the owner's
>    part of the receipt. This requirement, and every other use of "honorarios" written before
>    2026-09-23, means this one.
> 2. **Honorarios contractuales** — a fixed **AMOUNT** for handling the contract, paid by the
>    **tenant**, **once**, in **1 to 6 fixed instalments** that never adjust. Paying in full is
>    simply one instalment, not a separate case. It is printed on the tenant's part of the monthly
>    receipt, never the owner's. Confirmed 2026-09-23 and **not modelled anywhere yet**; it belongs
>    to the collection change.
>
> The contrast that matters: the owner's is a **rate** that rides the rent upward as it adjusts;
> the tenant's is an **amount** fixed at signing that never moves. Storing either one the other
> way round would be wrong.
>
> Anything added later MUST disambiguate which of the two it means. See
> `openspec/domain/respuestas-del-dueno.md`.
>
> **OPEN — where the administration rate belongs.** It is stored here, on `Contract`, because the
> administration mandate is cláusula DÉCIMA SEXTA *of the lease itself* rather than a separate
> agreement. But the rate is negotiated with the **owner**, so an owner holding several contracts
> carries the same rate copied into each one with nothing binding the copies together. The team is
> reviewing this. It is recorded as an open question, not a settled decision, and may change.

#### Scenario: Honorarios unassigned

- GIVEN a contract is recorded without a known honorarios rate
- WHEN it is saved
- THEN the honorarios field MUST be storable as null

#### Scenario: Honorarios rate recorded

- GIVEN a contract with an agreed honorarios rate of 8%
- WHEN it is saved
- THEN the stored value MUST be 8%

## Tests

1. **Shares survive an adjustment.** A two-unit contract at 60/40 keeps those percentages after a confirmed adjustment, still summing to exactly 100%. This is the existing lease-contract invariant, and the rent-adjustment change is the first thing that exercises it for real.
