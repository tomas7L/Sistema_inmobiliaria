# Exploration: cuenta corriente

The running account that answers one question at any moment: **what does this tenant owe, and
why**. Written 2026-09-26, after `users-and-roles` closed.

Domain vocabulary, glossed once: **cuenta corriente** = running account / ledger; **locatario** =
tenant; **recargo** = late-payment surcharge; **mora** = the state of being late.

This is exploration. Nothing here is decided. Every claim about existing code or specification was
checked against the file named beside it, per `openspec/config.yaml`'s rule that an invariant is
verified against its constructor and never taken from a comment.

---

## 1. What the existing system already decides for us

Four requirements already in the living specification constrain this change more than anything we
would choose ourselves. Finding them first is what stops this change from re-litigating settled
questions.

| Existing requirement | Where | What it forces on us |
|---|---|---|
| **A Late Adjustment Is Not Charged Retroactively** | `specs/rent-adjustment/spec.md:186` | A period's charge, once raised, **never moves** because an adjustment landed late. This removes the strongest argument for a derived balance — see §4. |
| **Split Persisted, Not Re-Derived Monthly** | `specs/lease-contract/spec.md:249` | The project already prefers persisting a computed figure over recomputing it each cycle. A stored ledger is the house pattern, not a deviation from it. |
| **Append-Only Adjustment History** and **A Correction Produces a New Adjustment, Never a Rewrite** | `specs/rent-adjustment/spec.md:203` and `:234` | History is append-only and corrections are new rows. A cuenta corriente that allowed editing a past movement would contradict the change that precedes it. |
| **Honorarios Percentage Is Stored, Nullable** | `specs/lease-contract/spec.md:267` | The administration rate lives on the contract. Where it *should* live is still open with the owner — see §7. |

---

## 2. What a cuenta corriente is here: per contract, not per tenant

**Proposal: the account belongs to the CONTRACT. The tenant is how you look it up.**

The argument:

- Rent is owed **under a contract**, at a canon that contract defines, on a due date that contract
  sets.
- The recargo clock starts from **that contract's** due date. Two contracts late by different
  amounts cannot share one clock.
- The honorarios contractuales instalment plan is agreed **when a contract is signed** and dies
  when it is paid off.
- The owner's own rule — *a contract cannot close while the tenant still owes* — is only
  expressible if "what is owed" is scoped to a contract. Scoped to a tenant, it cannot be evaluated.

The counter-argument, and why it does not win: the agency thinks in people. Nicolás asks *"¿qué me
debe Abregu?"*, not *"¿qué se debe bajo el contrato 47?"*. That is real, but it is a **question about
presentation**. A tenant view that sums the accounts of that tenant's contracts answers it without
making the account itself tenant-scoped. The reverse — one tenant-scoped account carrying two
contracts' due dates and two recargo clocks — has no clean shape.

**Open for the team, not the owner:** does a tenant with two contracts see one combined screen or
two? That is a UI decision this change should name but need not settle.

---

## 3. What moves the balance

| Movement | Direction | Exists today? | Belongs to |
|---|---|---|---|
| Opening balance at go-live | debit | **No** | this change |
| Rent accrual for a period | debit | **No** | this change |
| Honorarios contractuales instalment | debit | **No** — nothing models it | this change |
| Tasa municipal, seguros, otros conceptos | **signed** | **No** | **collection — OUT OF SCOPE here**, confirmed by the owner 2026-09-26; see §7 |
| Recargo | debit | **No** | this change, but not as a stored movement — see §6 |
| Payment | credit | **No** | collection |

Nothing in `src/Inmobiliaria.Domain/` models any of these today. `Leasing` holds the contract, its
parties, its units, its documents and its adjustments — and stops there. This change starts from
nothing, which is unusually clean.

---

## 4. Stored ledger or derived balance

The usual reason to derive a balance rather than store it is that a past figure might change, and a
stored balance would then be stale.

**In this system a past figure cannot change.** `A Late Adjustment Is Not Charged Retroactively`
says so explicitly: an adjustment confirmed three months late generates no charge for those three
months. The canon that was in force when a period accrued is the canon that period is owed at,
permanently.

That removes the argument. And two further facts push the same way:

- **Only total payments.** There is no part-paid month to reconcile, no allocation of a partial
  amount across several debts. A payment settles a period or it does not exist.
- **The project's own append-only precedent.** `rent-adjustment` already forbids rewriting history
  and requires a correction to be a new row. A ledger that behaves differently would be the odd one
  out.

**Proposal: an append-only ledger of movements, with the balance as their sum.** A correction is a
new, signed movement that references what it corrects — exactly the shape `RentAdjustment` already
uses. Whether the running total is also cached as a column is a performance question for the design
phase, not a modelling one; with two people and a few dozen contracts, almost certainly not.

---

## 5. The boundary with the collection change

This is the line that decides whether this change can be finished. Drawn wrong, the two changes
bleed into each other and neither closes.

**Proposal:**

> **Cuenta corriente owns what is owed and why.** It is the ledger: it accrues debts, records
> opening balances, and answers "what is the balance on this date".
>
> **Collection owns the act of being paid and the document that proves it.** It computes the exact
> amount to charge today, takes the money, issues the receipt pair, and posts one credit movement
> back into the account.

The seam is the **payment movement**: collection creates it, cuenta corriente stores it and never
questions it.

Two consequences worth stating plainly:

- **The receipt is not this change's problem.** Its two-part shape is still an open question with
  the owner and it blocks collection, not this. Good: this change can proceed while that is
  unresolved.
- **"Otros Conceptos", tasa municipal and seguros belong to collection.** Confirmed by the owner on
  2026-09-26: they are typed on the receipt when it is closed, and do not accrue as debts. See §7.1
  for what that means for the balance this capability reports.

---

## 6. Recargo is the hard one, and it is not a movement

2% per day, simple, from the day of mora, **with no cap**. It grows every single day.

It therefore **cannot be a stored daily movement** — a contract three months late would generate
ninety rows saying nothing. It must be **derived at the moment it is asked for**, from the amount
owed, the due date, and the date of the question.

It becomes a stored movement at exactly one instant: **when it is charged on a receipt.** Before
that it is a projection; after that it is history.

That is a genuine modelling seam and it belongs in the design phase. Flagged here because it is the
one place where "append-only ledger" is not the whole answer, and a design that misses it will
either store ninety useless rows or lose the recargo actually charged.

`RECARGO = alquiler × 2% × (día_pago − 10)` was verified against the agency's real receipts during
the first change. The 10 is the due day and belongs to the contract, not to a constant.

---

## 7. Open questions

**ANSWERED by the project owner on 2026-09-26. Both were asked before the proposal, and both
shrink this change rather than grow it.**

1. **"Otros Conceptos", tasa municipal and seguros are typed on the receipt at the moment of
   closing it. They do NOT accrue as debts.** They therefore belong entirely to the collection
   change and are **out of scope here**.

   This is not a formality — the two options behave differently. Accrued, the system would have to
   answer *"the tenant owes $28,900 of municipal tax"* before any receipt exists, which would make
   them movements with their own dates and force a decision about whether they earn recargo. Typed
   at closing, they do not exist until a receipt exists.

   It is also the only shape that can work: the system has no source for the municipal tax. The
   owner reads it elsewhere and writes it down. There is nothing to accrue from.

   **Consequence that must be visible in the UI, not buried:** the balance this capability reports
   is **rent + honorarios contractuales instalments + recargo, and nothing else.** Municipal tax,
   insurance and "Otros Conceptos" appear for the first time on the receipt. Anyone expecting the
   balance to be the whole of what will be charged will be surprised, so the screen must say what
   the figure covers.

2. **Oldest debt first.** A tenant three months behind who pays one month is paying **the oldest
   unpaid period**. Not chosen by the operator.

   This is stronger than it looks: it removes a decision from the screen entirely. The employee
   picks nothing, so a whole class of misapplied-payment error cannot happen.

   **Two consequences for the design phase.** The recargo owed on that payment is computed over the
   oldest period's days late, which is the largest of the three — the tenant pays the most expensive
   month first, which is correct and worth stating so nobody "fixes" it. And the remaining two
   periods keep accruing recargo from their own due dates, untouched by the payment.

**Already open and tracked in `openspec/domain/respuestas-del-dueno.md`, not new here:**

3. Where the administration honorarios rate belongs — contract or owner relationship.
4. The exact current receipt number — **blocks collection, not this change**.
5. Whether the two-part receipt is one sheet per party or one in total — **blocks collection, not
   this change**.

**For the team:**

6. One screen per tenant or per contract, when a tenant holds several.

---

## 8. Shape of the work

The review budget for this change is **400 changed lines per slice**, half of what the previous
change used. That argues for narrow, independently shippable seams. A first sketch:

| Slice | Content |
|---|---|
| 1 | The domain: account, movement, movement kinds, the sum. Pure domain, no database |
| 2 | EF configuration **and** its migration — these are atomic, proven by `users-and-roles` |
| 3 | Opening-balance entry, typed by hand, one per contract |
| 4 | Rent accrual per period |
| 5 | Recargo projection |
| 6 | The screen |

Not a plan — a sanity check that the change divides at all. The tasks phase owns the real one.

---

## 9. What I could not determine

- **Whether the agency currently tracks anything resembling a running account**, or whether they
  look at a folder of receipts and work it out. Nobody has been asked. It matters because it tells
  us whether the opening balances they will type are a number they already maintain or one they will
  have to reconstruct per tenant — which is a very different amount of work for them, and worth
  knowing before we promise a go-live date.
- **Whether a tenant ever pays a contract that has already ended.** The owner said a contract does
  not close until everything is paid, which implies no — but that is an inference from one sentence,
  and inferences from single sentences are exactly what this project has had to correct twice.

---

## Key Learnings

1. `specs/rent-adjustment/spec.md:186` forbids retroactive charges for a late adjustment, which
   means a period's charge never changes after it accrues and removes the main argument for deriving
   the balance rather than storing it.
2. The project already persists rather than recomputes (`lease-contract:249`) and already forbids
   rewriting history (`rent-adjustment:203`), so an append-only ledger is the house pattern rather
   than a new one.
3. Nothing in `src/Inmobiliaria.Domain/` models any balance, movement, payment or charge today, so
   this change starts from an empty field.
4. Recargo cannot be a stored daily movement because it is uncapped and grows every day; it is a
   projection until the moment it is charged on a receipt, and only then history.
5. The owner's rule that a contract cannot close while the tenant still owes is only expressible if
   the account is scoped to a contract, which is the strongest argument against a tenant-scoped
   account.
