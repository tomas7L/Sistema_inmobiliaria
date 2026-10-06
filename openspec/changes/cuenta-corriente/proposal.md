# Proposal: cuenta corriente

Written 2026-10-06, from `exploration.md` in this folder. Domain vocabulary, glossed once:
**cuenta corriente** = running account / ledger; **locatario** = tenant; **recargo** = late-payment
surcharge; **mora** = the state of being late.

---

## Intent

The agency cannot today answer, from the system, the question it is asked most: **what does this
tenant owe right now, and why**. The contract knows its canon, the adjustment history knows how that
canon changed, and nothing knows what has accrued against it or what has been paid.

This change builds that ledger. It does not take money and it does not issue receipts — those are
the collection change that follows. It answers the question; collection acts on the answer.

Two owner answers make this change smaller than it looked when it was first sketched, and both are
recorded in `openspec/domain/respuestas-del-dueno.md` §16: the receipt-only amounts do not accrue,
and a payment always settles the oldest unpaid period.

---

## Scope

### In Scope

- A ledger per contract: an append-only sequence of movements whose sum is the balance
- The movement kinds this change can raise: opening balance, rent accrual, honorarios contractuales
  instalment
- Opening balances, typed by hand, one per contract, because nobody starts at zero
- The recargo projection: what is owed in surcharge as of a given date, derived rather than stored
- The rule that answers which period a payment settles: the oldest unpaid one
- An account that outlives its contract, so a rescission with a debt still owed is representable

### Out of Scope

| Not here | Where it belongs |
|---|---|
| Taking a payment, and the payment movement itself | collection |
| Receipts, their numbering, their two-part shape | collection |
| Tasa municipal, seguros, "Otros Conceptos" | collection — they are typed when a receipt is closed and never accrue (owner, 2026-09-26) |
| Liquidating to the owner, and honorarios de administración | collection |
| Notifying a tenant that they owe | notifications |
| Any screen beyond entering an opening balance and reading a balance | a later UI pass |

---

## Capabilities

### New Capabilities

- **`contract-account`** — the ledger itself: movements, the balance, the recargo projection, the
  opening balance, and the oldest-unpaid-period rule.

### Modified Capabilities

**None.** An earlier draft of this proposal added a requirement to `lease-contract` blocking a
contract from reaching `Ended` while its tenant still owed. **That requirement was wrong and is
removed** — see "Rescission leaves the account open" below.

---

## Approach

### Settled: the account belongs to the contract, not the tenant

Rent is owed under a contract, at a canon that contract defines, on a due date that contract sets.
The recargo clock starts from that contract's due date, and two contracts late by different amounts
cannot share one clock. The honorarios contractuales plan is agreed when a contract is signed and
dies when it is paid off.

And the owner's own rule decides it: *a contract cannot close while the tenant still owes.* Scoped
to a tenant, that rule cannot even be evaluated — which part of what Abregu owes belongs to this
contract and which to the other?

The agency thinks in people, and that stays true. It is a question about **presentation**: a tenant
view sums the accounts of that tenant's contracts. The reverse has no clean shape.

### Settled: an append-only ledger, with the balance as the sum

The usual argument for deriving a balance instead of storing movements is that a past figure might
change. **In this system a past figure cannot change.** `specs/rent-adjustment/spec.md:186` already
requires that a late adjustment generate no retroactive charge: the canon in force when a period
accrued is the canon that period is owed at, permanently.

Two more facts point the same way. Only total payments exist, so there is no partial amount to
allocate across debts. And the project already forbids rewriting history
(`specs/rent-adjustment/spec.md:203`) and requires a correction to be a new row (`:234`). A ledger
that behaved differently would be the odd one out.

A correction is therefore a new, signed movement referencing what it corrects — the shape
`RentAdjustment` already uses.

### Settled: recargo is derived, never a stored movement

2% per day, simple, from the day of mora, **with no cap** (owner, round 1). It grows every day, so a
contract three months late would otherwise generate ninety rows that say nothing.

It is computed when asked, from the amount owed, the due date, and the date of the question. In
**this** change it is only ever a projection. It becomes a stored movement at exactly one instant —
when it is charged on a receipt — and that instant belongs to collection.

`RECARGO = alquiler × 2% × (día_pago − 10)` was verified against the agency's real receipts during
the first change. The 10 is the contract's due day, not a constant.

**There is a second instant, and it is not a special case of the first.** When a contract is
rescinded and both parties sign a payment plan, that signature is a *novación* — the original
obligation is **extinguished and replaced**, not suspended. The recargo accrued up to the signing
date is final from that day, and nothing accrues afterwards, because the debt it was accruing
against no longer exists.

The distinction matters for the model. "Pause the counter" would be a flag, and a flag can be
cleared, forgotten, or asked "paused since when?" with no answer. A *novación* is **an event with a
date**, so the frozen figure is written into the ledger as a dated movement like any other, and the
account can still be read correctly for any date before it.

What this change owes: the recargo projection must take the freeze into account, and the freeze must
be recordable as a movement. **The payment plan itself — its instalments, its schedule, what happens
when somebody defaults on it — stays out of scope** and belongs to its own change.

**But one constraint from that future change lands here, and ignoring it would mean rewriting this
one.** The recargo calculation MUST take its rate and its grace period **from the obligation it is
measuring**, never from a constant. Today that obligation is a lease and the figures are 2% per day
from the contract's due day. Tomorrow it is a payment plan, whose terms are whatever the two parties
signed — and the agency owner decides those at signing, because he knows the person and an
aggressive rate on somebody who already could not pay recovers nothing.

This is the house pattern, not a new idea: the adjustment clause lives on the contract because each
contract signed its own, the honorarios percentage lives on the contract because it was negotiated
with that owner, and the rent split lives on the contract for the same reason. What the paper says,
the record carries. A payment plan is a signed paper.

Modelled this way from the start, the payment-plan change supplies its own figures and never has to
touch the recargo calculation.

### Settled: rescission leaves the account open

**A contract can be rescinded while money is still owed.** The tenant moves out, the debt remains,
and a payment plan is arranged for it. The account outlives the contract.

An earlier draft of this proposal had the opposite rule, built from the owner's sentence *"se
cerrará contrato luego de pagar todo lo que adeuda."* That sentence describes **what the agency
tries to achieve**, not a constraint the software should enforce, and reading it as an invariant was
a mistake. It is the third time on this project that a sentence was hardened into a rule nobody
asked for; the first two were the Empleado's contract-termination block and the clause reserving
money operations to the Admin, and both were removed for the same reason.

Blocking `End` would have made the system state something false: a contract reading `Active` for a
tenant who has already moved out. A system that cannot represent a rescission with a debt cannot
represent what actually happens.

What this means here:

- `Contract.End(...)` is **not modified**. It stays exactly as it is.
- The account does **not** close when its contract ends. It keeps accepting payments until the debt
  reaches zero.
- A contract that ended with nothing owed has an account at zero, and nothing is ever charged to it
  again. Ending is not what closes an account — a zero balance with no further accrual is.

**The payment plan itself is OUT OF SCOPE here.** Agreeing a schedule of future instalments against a
rescinded contract's debt is its own concept, with its own rules about who agrees it, what happens
when somebody defaults on the plan, and how it interacts with recargo. It is named in the open
questions below and belongs to a later change. This one only guarantees the account survives so a
plan has something to be written against.

### The design questions this change hands to `sdd-design`

1. **Where the balance is computed.** Summing movements on every read is correct and, at this
   scale — two people, a few dozen contracts — almost certainly fast enough. A cached running total
   is an optimisation with a staleness cost. Design decides; the proposal's position is: do not cache
   until something is measured.
2. **How a rent accrual is raised.** Monthly, by something that runs; or lazily, the first time a
   period is read. The second needs no scheduler, and `scheduler-mechanism` is still an open project
   decision in `openspec/config.yaml`. That is a real argument and design should weigh it.
3. **How the oldest-unpaid-period rule is exposed** so collection can call it without reaching into
   the ledger's internals.
4. **What an account looks like once its contract has ended.** It must keep accepting payments, and
   it must stop accruing rent. Whether that is a state on the account, or simply the absence of
   further accrual because no period comes due, is a modelling decision — the second is simpler and
   has no flag to get wrong.

---

## Affected Areas

| Area | Action | What |
|---|---|---|
| `src/Inmobiliaria.Domain/Accounts/` | Create | The account, its movements, the movement kinds, the recargo projection |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/` | Create | The EF configuration for the ledger |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/` | Create | One migration, shipped **with** its model changes — never split |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modify | The new DbSets |
| `openspec/specs/contract-account/` | Create | The new capability |
| `tests/Inmobiliaria.Domain.Tests/` | Create | Ledger arithmetic, recargo, the oldest-period rule |
| `tests/Inmobiliaria.Infrastructure.Tests/` | Create | Schema constraints, GRANTs for the new tables |

**Not touched:** `rent-adjustment` and its migration, `access-control`'s existing grants beyond
adding the new tables, and anything in `Inmobiliaria.Desktop`.

---

## Settled Decisions

| # | Decision | Source |
|---|---|---|
| 1 | The account belongs to the **contract**. A tenant view sums the contracts beneath it. | TEAM, from the owner's contract-closure rule |
| 2 | An **append-only ledger**; the balance is the sum of its movements. A correction is a new signed movement. | TEAM, matching `rent-adjustment`'s existing shape |
| 3 | **Recargo is derived**, never stored as a daily movement. Uncapped, 2% simple per day from mora. | CONFIRMED BY USER (round 1) |
| 4 | **Every account opens with a typed figure**, one per contract, as a distinct movement kind so nobody mistakes it for something the system computed. Most carry a debt; some are at zero because that tenant is up to date. The agency already keeps the numbers in a spreadsheet, so loading is transcription, not reconstruction. | CONFIRMED BY USER (2026-10-06) |
| 5 | **Only total payments.** No partial payment, no part-paid period, no allocation of a fraction across debts. | CONFIRMED BY USER |
| 6 | **A payment settles the oldest unpaid period.** The operator does not choose. | CONFIRMED BY USER (2026-09-26) |
| 7 | **Tasa municipal, seguros and "Otros Conceptos" do not accrue.** They are typed when a receipt is closed and belong to collection. | CONFIRMED BY USER (2026-09-26) |
| 8 | **A contract CAN be rescinded while money is still owed, and the account outlives it.** A payment plan is then arranged against the remaining debt. An earlier draft of this proposal had the opposite rule, hardened from a sentence that described the agency's intention rather than a system constraint; it is removed. `Contract.End(...)` is not modified. | CONFIRMED BY USER (2026-10-06, correcting the draft) |
| 9 | The migration and its EF model changes ship in **one** pull request. EF refuses to migrate when the model has changes no migration covers — proven during `users-and-roles`, where splitting them made CI red in a way no code could fix. | VERIFIED FACT |
| 10 | **Signing a payment plan is a *novación*: it stops the recargo on the signing date.** Both parties sign when they rescind, and the original obligation is **replaced** rather than suspended. The recargo accrued up to that day is final and becomes part of what the plan covers; nothing accrues against the old obligation afterwards, because there is no longer an old obligation. | CONFIRMED BY USER (2026-10-06) |
| 11 | **The recargo calculation takes its rate and grace period from the obligation it measures, never from a constant.** A lease supplies 2% per day from its own due day; a payment plan supplies whatever its parties signed. The agency owner sets a plan's terms at signing, because he knows the person. Same pattern as the adjustment clause, the honorarios percentage and the rent split: what the paper says, the record carries. | CONFIRMED BY USER (2026-10-06) |

---

## Assumptions

1. ~~**A tenant never pays a contract that has already ended.**~~ **Wrong, and now inverted into a
   requirement.** A tenant pays against an ended contract for as long as they owe — that is the
   normal path after a rescission with a debt. Once the balance reaches zero nothing further is ever
   charged. This was written as an assumption derived from a rule that was itself wrong; it is now
   in scope, with a test.
2. **An opening balance is one figure per contract, not a reconstruction of its history.** The agency
   types what is owed today; the system does not need to know which months it came from. If they need
   the breakdown, this grows.
3. **A period accrues on its due date**, not on the first day of the month it covers. The real
   receipts show a due day of 10, and the recargo formula counts from it.

---

## Open Questions

**For the owner — neither blocks this change starting:**

1. ~~**Does the agency track a running account today at all?**~~ **ANSWERED 2026-10-06.** They keep a
   spreadsheet of what each tenant owes, so loading is transcription rather than reconstruction.

   One practical item it leaves behind, for whoever runs the load rather than for this design: that
   spreadsheet is keyed by **tenant**, while an account here belongs to a **contract**. They coincide
   for a tenant with one contract. For a tenant holding two, somebody decides how the figure splits,
   and it should not be discovered on the day.
2. ~~**Does the recargo keep running under a payment plan?**~~ **ANSWERED 2026-10-06 — see Decision
   10.** It stops, and it stops because the obligation is replaced, not because a counter is paused.

**Tracked elsewhere, and blocking collection rather than this change:**

3. The exact current receipt number, where the administration honorarios rate belongs, and whether
   the two-part receipt is one sheet per party or one in total — all in
   `openspec/domain/respuestas-del-dueno.md`.

---

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| The boundary with collection blurs and neither change closes | Medium | The boundary is stated as a settled decision, not an intention: this capability owns what is owed; collection owns the payment and the document. The payment movement is the seam. |
| Recargo is modelled as a stored movement by reflex | Medium | Decision 3 states it, and a test must assert that no movement row is created by the passage of time. |
| ~~Recargo runs unbounded while a payment plan is honoured~~ | — | **Resolved.** Signing the plan is a *novación*: the recargo stops on the signing date. See Decision 10. |
| The recargo freeze is modelled as a flag on the contract rather than as a dated movement | Medium | Decision 10 makes it a movement with the signing date on it. A flag loses the date, and the date is the whole of the legal effect. |
| An opening balance is mistaken for a system-computed figure | Low | It is a distinct movement kind and must render as such. |
| A `decimal` arithmetic slip in the recargo | Low | `specs/rent-adjustment/spec.md:151` already forbids floating point; the same rule applies and is tested. |

---

## Rollback Plan

The migration ships with a `Down` that drops the new tables and reverses the `contracts` guard if it
touched the schema. Nothing outside `Accounts` depends on this capability, so reverting the code is
deleting a namespace and its tests. No existing capability loses behaviour, because none of them
reads the ledger yet — collection is what would.

---

## Dependencies

- `lease-contract` must exist, for the contract and its canon — it does.
- `rent-adjustment` must exist, for the canon's history — it does, and this change must **not**
  rebuild it.
- `access-control` must grant the new tables in the same migration that creates them, per the
  convention `new-table-ships-with-grants` in `openspec/config.yaml`.

---

## Size Forecast

The review budget for this change is **400 authored lines per slice**, half of what `users-and-roles`
used. The slices below are a sanity check that the change divides at all; `sdd-tasks` owns the real
plan.

| Slice | Content | Estimate |
|---|---|---|
| 1 | Domain: account, movement, kinds, balance, the oldest-period rule + tests | 300–380 |
| 2 | Recargo projection + tests | 180–240 |
| 3 | EF configuration **and** its migration, with GRANTs + schema tests | 320–400 |
| 4 | An account that outlives its contract: payments accepted after `Ended`, no further accrual + tests | 140–200 |
| 5 | Opening-balance entry | 200–260 |
| 6 | Rent accrual | 200–280 |

**Total: 1,320–1,740 authored lines**, against roughly four to six pull requests. Smaller than
`users-and-roles`, which ran 2,315–2,855.

Slices 1 and 2 are pure domain and need no database. Slice 3 is the first that needs Docker.

---

## Success Criteria

- The system answers "what does this contract owe today, and from what" without anyone opening a
  folder of receipts.
- An opening balance can be typed for every live contract, and is visibly distinct from anything the
  system computed.
- Recargo is correct against the real receipts already analysed, and no row is written by the mere
  passage of time.
- A contract rescinded with a debt still owed keeps an open account, and payments against it still
  land after the contract reads `Ended`.
- The collection change can be written against this capability without reaching inside it: it posts
  a payment and asks which period that payment settles.
