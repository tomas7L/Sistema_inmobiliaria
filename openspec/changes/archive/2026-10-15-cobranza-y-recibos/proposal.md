# Proposal: cobranza y recibos (slice A: tenant payment and receipt)

Written 2026-10-11, from the exploration saved in Engram as `sdd/cobranza-y-recibos/explore`
(observation #90). Domain vocabulary, glossed once: **cobranza** = collection; **recibo** = receipt;
**recargo** = late-payment surcharge; **mora** = being late; **tasa municipal** = municipal tax passed
through to the tenant; **seguro** = insurance; **Otros Conceptos** = the free, signed "other items"
line; **honorarios contractuales** = the tenant's one-off contract fee, paid in 1 to 6 fixed
instalments.

Business rules come from `openspec/domain/respuestas-del-dueno.md` (cited as §N). This proposal does
not restate them; it cites them.

**How to read this document (Tomás):** read Intent, Scope, Settled Decisions, Open Questions and
Size Forecast. Skim Approach. Skip Affected Areas and Rollback unless you want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Settled Decisions (with how strongly each is held)
6. Open Questions
7. Affected Areas
8. Risks
9. Rollback Plan
10. Dependencies and Preconditions
11. Size Forecast
12. Success Criteria

---

## 1. Intent

`cuenta corriente` answers *what does this contract owe right now, and why*. Nothing yet acts on the
answer: the system cannot record that a tenant paid, and cannot issue the recibo the agency hands
over when they do. The contract-account spec already reserves the seam — *"the payment movement
itself is created by the collection change"* — and nobody has built that side.

This change builds the tenant-facing half of collection: taking a total payment against the period the
tenant names — or, when the tenant names none, the oldest unpaid one — turning the projected recargo into a stored charge at that instant, and issuing its
two numbered recibos — *locatario* and *propietario* — with the lines typed at closing (tasa
municipal, seguro, Otros Conceptos) and, on the propietario recibo, the honorarios de administración.

The liquidation to the owner — paying out what was collected — is blocked on unanswered questions
and is a separate, later change. Only the propietario **document** moves into this change (§6). Rendering the recibo as a PDF is blocked on
the open decision `pdf-library` and is also a separate change.

---

## 2. Scope

### In Scope

- **Payment movement** (`Payment`) on the contract ledger: total only (§6). The tenant may name
  which owed period it pays (CCyCN art. 900); when the tenant names none, it settles the oldest
  unpaid period (§16, art. 901). The settled period is stated on the recibo. See Decision 3.
- **Recargo charged** (`RecargoCharged`) as a stored movement, written for the same period and in the
  **same database transaction** as the `Payment`. This is the instant the cuenta-corriente proposal
  named when it said recargo "becomes a stored movement when it is charged on a receipt".
- **Honorarios contractuales plan, given a home**: an amount and an instalment count (1 to 6) on the
  contract, and the writer that raises each `ContractualFeeInstalment` movement. **In scope because
  payment application needs it** — see Approach §4.3.
- **Recibo with its lines**, modelled as catalog + lines (config `rules.design`):
  - the settled period's rent, the honorarios contractuales instalment due with it (if any), and the
    recargo charged — all derived from the ledger, never typed;
  - **tasa municipal**: typed at closing, never accrues (§4, §16);
  - **seguro**: its own line, separate from Otros Conceptos (§4);
  - **Otros Conceptos**: a **signed** amount typed by the operator (§4). No positive-only guard.
- **One payment per period** (§13), and each payment issues **two** recibos — *locatario* and
  *propietario* — with consecutive numbers (§6). Paying two months is two payments, four recibos.
  Slice A issues both (§6, Decision 15).
- **Gapless recibo numbering**: a counter row incremented in the same transaction as the recibo
  insert, with a unique constraint on the number as a backstop.
- **Cutover starting number**: the counter's first number is set **once**, at go-live, and is locked
  the moment the first recibo is issued. See Decision 9.
- **Voiding (anulación)**: voiding a recibo appends an annulment record and reverses its ledger
  movements with `Correction` movements. A voided number is never reused and never deleted.
- **Propietario recibo**: the same lines as the locatario recibo plus the honorarios de
  administración line, negative, computed from the rate already on `Contract`. All the contract's
  lessors appear together on one propietario recibo. See Decision 15.
- **Non-fiscal labelling**: the recibo carries a fixed legend saying it is not a fiscal invoice.
- **A confirmation preview**: the payment use case first returns a summary (period, previous amount
  owed, each line, total, resulting balance) and only commits on confirmation, per convention
  `confirm-with-a-summary`. This is an application-layer contract, not a screen.
- Schema: new tables with their GRANTs in the same migration (`new-table-ships-with-grants`), the
  `account_movements.kind` CHECK extended, and a partial unique index allowing one `Payment` per
  (account, period).

### UI

**No screen is in scope.** The use cases are callable and tested from `Inmobiliaria.Domain.Tests` and
`Inmobiliaria.Infrastructure.Tests`. A collection screen is a later UI pass. Held: medium-strong —
it keeps the change reviewable and matches how `cuenta corriente` shipped; the cost is that the
agency cannot use it until that pass exists.

### Out of Scope

| Not here | Where it belongs | Why |
|---|---|---|
| The liquidation to the owner, splitting the payout among several locadores, where the administration rate should live | **`liquidacion-al-dueno`** (later change) | Blocked: rate placement (§3), multi-lessor split, payout timing, and the receipt-derived formula are all unconfirmed by the owner |
| PDF rendering and printing of the recibo | **`recibo-pdf`** (later change) | Blocked on open decision `pdf-library` (config.yaml) — flagged here, **not** resolved |
| Notifications to tenants | `notificaciones` | Unrelated to taking a payment; also touches open decision `scheduler-mechanism` |
| Payment plan after rescission (plan de pagos), and paying an account whose recargo is frozen by a novación | `plan-de-pagos` | Its instalments, schedule and default rules are their own concept (cuenta-corriente Decision 10) |
| Electronic invoicing (ARCA) | Separate project | §10 — explicitly not part of this system |
| The optional rounding option the owner asked for | A later, additive change | §11 calls it an optional nicety; truncation stands and is already correct |
| Any collection screen | Later UI pass | See UI above |

---

## 3. Capabilities

### New Capabilities

- **`collection`** — taking a payment: total-only, the period the tenant names or else the oldest unpaid, recargo charged and payment
  written atomically, the confirmation preview, and voiding's ledger reversal.
- **`receipt`** — the recibo document: header, catalog of line kinds, signed lines, one per period,
  gapless numbering, the cutover starting number, annulment, the non-fiscal legend.

### Modified Capabilities

- **`contract-account`** — two new movement kinds (`Payment`, `RecargoCharged`); the requirement
  "A Payment Settles the Oldest Unpaid Period" is **rewritten**: oldest-first becomes the default
  when the tenant names no period, and the "no choice, no override" clause is removed (Decision 3);
  honorarios contractuales instalments become payable through the period rule (today they carry no
  period, so nothing can ever settle them); the opening balance is loaded month by month and so
  becomes payable (§4.4). "The payment movement is created by the collection change" stops being a forward reference.
- **`lease-contract`** — the contract gains the honorarios contractuales plan: a fixed amount and an
  instalment count from 1 to 6 (§2). Not a percentage, never adjusted.
- **`access-control`** — GRANTs for the new tables, including a **deliberate departure** from the
  insert-only pattern: the numbering counter needs `UPDATE`.

The spec is one file, `openspec/changes/cobranza-y-recibos/spec.md`, with a Decisions table, a table
of contents and numbered Tests, per this project's working agreement.

---

## 4. Approach

### 4.1 Payment = recargo charged + payment, one transaction

At payment time the system takes the period the tenant named — or, if none, asks the account for its
oldest unpaid period — and that period's amount owed as of the payment date. A named period must be
owed and already due; anything else is refused. It writes, in one transaction:

1. `RecargoCharged` for that period, equal to the projected recargo (truncated at the edge, as
   `RecargoMath` already does);
2. `Payment` for that period, negative, equal to rent + instalment + recargo for the period;
3. the recibo, its lines, and the incremented counter.

The period then nets to zero and `OldestUnpaidPeriod` and `AmountOwed` stop reporting it with no
change to either method. If (1) were written without (2), `AmountOwed` would project recargo on top of
recargo; atomicity is what prevents it, and a test must prove a failure in (3) leaves no (1) or (2).

The recibo total is **not** the ledger figure: tasa municipal, seguro and Otros Conceptos never touch
the ledger (§16, spec "The Balance Names What It Covers"). The preview must show both figures, named.

### 4.2 Gapless numbering, and the cutover number

A Postgres `SEQUENCE` is rejected: it leaves gaps on rollback. Instead a single counter row is read
with a lock (`SELECT ... FOR UPDATE` or `UPDATE ... RETURNING`) inside the recibo transaction. A
unique constraint on the recibo number catches anything the lock misses.

The starting number is **a cutover parameter, not a constant and not a blocker**. The agency's
current number is loaded at data-loading time, which the project deliberately left for the end. The
design must provide: set the starting number once; refuse to change it after the first recibo is
issued. Who may set it (Admin only, by GRANT) is for design.

Voiding never frees a number. The recibo stays, marked annulled by an appended record; its ledger
movements are reversed with `Correction` movements, and the period becomes unpaid again.

### 4.3 Why the honorarios contractuales plan is in scope

The exploration left this conditional; the code decides it. `AccountMovement.ContractualFeeInstalment`
creates its movement with `period: null` (`src/Inmobiliaria.Domain/Accounts/AccountMovement.cs:93`),
and `OldestUnpaidPeriod` considers only movements that carry a period
(`ContractAccount.cs:161-169`). So an instalment, once written, could **never** be settled by any
payment, and the balance would claim to cover instalments that nothing can pay. Nothing writes them
today, and `Contract` has nowhere to store the plan.

The owner said the instalment *"aparece junto con lo que paga de alquiler"* (§2). The proposed
shape: instalment *k* attaches to the *k*-th rent period from the contract's start, so it is settled
by the same payment, on the same recibo, under the same period rule. Held: **medium** — it fits
§2 and §16, but whether the agency ever lets an instalment be paid off-schedule is Open Question 4.

### 4.4 The opening balance has the same gap

`AccountMovement.OpeningBalance` also carries `period: null` (`AccountMovement.cs:68-69`), so the
same reasoning applies: the debt most tenants start with could never be paid. This was not in the
exploration.

**Settled (Tomás, 2026-10-13): the old debt is loaded month by month**, each month with its real
amount, as ordinary periods — a tenant who owes three months can pay one of them. The old debt then
behaves exactly like new debt, and no special rule is needed. Splitting a total evenly is rejected:
an adjustment, a partial payment or a month's recargo makes the months unequal. A single opening
figure settled whole is rejected for the same reason: it could not be paid one month at a time.

Still unknown, and now a **data-loading** question rather than a spec blocker: the owner described
the spreadsheet as *"un excel con la deuda de cada inquilino"* (§ cuenta corriente, 2026-10-06) — one
figure per tenant. Whether it records which months are owed decides how the months are reconstructed
at cutover, not how the system models them. Tomás expects three months owed to be rare in practice:
the agency would likely rescind first.

### 4.5 What design must decide

1. Where the counter lives (its own table vs. a settings row) and how the Supavisor pooler in
   transaction mode affects `FOR UPDATE` — verify against the real pooler, do not assume.
2. Whether the instalment gains a period at creation (factory change) or is mapped to one.
3. The receipt-line catalog: fixed kinds in code, or a table. Config requires catalog + lines.
4. The concurrency guard for two operators paying the same period: the partial unique index is the
   proposed backstop.
5. The payment date rule (§6, "Settled — the payment date"): how far back a received date may go and
   who may type it.

---

## 5. Settled Decisions

Strength: **strong** = owner-confirmed or verified fact; **medium** = well-reasoned team call;
**weak** = no primary source, act on it only provisionally.

| # | Decision | Source | Strength |
|---|---|---|---|
| 1 | This change is **slice A only**: tenant payment and recibo. Owner settlement and PDF are separate changes. | Exploration slicing; orchestrator | Medium-strong |
| 2 | **Total payments only**; a partial amount is refused. | §6, contract-account spec | Strong |
| 3 | **The tenant may name which owed, due period it pays**; when it names none, the **oldest unpaid** period is settled. The recibo states the period. This **corrects** §16 and the contract-account requirement, which forbade any choice. | CCyCN arts. 900–902 (verified 2026-10-13): the debtor may impute at payment (900); if it does not, the creditor may, on receipt (901) — the owner's practice, *"el primero que adeuda"* (§16); the statutory fallback is the most onerous due debt, then pro rata (902) | **Strong** on the right to choose. **Weak** on one edge: art. 900 forbids imputing to capital while interest is owed without the creditor's consent — whether that forces older periods' recargo to be paid first is for the agency's lawyer or contador |
| 4 | **One payment per period**; each payment issues a *locatario* and a *propietario* recibo, consecutive numbers from one sequence. | §13; samples 24269/24270 compared 2026-10-13 | Strong on two numbers; **medium** on "issued together" (one sample pair) |
| 5 | `RecargoCharged` and `Payment` are written in **the same transaction** as the recibo. | Exploration risk analysis; follows from `AmountOwed` netting | Strong (technical) |
| 6 | Tasa municipal, seguro and Otros Conceptos are **typed at closing and never accrue**; seguro has its own line; Otros Conceptos **and seguro** are **signed**. | §4, §16; seguro's sign: Tomás, 2026-10-13 | Strong |
| 7 | **Gapless counter + unique constraint**, not a `SEQUENCE`. | Exploration (Postgres sequence semantics) | Medium-strong on technique; **weak** on legal need — continuity is the owner's wish (§7), not a found legal rule |
| 8 | **Voiding appends an annulment**; a number is never reused or deleted; ledger reversal by `Correction`. | Append-only house pattern; Tomás, 2026-10-13: the voided number stays in the history, marked annulled, so the sequence can be audited | Strong |
| 9 | **The agency's current recibo number is a cutover parameter, not a blocker.** Set once at data loading; not hardcoded; not changeable after the first recibo is issued. This **corrects** the exploration and §-Open item 1 of `respuestas-del-dueno.md`, which called it a blocker for `cobranza`. | Orchestrator decision 2026-10-11 | Strong |
| 10 | The recibo is **non-fiscal** and is labelled so it cannot be mistaken for a factura. Fiscal invoicing stays out (§10). | §8, §10; exploration found **no primary source** on whether an agency collecting for an owner must itself invoice or number under RG 1415 | **WEAK** — to be confirmed with the agency's **contador**, not with Nicolás. Do not present it as legal fact. |
| 11 | The honorarios contractuales plan gets its home here (amount + count 1..6 on the contract, plus the instalment writer). | Code evidence, Approach §4.3 | Medium-strong |
| 12 | **No UI** in this change. | Scope discipline | Medium-strong |
| 13 | Amounts are whole pesos, truncated at the edge, `decimal` only, `numeric(14,2)` storage. | Convention `whole-peso-amounts` | Strong |
| 14 | `Contract.End()` gains **no** solvency check. §2 consequence 4 is superseded by §17. | §17, contract-account spec | Strong |
| 15 | **Slice A issues the propietario recibo too**, with the honorarios de administración line from the rate on `Contract`. Paying the owner stays in `liquidacion-al-dueno`. | Two consecutive numbers per collection (§6); a reserved number with no document would break the gapless rule; Tomás, 2026-10-13 | Strong (decision); **medium** that both are issued at collection time (one sample pair) |

---

## 6. Open Questions

### For Nicolás — these affect slice A only

| # | Question (one sentence) | Blocks spec? | Default if not blocking |
|---|---|---|---|
| 4 | Can a honorarios contractuales instalment ever be paid ahead of, or apart from, the monthly rent? | No | No: instalment *k* rides with the *k*-th rent period and is settled with it |
| 7 | Does your Excel say which months each tenant owes, or only the total? | No — data loading only (§4.4) | Reconstruct the months with Nicolás at cutover |

### Settled — the propietario recibo (Tomás, 2026-10-13)

One collection issues **two** numbered documents (Already answered, below): a *locatario* recibo and
a *propietario* recibo. The propietario recibo carries the honorarios de administración line, which
this proposal had placed in `liquidacion-al-dueno` (§2, Out of Scope). Consecutive numbers on the
sample pair suggest both are issued at the moment of collection, from one shared sequence — held
**medium**: one pair is one observation.

So slice A must either issue the propietario recibo itself (using the rate already stored on
`Contract`, `src/Inmobiliaria.Domain/Leasing/Contract.cs:24`), or reserve its number for a later
change. Reserving leaves a number with no document behind it, which the gapless rule exists to
prevent. **Decided: slice A issues both** (Decision 15). The liquidation — paying the owner — stays in
`liquidacion-al-dueno`; only the document moves. The rate's placement (owner vs. contract,
`respuestas-del-dueno.md` §3) stays open: slice A reads the rate where it lives today.

### Settled — the payment date (Tomás, 2026-10-13)

Tomás, 2026-10-13: a payment is recorded on the day the money is received; if it is received
yesterday and recorded today, today's date is what the system would use. The problem he raises is
real: recargo grows by the day, so a late entry makes the tenant owe more than they did when they
paid, and the amount handed over turns out insufficient.

Accepted, and it matches established practice (held **strong** on the shape): core banking systems
separate the **value date**, which drives interest, penalty and arrears, from the **booking/creation
date**, when the entry is recorded; back-dating is permission-gated and cannot reach before an
accounting closure (Mambu, "Booking Date vs Value Date",
https://docs.mambu.com/docs/booking-date-vs-value-date/). The payment carries two dates. **Received on** defaults to today and may
be typed earlier, never later than today and no more than a bounded number of days back; recargo is
computed as of that date. **Recorded at** is the system timestamp, never editable. Both are stored
and the recibo shows the received date. The bound and who may back-date are for design. The banking analogue of the bound is the closure:
once `liquidacion-al-dueno` exists, a month already liquidated to the owner is the natural floor;
until then, design sets a small fixed number of days. A typed date
lowers the recargo, so it must be visible and bounded, not free. Side effect: recibo numbers follow
recording order, so a back-dated recibo can carry a higher number than one dated after it.

Questions about the owner's part (honorarios de administración rate placement, multi-lessor split,
seguro on the owner's part, payout timing) belong to `liquidacion-al-dueno` and are not asked here.

### Not for Nicolás

- **Fiscal status of the recibo** — for the agency's contador (Decision 10).
- **Pooler semantics** for the locked counter — technical, design verifies it.

### Already answered — do not ask again

- **Paying one month at a time** (former Question 1): yes. Old debt is loaded month by month (§4.4).
- **Voiding** (former Question 3): the recibo is annulled, its number stays in the history marked
  annulled, its movements are reversed, and a new recibo takes the next number (Decision 8).
- **Seguro sign** (former Question 6): signed, like Otros Conceptos (Decision 6).
- **Which period a payment settles**: the tenant's choice, oldest by default (Decision 3).
- The current recibo number: it is a cutover input (Decision 9), asked at data loading.
- **One number or two per collection: TWO** (Tomás, 2026-10-13, from the samples side by side).
  N° 24269 and N° 24270 have the same locatario (ABREGU XUAN), the same propietarios (VICO LESLIE Y
  VICO ALEJANDRA) and the same period. One is headed *locatario*, the other *propietario*; they
  differ only in the honorarios de administración line — about −47,000 on the propietario recibo,
  0 on the locatario one. This confirms the 2025 reading (one collection, two documents, two
  numbers from one correlative sequence). The owner's *"uno se lo lleva, otro se lo quedan ellos"*
  describes the two parts of **each** sheet, which share that sheet's number. Several lessors appear
  together on one propietario recibo, not one recibo each.

---

## 7. Affected Areas

| Area | Action | What |
|---|---|---|
| `src/Inmobiliaria.Domain/Accounts/` | Modify | `MovementKind` (+`Payment`, `RecargoCharged`), `AccountMovement` factories, period handling for instalments/opening balance |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modify | Honorarios contractuales amount and instalment count |
| `src/Inmobiliaria.Domain/Collection/` (name for design) | Create | Payment application, preview, recibo, lines, numbering rules, annulment |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/` | Create/Modify | Recibo, lines, counter; account-movement kind CHECK; contract fields |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/` | Create | Shipped **with** its model changes, GRANTs included |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modify | New DbSets |
| `tests/Inmobiliaria.Domain.Tests/` | Create | Payment application, recargo charge, lines, sign rules, numbering rules |
| `tests/Inmobiliaria.Infrastructure.Tests/` | Create | Atomicity, gapless numbering under concurrency, unique backstops, GRANTs, cutover lock |

**Not touched:** `Inmobiliaria.Desktop`, `RecargoMath`'s formula, `Contract.End()`, the rent-adjustment
capability.

---

## 8. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Recargo charged on top of recargo, if `RecargoCharged` lands without its `Payment` | Medium | One transaction (Decision 5); an infrastructure test forces a failure after step 1 and asserts nothing persisted |
| A number skipped or duplicated under concurrent issuance | Medium | Locked counter row + unique constraint; a concurrency test with two sessions |
| `FOR UPDATE` behaves unexpectedly through the Supavisor pooler in transaction mode | Low-Med | Design verifies; the unique constraint still refuses a duplicate |
| Recibo total read as the account balance | Medium | Preview names both figures (spec "The Balance Names What It Covers") |
| A positive-only guard copied onto Otros Conceptos or seguro | Medium | §4 warning; a test pays with a negative Otros Conceptos and another with a negative seguro |
| Opening balance or instalments unpayable (period is null) | **High if ignored** | Approach §4.3–4.4 |
| The oldest-first rule survives somewhere and refuses a named period | Medium | The contract-account requirement and its Test 14 are rewritten in this change's spec; a test pays September while July is owed |
| A back-dated received date used to shave recargo | Medium | Bounded, never future, both dates stored and shown (§6, payment date) |
| Adding a `MovementKind` member changes the EF model, splitting model and migration across PRs | Medium | Every slice that changes the model ships its migration; run `dotnet ef migrations has-pending-model-changes` per slice |
| The numbering modelled as one number per collection | Retired | Settled: two recibos, two numbers (Open Questions, "Already answered") |
| The non-fiscal stance is wrong | Low-Med | Decision 10 is marked WEAK and routed to the contador before go-live |
| `RentAdjustmentConfiguration` key-generation defect (23503 on confirm) | Low | Fixed by `15ae544`: a model-wide `ValueGenerated.Never` on Guid keys in `InmobiliariaDbContext`, covered by `ApplicationSuppliedKeyTests` (code read 2026-10-13; the suite was not re-run) |

---

## 9. Rollback Plan

Each migration ships with a `Down` that drops the recibo, line and counter tables, removes the partial
unique index, restores the previous `kind` CHECK and drops the new contract columns. Before any real
recibo exists, rollback is lossless. **After the first real recibo is issued, rollback is not a
schema operation**: issued numbers and payments are agency records, and reverting would erase them —
at that point a defect is fixed forward, and a wrong recibo is voided, never deleted. Reverting the
code is removing the collection namespace and the two new movement kinds; `cuenta corriente` keeps
working as it does today because nothing else reads `Payment` or `RecargoCharged`.

---

## 10. Dependencies and Preconditions

- `contract-account` exists and owns the period rule — it does; this change rewrites that rule
  (Decision 3) but must not rebuild the account.
- `lease-contract` exists — it does; it is modified (Decision 11).
- `access-control` convention `new-table-ships-with-grants`.
- Open decision **`pdf-library`** is touched only by the deferred `recibo-pdf`; this change does not
  resolve it. **`scheduler-mechanism`** is not touched.

**Preconditions for implementation (announce before starting):**

- **Docker running** for every slice touching `Inmobiliaria.Infrastructure.Tests`. A skipped
  Testcontainers test is an unrun test.
- **Supabase restored** (it pauses on the free plan) before the migration is applied to the live
  database. Only Tomás connects to it.
- The one-or-two-numbers question settled before `sdd-spec` writes the numbering requirement; the rest of the spec can proceed.

---

## 11. Size Forecast

Advisory only. Planning heuristic: about **400 authored changed lines per slice** (the project budget
in config is 800; `cuenta corriente` planned at 400). `sdd-tasks` owns the real plan.

| Slice | Content | Estimate |
|---|---|---|
| 1 | Domain: `Payment` + `RecargoCharged`, period application (named or oldest), preview, voiding reversal + tests (plus kind CHECK migration if the EF model sees the enum) | 280–360 |
| 2 | Honorarios contractuales plan on `Contract` + instalment writer + period attachment, with its migration + tests | 200–300 |
| 3 | Recibo + lines (catalog, signs), locatario and propietario recibos with the honorarios de administración line, paired consecutive numbering and cutover rules in the domain + tests | 400–550 |
| 4 | EF config + migration for recibo, lines, counter, partial unique, GRANTs; the transactional use case; atomicity and concurrency tests | 330–430 |

**Total: about 1,210–1,640 authored lines**, four to five PRs to `develop` — above the
exploration's 1,000–1,400 because the propietario recibo moved in (Decision 15); slice 3 will likely
split in two. Slice 1 and 3 are mostly domain; slices 2 and 4 need Docker.

Decision needed before apply: Yes (chain strategy, under `ask-on-risk`)
Chained PRs recommended: Yes
400-line budget risk: Medium

---

## 12. Success Criteria

- A total payment settles exactly the period the tenant named, or the oldest unpaid one when none is
  named; a period not owed or not yet due is refused. It writes `RecargoCharged`
  and `Payment` atomically, and leaves `AmountOwed` correct the next day with no double recargo.
- A partial amount is refused and writes nothing.
- Each payment issues its recibos (locatario and propietario) with the next
  consecutive gapless numbers; two concurrent
  issuances never share or skip a number.
- The starting number can be set once at cutover and is refused after the first recibo.
- A voided recibo keeps its number, is marked annulled, and its period becomes unpaid again.
- Tasa municipal, seguro and a negative Otros Conceptos appear on the recibo and never on the ledger.
- A honorarios contractuales instalment and the opening balance can actually be paid.
- The preview shows previous amount owed, each line, the total and the resulting balance before
  anything is written.
- Every new table is readable and writable by both application roles as granted, and by nothing more.
