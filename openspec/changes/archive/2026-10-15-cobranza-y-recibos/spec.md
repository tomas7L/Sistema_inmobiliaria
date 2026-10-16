# Spec: cobranza y recibos (slice A: tenant payment and recibo)

Single-file specification for the `cobranza-y-recibos` change. Five capabilities are covered:
**`collection` (NEW)**, **`receipt` (NEW)**, and three MODIFIED: **`contract-account`**,
**`lease-contract`** and **`access-control`**. Each modified capability states exactly which living
requirement is added, rewritten or removed.

Domain vocabulary (Argentine terms, glossed once): **cobranza** = collection; **recibo** = receipt;
**locatario** = tenant; **locador** = lessor; **propietario** = owner of the property (the
*propietario recibo* is the owner's copy of a collection); **recargo** = late-payment surcharge;
**mora** = being late; **tasa municipal** = municipal tax passed through to the tenant; **seguro** =
insurance; **Otros Conceptos** = the free, signed "other items" line; **honorarios de
administración** = the owner's monthly management fee, a percentage of the rent;
**honorarios contractuales** = the tenant's one-off contract fee, paid in 1 to 6 fixed instalments;
**anulación** = annulment (voiding).

The proposal is `openspec/changes/cobranza-y-recibos/proposal.md`. Owner answers are cited as `§N` of
`openspec/domain/respuestas-del-dueno.md`. Anything this spec had to decide that the proposal did not
is marked **SPEC CALL** in the Decisions table, with its strength.

**What this spec does not do.** It states what must be true, observably. Table layout, class names,
SQL, counter placement and pooler behaviour are design's. Where a parameter is design's, the text
says so.

## Decisions

**CONFIRMED BY USER** = the agency owner or the project owner said so. **TEAM DECISION** = Volkode
decided. **VERIFIED FACT** = proven against a source or the code. **SPEC CALL** = this spec had to
decide because the proposal did not; the strength says how hard to lean on it. Strength: **strong**,
**medium**, **weak** (act on it only provisionally).

Rows 1 to 15 are the proposal's Settled Decisions, in the proposal's numbering. Rows 16 onwards are
this spec's.

| # | Decision | Status |
|---|---|---|
| 1 | This change is **slice A only**: tenant payment and recibo. Owner settlement (`liquidacion-al-dueno`) and PDF (`recibo-pdf`) are separate changes. | TEAM DECISION, medium-strong |
| 2 | **Total payments only.** A partial amount is refused and writes nothing. | CONFIRMED BY USER (§6) |
| 3 | **The tenant may name which payable period a payment settles**: one that no live payment settles and that is owed, or whose month has begun and whose due date has not arrived (Decision 37). When it names none, the **oldest unpaid** period is settled. The recibo states the period. This corrects §16 and the living requirement "A Payment Settles the Oldest Unpaid Period", which forbade any choice. | VERIFIED FACT: CCyCN arts. 900–902. **Strong** on the right to choose. **WEAK** on one edge: art. 900 forbids imputing to capital while interest is owed without the creditor's consent, so whether older periods' recargo must be paid first is for the agency's lawyer or contador. This spec does not enforce that edge. |
| 4 | **One payment per period**; each payment issues a *locatario* and a *propietario* recibo, with consecutive numbers from one sequence. | CONFIRMED BY USER (§13); strong on two numbers, **medium** on "issued together" (one sample pair) |
| 5 | `RecargoCharged` and `Payment` are written in **the same transaction** as the recibos and the counter. | TEAM DECISION, strong (technical) |
| 6 | Tasa municipal, seguro and Otros Conceptos are **typed at closing and never accrue**; seguro has its own line; Otros Conceptos **and seguro** are **signed**. | CONFIRMED BY USER (§4, §16; seguro's sign confirmed by Tomás, 2026-10-13) |
| 7 | **Gapless counter + unique constraint**, not a database `SEQUENCE`. | TEAM DECISION. Medium-strong on technique; **WEAK** on legal need: continuity is the owner's wish (§7), not a found legal rule |
| 8 | **Voiding appends an annulment**; a number is never reused or deleted; the ledger is reversed by `Correction` movements. | CONFIRMED BY USER (Tomás, 2026-10-13), strong |
| 9 | **The agency's current recibo number is a cutover parameter, not a blocker.** Set at data loading; not hardcoded; not changeable after the first recibo is issued. Corrects the Open item 1 of `respuestas-del-dueno.md`. | TEAM DECISION, strong |
| 10 | The recibo is **non-fiscal** and is labelled so. Fiscal invoicing stays out (§10). | **WEAK.** No primary source found on whether an agency collecting for an owner must itself invoice or number under RG 1415. To be confirmed with the agency's **contador**, not with Nicolás. Not legal fact. |
| 11 | The honorarios contractuales plan gets its home here (amount + count 1 to 6 on the contract, plus the instalment writer). | TEAM DECISION, medium-strong (code evidence: instalments carry no period today) |
| 12 | **No UI** in this change. | TEAM DECISION, medium-strong |
| 13 | Amounts are whole pesos, truncated at the edge, `decimal` only, `numeric(14,2)` storage. | CONFIRMED BY USER (convention `whole-peso-amounts`) |
| 14 | `Contract.End()` gains **no** solvency check. §2 consequence 4 is superseded by §17. | CONFIRMED BY USER (§17) |
| 15 | **Slice A issues the propietario recibo too**, with the honorarios de administración line from the rate on `Contract`. Paying the owner stays in `liquidacion-al-dueno`. | TEAM DECISION (Tomás, 2026-10-13), strong; **medium** that both are issued at collection time |
| 16 | **Payment dates.** *Received on* defaults to today, may be typed earlier, never later than today, within a bounded number of days back; recargo is computed as of it. *Recorded at* is the system timestamp and is never editable. The recibo shows the received date. The bound and who may back-date are set by Decision 38. | TEAM DECISION (Tomás, 2026-10-13). Strong on the shape (value date vs booking date); the bound is Decision 38. |
| 17 | **Open Question 4 default, labelled as a default:** a honorarios contractuales instalment is never paid apart from the monthly rent; instalment *k* rides with the *k*-th rent period. | **DEFAULT, not answered by Nicolás**; medium |
| 18 | **Open Question 7 default, labelled as a default:** old debt is loaded month by month; which months each tenant owes is reconstructed with Nicolás at cutover. The model does not depend on the answer. | **DEFAULT**; data-loading question only |
| 19 | **Voiding a recibo annuls the whole pair.** A payment produces two recibos from one event; reversing the payment while leaving one of the pair standing would leave a recibo describing money that no longer counts. Annulling either recibo therefore annuls both and reverses the payment. | SPEC CALL, **medium-strong** (follows from Decision 8 and the success criterion "its period becomes unpaid again"; the proposal never says it) |
| 20 | **The propietario recibo does not carry the honorarios contractuales instalment.** The proposal says the propietario recibo has "the same lines" plus the administration fee; the owner said the instalment prints only on the tenant's part and "the owner's part never carries it" (§2). The owner's words win. | SPEC CALL, **medium**; **contradicts the proposal's wording**, one-line change if Tomás decides otherwise |
| 21 | **Honorarios de administración base.** The line is the contract's rate applied to the **rent of the settled period plus the recargo charged on it**, truncated. Tasa municipal, seguro, Otros Conceptos and the instalment are not in the base. Evidence: propietario recibo N° 24270 reads -47,860 = 8% × (507,000 rent + 91,260 recargo) = 47,860.80 truncated; 8% of the rent alone (40,560) does not reproduce it, nor does 8% with tasa municipal and Otros Conceptos added. | CONFIRMED BY USER (Tomás, 2026-10-14: "si ese alquiler tiene recargo, se aplica a lo que sale el alquiler con ese recargo"), **strong**; matches recibo N° 24270 |
| 22 | **A rate never entered and a rate of 0% are different things.** A contract whose rate was never entered cannot have a collection committed: the payment is refused, writes nothing, and the preview says why. A contract with a rate of **0%** MAY be collected: the honorarios de administración line is zero, and the preview MUST warn that the contract charges no administration fee, so a forgotten rate is caught before the recibos are issued. | TEAM DECISION (Tomás, 2026-10-14, the 0% warning), **medium**; whether 0% contracts exist in practice is Nicolás's to say and does not block |
| 23 | **The locatario recibo takes the lower number of the pair, the propietario recibo the next one.** Numbers follow issuance order, and the system issues the locatario recibo first. | **medium**: the sample pair says so (N° 24269 is headed *Locatario*, N° 24270 *Propietario*, both dated 19/08/2026); one pair only. No rule on the order was found in law or practice |
| 24 | **A zero recargo writes no `RecargoCharged` movement and prints no recargo line.** | SPEC CALL, weak; low consequence |
| 25 | **Tasa municipal MUST NOT be negative**; seguro and Otros Conceptos MAY be. The proposal names only those two as signed. | SPEC CALL, weak; low consequence |
| 26 | **The cutover number is required before the first issuance** (no silent default of 1), and MAY be set and corrected freely until the first recibo exists, then is locked. "Set once" is read as "locked after first use", so a typo at cutover is recoverable. Who may set it is Decision 39. | SPEC CALL, medium |
| 27 | **The amount a payment is for must equal the period's ledger total exactly** (rent + instalment + recargo as of the received date). A smaller amount is the partial payment of Decision 2; a larger one is refused too, because the system does not represent credit on a period. The agency settles cash differences at the register (§11). | SPEC CALL, medium |
| 28 | **A payment against an account whose recargo is frozen by a novación is refused.** The plan de pagos is its own change. | SPEC CALL, medium-strong (the proposal puts it out of scope; refusing is the safe reading) |
| 29 | **A commit applies exactly what the preview showed.** If any previewed figure differs at commit (the day turned over, another operator paid, a rate changed), the commit is refused, writes nothing, and a fresh preview is required. | SPEC CALL, medium |
| 30 | **An opening balance with an amount must carry its period; only a zero one may be period-less.** The living rule "exactly one `OpeningBalance` per account" cannot survive Decision "loaded month by month": it becomes one entry per owed month, or one period-less zero for an up-to-date tenant. | SPEC CALL, medium (the proposal says "as ordinary periods" without saying how the living rule bends) |
| 31 | **An opening-balance month projects no recargo.** The living code already treats a movement with no accrual as having no date to measure from, and the typed figure is what the agency's spreadsheet says is owed, mora included. Data loading must enter each month's real figure with that in mind. | VERIFIED FACT (`ContractAccount.cs:107-109`, `:134-141`) |
| 32 | **Instalments are raised only for periods due after the contract's cutover date** on a contract that predates the system; a period due on or before it, including exactly on it, belongs to the opening balance. The plan cannot be changed once any instalment has been written. | SPEC CALL, medium (the proposal does not cover a pre-existing contract mid-plan). **Amended 2026-10-14:** was "on or after go-live"; Decision 41 governs the boundary, so instalments follow rent |
| 33 | **Voiding records a reason, the acting user and the time.** Precedent: "Ended Requires a Reason". Who may void, and until when, is Decision 45. | SPEC CALL, medium |
| 34 | **"Today" is the agency's local calendar date**, not the database server's UTC date. | SPEC CALL, medium |
| 35 | **Fractional pesos typed on a line are truncated toward zero** at the edge, and the preview shows the truncated figure. | SPEC CALL, medium (convention `whole-peso-amounts`) |
| 36 | **The database cannot tell an increment of the counter inside an issuance from any other `UPDATE` of it**, so the counter's `UPDATE` grant is a named limit, not a protection. The unique constraint on the recibo number is what makes tampering visible. | VERIFIED FACT (privilege model), named as the Column-Level Limits requirement demands |
| 37 | **A period may be paid as soon as its month has begun**, before its due date. Its rent is brought onto the account by the payment, with no recargo (it is not late). A month that has not begun cannot be paid. **Amended 2026-10-14:** *payable* = no live payment settles the period **and** it is owed, or its month has begun and its due date has not arrived. A period whose early payment was voided before its due date is therefore payable again, and a payment writes the period's rent and instalment only when the period does not already carry its charge. | CONFIRMED BY USER (Tomás, 2026-10-14: a tenant paying the month as soon as it starts must be allowed), **strong**; paying a month that has not begun was not asked and stays refused |
| 38 | **Back-dating bound: the received date may be typed up to 7 calendar days before today, by either role.** An earlier date is refused. The 7 is one named constant, never a literal repeated across the code, and the database refuses a date outside it as well. Fills the bound Decision 16 left open. | CONFIRMED BY USER (Tomás, 2026-10-14), **medium**: the agency has not been asked, and Nicolás may still narrow it |
| 39 | **The starting recibo number is set by Volkode at go-live, under an Admin login. Only Admin may set it**, and the database enforces that (Empleado is refused), not the UI. | CONFIRMED BY USER (Tomás, 2026-10-14) that Volkode sets it under an Admin login; **medium** on "Admin only" as the permanent rule (it follows from data loading being an Admin act today) |
| 40 | **The collection event (`collections`) is a fifth table in the access-control requirement and in Test 65**, insert-only like recibos. **The counter's grants are narrowed**: both roles may advance it, only Admin may set its starting number. | SPEC CALL following design D7 and D9, **medium** (design §12 rows 1 and 2) |
| 41 | **A pre-existing contract accrues nothing, rent or instalment, for a period due on or before its cutover date** (a date **stored on the contract** when it is loaded as one that predates the system). Without it, lazy accrual would raise every month since signing on top of the opening balance. Extends Decision 32 from instalments to rent. **Amended 2026-10-14:** was "the date its earliest opening balance was entered"; a contract read before its opening balances were typed would then have had no cutover and materialised its whole history, permanently, on an append-only ledger. The stored date cannot change once the account holds a movement. | CONFIRMED BY USER (Tomás, 2026-10-14); **medium**. VERIFIED gap: the schedule walks from the contract's start (design §12 row 3); VERIFIED: `MoraWorklistQuery.cs:40-48` materialises every account on each read, which is why the date is stored rather than derived |
| 42 | **An early payment racing the lazy accrual of the same period leaves exactly one `RentAccrual` and at most one instalment**; the loser writes nothing. The database, not the application, settles the race. | SPEC CALL following design D1, **strong** (unique index precedent); proven by Test 70 |
| 43 | **"On the contract" means "a period the contract charges rent for, or one carrying an opening balance".** A calendar month the schedule never charges (a contract starting on the 20th with due day 10 never charges its first calendar month) is not on the contract and is refused. | SPEC CALL following design D24, **medium** |
| 44 | **A voiding's `Correction` is dated on the later of the voiding day and the date of the movement it reverses.** An ordinary late payment is unchanged (the voiding day); an early payment voided before its due date is not reversed by a movement that precedes it. | SPEC CALL following design D2, **medium** (design §12 row 6) |
| 45 | **Voiding window.** Up to **7 calendar days** after the payment was recorded, either role may void. From day **8 to day 30**, **Admin only**. After day 30, **nobody**. The restriction is enforced by the database on the annulment insert, not only by the application. Supersedes "who may void is design's" (Decision 33). | TEAM DECISION (Tomás, 2026-10-14), **medium** on the shape (ordinary cash-desk internal control; no law or standard fixes a limit for a non-fiscal recibo). **WEAK on the 30**: a stand-in for "until the period is liquidated to the owner", to be replaced when `liquidacion-al-dueno` exists |
| 46 | **An issued recibo's honorarios de administración line is stored and never recomputed.** Since `Contract` has no way to change the rate after construction, "a later change of rate" is proven as "reading the issued recibo against a contract carrying another rate leaves the line as issued". | SPEC CALL following design §12 row 8, **medium** |
| 47 | **`Payment` and `RecargoCharged` are dated on the later of the received date and the period's charge date** (the date of its `RentAccrual` or `OpeningBalance`). For an ordinary late payment that is the received date; for an early payment it is the due date, so nothing about a period appears on the ledger before it was charged. The received date itself is stored on the collection and printed on the recibo. | SPEC CALL following design D2, **medium** |
| 48 | **An early-written rent is final.** The `RentAccrual` an early payment writes carries the canon the schedule gives the period when it is written. A rent adjustment taking effect on or before that period's due date and confirmed after the early payment does not re-price it: it is a late confirmation under the living requirement "A Period Accrues at the Canon in Force, and Never Re-Prices" (`openspec/specs/contract-account/spec.md`), and the next period accrues at the adjusted canon. | SPEC CALL following the living rule and design D26, **medium-strong** (the living rule already covers a late confirmation; this names that an early write is one) |
| 49 | **A month carried by an opening balance prints its figure on a Saldo Deudor line**, on both recibos, not on the rent line (the physical recibo has a "Saldo Deudor" row). Its honorarios de administración base is that figure itself, truncated as usual: it already holds the month's rent and mora (Decision 31), so there is no recargo to add. The base is **that one month's figure**, never the tenant's whole loaded debt (each owed month is its own opening balance, Decision 30). | CONFIRMED BY USER (Tomás, 2026-10-14: "cifra completa del mes que adeuda, no desde todo"), **medium-strong** |

## Table of Contents

1. [Capability: collection (NEW)](#capability-collection-new)
   1. [Requirement: A Payment Is a Total Payment of One Period](#requirement-a-payment-is-a-total-payment-of-one-period)
   2. [Requirement: The Tenant Names the Period; the Oldest Unpaid One Is the Default](#requirement-the-tenant-names-the-period-the-oldest-unpaid-one-is-the-default)
   3. [Requirement: A Payment Charges the Recargo and Settles the Period in One Transaction](#requirement-a-payment-charges-the-recargo-and-settles-the-period-in-one-transaction)
   4. [Requirement: A Payment Carries Two Dates](#requirement-a-payment-carries-two-dates)
   5. [Requirement: One Live Payment per Period, Under Concurrency](#requirement-one-live-payment-per-period-under-concurrency)
   6. [Requirement: A Confirmation Preview Precedes Every Payment](#requirement-a-confirmation-preview-precedes-every-payment)
   7. [Requirement: Voiding Reverses the Payment and Annuls Both Recibos](#requirement-voiding-reverses-the-payment-and-annuls-both-recibos)
2. [Capability: receipt (NEW)](#capability-receipt-new)
   1. [Requirement: Every Payment Issues Two Recibos](#requirement-every-payment-issues-two-recibos)
   2. [Requirement: Recibo Numbers Are Consecutive, Gapless and From One Sequence](#requirement-recibo-numbers-are-consecutive-gapless-and-from-one-sequence)
   3. [Requirement: The Starting Number Is a Cutover Parameter, Set Before the First Recibo](#requirement-the-starting-number-is-a-cutover-parameter-set-before-the-first-recibo)
   4. [Requirement: A Recibo Is a Header and Typed Lines](#requirement-a-recibo-is-a-header-and-typed-lines)
   5. [Requirement: Ledger-Derived Lines Are Never Typed](#requirement-ledger-derived-lines-are-never-typed)
   6. [Requirement: Tasa Municipal, Seguro and Otros Conceptos Are Typed at Closing and Never Touch the Ledger](#requirement-tasa-municipal-seguro-and-otros-conceptos-are-typed-at-closing-and-never-touch-the-ledger)
   7. [Requirement: The Propietario Recibo Carries the Honorarios de Administración](#requirement-the-propietario-recibo-carries-the-honorarios-de-administración)
   8. [Requirement: The Recibo Total Is Not the Ledger Figure](#requirement-the-recibo-total-is-not-the-ledger-figure)
   9. [Requirement: A Recibo Is Never Edited or Deleted; an Annulment Is Appended](#requirement-a-recibo-is-never-edited-or-deleted-an-annulment-is-appended)
   10. [Requirement: The Recibo Is Labelled Non-Fiscal](#requirement-the-recibo-is-labelled-non-fiscal)
3. [Capability: contract-account (MODIFIED)](#capability-contract-account-modified)
   1. [ADDED Requirement: Payment and RecargoCharged Are Ledger Movements](#added-requirement-payment-and-recargocharged-are-ledger-movements)
   2. [RENAMED and MODIFIED Requirement: A Payment Settles the Period the Tenant Names, or the Oldest Unpaid One](#renamed-and-modified-requirement-a-payment-settles-the-period-the-tenant-names-or-the-oldest-unpaid-one)
   3. [MODIFIED Requirement: An Opening Balance Exists Only for a Contract That Predates the System](#modified-requirement-an-opening-balance-exists-only-for-a-contract-that-predates-the-system)
   4. [ADDED Requirement: A Honorarios Contractuales Instalment Is Payable With Its Rent Period](#added-requirement-a-honorarios-contractuales-instalment-is-payable-with-its-rent-period)
   5. [ADDED Requirement: A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date](#added-requirement-a-pre-existing-contract-accrues-nothing-due-on-or-before-its-cutover-date)
   6. [Unchanged requirements that this change relies on](#unchanged-requirements-that-this-change-relies-on)
4. [Capability: lease-contract (MODIFIED)](#capability-lease-contract-modified)
   1. [ADDED Requirement: A Contract Carries a Honorarios Contractuales Plan](#added-requirement-a-contract-carries-a-honorarios-contractuales-plan)
   2. [MODIFIED Note: Honorarios Percentage Is Stored, Nullable](#modified-note-honorarios-percentage-is-stored-nullable)
5. [Capability: access-control (MODIFIED)](#capability-access-control-modified)
   1. [ADDED Requirement: The Collection Tables Are Granted Deliberately, and the Counter Is a Named Departure](#added-requirement-the-collection-tables-are-granted-deliberately-and-the-counter-is-a-named-departure)
6. [Out of Scope](#out-of-scope)
7. [Parameters Left to Design](#parameters-left-to-design)
8. [Tests](#tests)

---

## Capability: collection (NEW)

### Purpose

Taking a tenant's payment against one period of a contract's account: total only, for the period the
tenant names or else the oldest unpaid, with the recargo turned into a stored charge at that instant,
the recibos issued, and the whole thing either entirely written or not written at all. It also owns
voiding. It does not pay the owner, render a PDF, or present a screen.

### Requirement: A Payment Is a Total Payment of One Period

A payment MUST settle exactly one period of one contract account, in full. The amount a payment is
for MUST equal that period's ledger total as of the received date: the period's rent, plus its
honorarios contractuales instalment if one is attached, plus the recargo projected for it as of that
date. A payment for any other amount MUST be refused and MUST write nothing.

The system MUST NOT represent a partially paid period, MUST NOT allocate a fraction of one payment
across several periods, and MUST NOT carry an excess forward as credit. Paying two months is two
payments, each with its own recibos.

#### Scenario: A partial amount is refused

- GIVEN a period whose ledger total as of the received date is 500,000
- WHEN a payment of 300,000 is offered against it
- THEN it MUST be refused
- AND no movement, recibo or number MUST be written

#### Scenario: An excess amount is refused

- GIVEN the same period
- WHEN a payment of 600,000 is offered against it
- THEN it MUST be refused
- AND nothing MUST be written

#### Scenario: Two months paid at once are two payments

- GIVEN a tenant handing over two months at the same visit
- WHEN both are collected
- THEN two payments MUST exist, one per period
- AND four recibos MUST be issued, two per payment

#### Scenario: A payment against a frozen account is refused

- GIVEN an account whose recargo was frozen by a novación
- WHEN a payment is attempted against it
- THEN it MUST be refused with a message that the account is under a payment plan
- AND nothing MUST be written

### Requirement: The Tenant Names the Period; the Oldest Unpaid One Is the Default

A payment MAY name the period it settles. A named period MUST be **payable** as of the received date:
no live payment settles it, **and** either it is **owed** (its movements do not net to zero) or its
**month has begun** and its due date has not arrived (Decision 37). A period that is already settled,
whose month has not begun, or that is not on the contract MUST be refused and MUST write nothing. A
period is **on the contract** when the contract charges rent for it (the accrual schedule produces
it) or when it carries an opening balance (Decision 43). When
no period is named, the payment MUST settle the oldest unpaid period on the account or, when nothing
is owed, the month in course.

A period paid before its due date MUST have its charges brought onto the account by the payment
itself: its rent and, when the period carries one, its honorarios contractuales instalment, with no
recargo. It MUST write them only when the period does not already carry its charge: a period whose
early payment was voided before its due date still holds the rent and instalment that payment wrote,
so paying it again writes neither a second time. The later accrual MUST NOT raise either of them a
second time.

The rent an early payment writes is final. A rent adjustment taking effect on or before that period's
due date and confirmed afterwards MUST NOT re-price it: it is a late confirmation under the living
requirement "A Period Accrues at the Canon in Force, and Never Re-Prices", and the next period
accrues at the adjusted canon (Decision 48).

Every recibo MUST state the period it settles. Naming a period never changes what the other periods
owe: each unpaid period keeps counting recargo from its own due date.

A period carrying an opening balance is due from the moment the account exists (it fell due before
the system did).

This is the rewrite of the living requirement named under `contract-account` below.

#### Scenario: The tenant names a newer period while an older one is owed

- GIVEN an account with July, August and September all unpaid and due
- WHEN a payment names September
- THEN September MUST be the period settled
- AND July and August MUST remain unpaid, each still counting recargo from its own due date

#### Scenario: No period named, the oldest is settled

- GIVEN the same account
- WHEN a payment names no period
- THEN July MUST be the period settled
- AND the recibos MUST state July

#### Scenario: A period that is already settled is refused

- GIVEN a period that a live payment already settles
- WHEN a payment names it
- THEN it MUST be refused and nothing MUST be written

#### Scenario: The current month is paid before its due date

- GIVEN a contract due on the 10th with nothing owed, and a received date of the 2nd of the month
- WHEN a payment names that month, or names none
- THEN that month MUST be the period settled, with no recargo
- AND the later accrual MUST NOT charge that month again

#### Scenario: A month that has not begun is refused

- GIVEN a period whose month begins after the received date
- WHEN a payment names it
- THEN it MUST be refused and nothing MUST be written

#### Scenario: A period that is not on the contract is refused

- GIVEN a month before the contract's start or after its end
- WHEN a payment names that month
- THEN it MUST be refused and nothing MUST be written

#### Scenario: A month the contract never charges is refused

- GIVEN a contract starting on the 20th with its rent due on the 10th, which never charges rent for
  its first calendar month
- WHEN a payment names that first calendar month
- THEN it MUST be refused and nothing MUST be written

#### Scenario: An early payment and the lazy accrual race

- GIVEN a period whose month has begun and whose due date has not arrived
- WHEN an early payment of it and the lazy accrual of the same period commit at the same time
- THEN exactly one `RentAccrual`, and at most one instalment, MUST exist for the period
- AND the commit that loses MUST write nothing

#### Scenario: A voided early payment is paid again before its due date

- GIVEN a period due on the 10th, paid on the 2nd and voided on the 3rd
- WHEN a payment names it on the 4th
- THEN it MUST be accepted, with no recargo
- AND no second `RentAccrual` and no second instalment MUST be written for the period
- AND the period MUST net to zero afterwards

#### Scenario: An early-written rent is not re-priced by a later-confirmed adjustment

- GIVEN July, due on the 10th, paid on 2 July at a canon of 500,000
- AND a rent adjustment taking effect on 1 July, confirmed on 5 July
- WHEN the account is read
- THEN July's `RentAccrual` MUST still be 500,000 and no movement MUST be generated for it
- AND the next period MUST accrue at the adjusted canon

#### Scenario: An account with nothing payable has no default period

- GIVEN an account with no unpaid period, whose month in course is already settled or outside the
  contract
- WHEN a payment names no period
- THEN it MUST be refused and nothing MUST be written

### Requirement: A Payment Charges the Recargo and Settles the Period in One Transaction

Committing a payment MUST write, in one transaction and nothing short of all of it:

1. a `RecargoCharged` movement for the settled period, equal to the recargo projected for that period
   as of the received date, truncated to a whole peso;
2. a `Payment` movement for the same period, of the opposite sign, equal to the period's ledger total
   including that recargo;
3. the locatario recibo and the propietario recibo, with their lines;
4. the advance of the recibo counter.

If anything in this set cannot be written, **none** of it MUST persist, **and no recibo number MUST be
consumed**. After a commit the period MUST net to zero, so that neither the oldest-unpaid rule nor the
amount owed reports it, and so that the amount owed read on any later day does not project recargo on
top of the recargo already charged.

When the projected recargo is zero, no `RecargoCharged` movement MUST be written and no recargo line
MUST appear on either recibo (Decision 24).

The `Payment` and `RecargoCharged` movements MUST be dated on the later of the received date and the
period's charge date (Decision 47). The received date itself is kept on the payment and printed on the
recibo, whatever the movements are dated.

#### Scenario: A committed payment leaves the period at zero

- GIVEN a period of 500,000 rent, 10 days late, projecting a recargo
- WHEN the payment is committed
- THEN the period's movements MUST net to zero
- AND `RecargoCharged` and `Payment` MUST both exist for that period

#### Scenario: No double recargo the next day

- GIVEN the committed payment above
- WHEN the amount owed is read on the following day
- THEN no recargo MUST be projected for the settled period

#### Scenario: A failure after the recargo charge persists nothing

- GIVEN a commit in which the `RecargoCharged` movement has been written
- WHEN the writing of a later part of the set fails
- THEN no `RecargoCharged`, no `Payment`, no recibo and no line MUST exist afterwards
- AND the counter MUST read exactly what it read before

#### Scenario: A failure while issuing the recibos consumes no number

- GIVEN a commit in which the locatario recibo has been numbered
- WHEN the propietario recibo cannot be written
- THEN neither recibo MUST persist
- AND the next successful payment MUST receive the number the failed one would have received

#### Scenario: An early payment is dated on the due date

- GIVEN a period due on the 10th and a payment received on the 2nd
- WHEN the payment is committed
- THEN its `Payment` MUST be dated the 10th, the same date as the period's `RentAccrual`
- AND the recibo and the payment record MUST still show the 2nd as the received date

#### Scenario: A period inside the grace period charges no recargo

- GIVEN a period paid on a day that projects a recargo of zero
- WHEN the payment is committed
- THEN no `RecargoCharged` movement MUST exist
- AND the recibos MUST carry no recargo line

### Requirement: A Payment Carries Two Dates

A payment MUST carry two dates and MUST NOT conflate them.

- **Received on**: the day the money was received. It MUST default to today. It MAY be typed earlier.
  It MUST NOT be later than today, and MUST NOT be more than **7 calendar days** before today
  (Decision 38). Recargo MUST be
  computed as of this date, and whether a period is payable MUST be judged as of this date.
- **Recorded at**: the instant the system recorded the payment. It MUST be set by the system, MUST NOT
  be supplied by any caller, and MUST NOT be updatable.

Both MUST be stored. The recibo MUST show the received date. "Today" is the agency's local calendar
date.

A back-dated payment lowers the recargo, so the received date MUST be visible on the preview and the
recibo and MUST be bounded, never free. Either role MAY type a date other than today within the
bound. The 7 MUST be one named constant, not a literal repeated across the code, and the database
MUST refuse a received date outside the bound whatever the application did. Consequence, stated so
nobody "fixes" it:
recibo numbers follow recording order, so a back-dated recibo can carry a higher number than one
dated after it.

#### Scenario: Received on defaults to today

- GIVEN a payment with no received date supplied
- WHEN it is committed
- THEN its received date MUST be today
- AND its recorded-at MUST be set by the system

#### Scenario: Yesterday's money, recorded today, owes yesterday's recargo

- GIVEN a period that projects a larger recargo today than it did yesterday
- WHEN a payment is committed today with received date yesterday
- THEN the recargo charged MUST be the one projected as of yesterday
- AND the recibo MUST show yesterday as the received date

#### Scenario: A future received date is refused

- GIVEN a payment with a received date after today
- WHEN it is attempted
- THEN it MUST be refused and nothing MUST be written

#### Scenario: A received date beyond the bound is refused

- GIVEN a payment whose received date is 8 or more calendar days before today
- WHEN it is attempted
- THEN it MUST be refused and nothing MUST be written

#### Scenario: Either role may back-date within the bound

- GIVEN an Empleado, and separately an Admin, each taking a payment with a received date 7 calendar
  days before today
- WHEN it is committed
- THEN it MUST be accepted for both, with that received date

#### Scenario: The database refuses a received date beyond the bound

- GIVEN a collection inserted directly, bypassing the application, with a received date 8 calendar
  days before today
- WHEN it is attempted
- THEN it MUST be refused

#### Scenario: Recorded-at cannot be supplied or changed

- GIVEN a committed payment
- WHEN a caller supplies a recorded-at value, or an update of the stored value is attempted
- THEN it MUST be ignored at creation and refused afterwards

### Requirement: One Live Payment per Period, Under Concurrency

At most one **live** payment (one not reversed by a voiding) MUST exist per account and period. When
two operators commit a payment for the same period at the same time, exactly one MUST succeed and the
other MUST be refused and MUST write nothing. This MUST hold in the database, not only in the
application.

After a voiding, the period is unpaid again and MUST accept a new payment.

#### Scenario: Two operators pay the same period

- GIVEN two sessions that each preview and commit a payment for the same period
- WHEN both commit
- THEN exactly one payment MUST exist for that period
- AND the other commit MUST be refused with no movement, recibo or number written

#### Scenario: A voided period can be paid again

- GIVEN a period whose payment was voided
- WHEN a new payment is committed for it
- THEN it MUST succeed
- AND the voided payment MUST remain on the ledger, reversed

### Requirement: A Confirmation Preview Precedes Every Payment

Taking a payment MUST be a two-step use case, per convention `confirm-with-a-summary`. The first step
MUST return a summary and MUST write nothing. The second step commits, and only on confirmation.

The summary MUST show:

- the period being settled and the received date;
- the **previous amount owed** on the account, as of the received date;
- every line of the locatario recibo and of the propietario recibo, each with its amount and sign;
- the **recibo total** of each recibo, and the **ledger figure** the payment settles (rent +
  instalment + recargo), **named separately** and never under one name (see "The Recibo Total Is Not
  the Ledger Figure");
- the **resulting balance**, which is the amount owed on the account after the payment, naming the
  concepts it covers (rent, instalments, recargo);
- any condition that would refuse the commit, such as a contract whose administration rate was never
  entered;
- a warning when the contract's administration rate is 0%.

A commit MUST apply exactly what was previewed. If any previewed figure differs at commit time, the
commit MUST be refused, MUST write nothing, and a fresh preview MUST be required.

Fractional pesos typed on a line MUST be truncated toward zero, and the preview MUST show the
truncated figure.

#### Scenario: The preview shows everything and writes nothing

- GIVEN a payable period
- WHEN the preview is requested
- THEN it MUST show the period, the previous amount owed, each line, both totals and the resulting
  balance
- AND the numbers of movements, recibos and the counter value MUST be identical before and after

#### Scenario: The two figures are named apart

- GIVEN a preview with a tasa municipal of 28,900 typed
- WHEN it is read
- THEN the recibo total MUST include the 28,900
- AND the ledger figure MUST NOT include it
- AND the two MUST carry different names

#### Scenario: Another operator pays between preview and commit

- GIVEN a preview taken for a period
- AND another operator commits a payment for that period before this one confirms
- WHEN this commit is attempted
- THEN it MUST be refused and a fresh preview MUST be required

#### Scenario: The day turns over between preview and commit

- GIVEN a preview whose recargo was projected as of a received date that defaulted to today
- AND the commit runs after midnight, so the default received date would now differ
- WHEN the commit is attempted
- THEN it MUST be refused and a fresh preview MUST be required

#### Scenario: A fractional typed amount is truncated and shown

- GIVEN a seguro typed as 1,250.80
- WHEN the preview is requested
- THEN the seguro line MUST read 1,250

### Requirement: Voiding Reverses the Payment and Annuls Both Recibos

Voiding MUST be applied to a payment's recibo pair. Annulling either recibo of a pair MUST annul both,
because both describe the same payment (Decision 19). Voiding MUST:

1. append an annulment record for each recibo of the pair, naming the voided recibo, the reason (which
   MUST be given), the acting user and the time;
2. append `Correction` movements that reverse the `Payment` and the `RecargoCharged` of that payment,
   each carrying the opposite sign and naming the movement it corrects, and each dated on the later
   of the voiding day and the date of the movement it reverses (Decision 44);
3. leave every recibo number, every line and every original movement readable and unchanged.

A voided number MUST NOT be reused and MUST NOT be deleted. After voiding, the period MUST be unpaid
again, its recargo MUST again be projected from its own due date, and a new payment for it MUST take
the next numbers. A recibo already annulled MUST NOT be annulled again. Voiding moves money, so it
MUST show a summary before it is confirmed (previous amount owed, what is reversed, resulting
amount owed).

**Who may void, and until when** (Decision 45). The age of a pair is the number of calendar days, in the
agency's local calendar, between the day its payment was recorded and the day of the voiding.

| Age of the pair | Who may void it |
|---|---|
| 0 to 7 days | Admin or Empleado |
| 8 to 30 days | Admin only |
| 31 days or more | Nobody |

A refused voiding MUST write nothing. The restriction MUST be enforced by the database when the
annulment is recorded, not only by the application, and it MUST hold for raw SQL, because both roles
hold `INSERT` on annulments (see `access-control`). The age MUST be measured from the instant the
database stamped on the payment, never from a value any caller supplied. The figures are constants in
one place. The 30 is weak (Decision 45).

The honorarios contractuales instalment is not reversed by a voiding: it was raised with the rent
period, not by the payment, and it returns to unpaid together with the rent.

#### Scenario: Voiding makes the period unpaid again

- GIVEN a paid period
- WHEN its recibo is voided with a reason
- THEN corrections MUST exist for the `Payment` and the `RecargoCharged`
- AND the period MUST net to its rent and instalment again
- AND `OldestUnpaidPeriod` MUST report it again if it is the oldest

#### Scenario: Voiding one recibo voids the pair

- GIVEN a payment with a locatario recibo N and a propietario recibo N+1
- WHEN recibo N+1 is voided
- THEN both N and N+1 MUST be marked annulled

#### Scenario: A voided number stays visible and is never reused

- GIVEN a voided pair N and N+1
- WHEN a new payment is committed
- THEN the new recibos MUST take N+2 and N+3
- AND N and N+1 MUST remain readable, marked annulled, showing reason, user and time

#### Scenario: Earlier history reads as it did

- GIVEN a payment voided today
- WHEN the ledger balance is read for a date before the voiding
- THEN it MUST still show the payment as written, because a correction is dated on the voiding day
  (or on the date of the movement it reverses, if later), never before it

#### Scenario: A correction never precedes the payment it reverses

- GIVEN a payment taken on the 2nd for a period due on the 10th, with its `Payment` dated the 10th,
  and voided on the 5th
- WHEN the corrections are read
- THEN they MUST be dated the 10th, not the 5th
- AND the period MUST read as unpaid and owed from the 10th, counting recargo from the 10th

#### Scenario: Either role voids within 7 days

- GIVEN a pair recorded 7 days ago
- WHEN an Empleado, and separately an Admin, voids it with a reason
- THEN the voiding MUST succeed for both

#### Scenario: Only Admin voids from day 8 to day 30

- GIVEN a pair recorded 8 days ago
- WHEN an Empleado attempts to void it
- THEN it MUST be refused by the database and nothing MUST be written
- AND an Admin voiding the same pair MUST succeed
- AND an Admin voiding a pair recorded 30 days ago MUST succeed

#### Scenario: Nobody voids after day 30

- GIVEN a pair recorded 31 days ago
- WHEN an Admin, and separately an Empleado, attempts to void it
- THEN both MUST be refused and nothing MUST be written

#### Scenario: A recibo cannot be annulled twice

- GIVEN an annulled pair
- WHEN a voiding is attempted on either
- THEN it MUST be refused and nothing MUST be written

#### Scenario: A voiding needs a reason

- GIVEN a live pair
- WHEN a voiding is attempted without a reason
- THEN it MUST be refused and nothing MUST be written

---

## Capability: receipt (NEW)

### Purpose

The recibo: the numbered document handed over when a tenant pays. A header plus signed, typed lines,
in two copies per payment (*locatario* and *propietario*), numbered gaplessly from the agency's own
sequence, never edited, annulled by an appended record. It is a document, not a PDF: rendering is
`recibo-pdf`.

### Requirement: Every Payment Issues Two Recibos

Each payment MUST issue exactly two recibos: one headed *locatario* and one headed *propietario*.
Both MUST name the same contract, tenant, settled period, received date and lessors, and both MUST be
issued in the same transaction as the payment. A payment MUST NOT issue only one, and a recibo MUST
NOT exist without a payment.

#### Scenario: One payment, two recibos

- GIVEN a committed payment
- WHEN its recibos are read
- THEN exactly one locatario recibo and one propietario recibo MUST exist for it
- AND both MUST state the same period and received date

### Requirement: Recibo Numbers Are Consecutive, Gapless and From One Sequence

Recibo numbers MUST come from one sequence shared by both kinds. A payment's two numbers MUST be
consecutive, the locatario recibo taking the lower one (Decision 23), and no other recibo number MUST
lie between them. Across the agency, the numbers actually issued MUST form an unbroken run: no number
skipped, none duplicated.

A rolled-back issuance MUST NOT consume a number. A voided recibo keeps its number. Under concurrent
issuance, two sessions MUST NOT receive the same number and MUST NOT leave a gap. A database
`SEQUENCE` MUST NOT be the mechanism, because it leaves gaps on rollback. The database MUST refuse a
second recibo with an existing number whatever the application did.

Where the counter lives, and how a transaction-mode connection pooler affects its locking, are
**design's** and MUST be verified against the real pooler.

#### Scenario: Two numbers, consecutive

- GIVEN the last issued number is 24268
- WHEN a payment is committed
- THEN the locatario recibo MUST be 24269 and the propietario recibo 24270

#### Scenario: Concurrent issuance leaves no gap and no duplicate

- GIVEN several sessions committing payments for different periods at the same time
- WHEN all complete
- THEN the issued numbers MUST be one unbroken run
- AND each payment's pair MUST be adjacent

#### Scenario: The database refuses a duplicate number

- GIVEN an issued recibo numbered 24269
- WHEN a second recibo with number 24269 is inserted, bypassing the application
- THEN it MUST be refused

#### Scenario: A rollback leaves the sequence unbroken

- GIVEN a commit that fails after numbering
- WHEN the next payment is committed
- THEN it MUST receive the first number the failed one would have received

### Requirement: The Starting Number Is a Cutover Parameter, Set Before the First Recibo

The first number the system issues MUST be a parameter set at data loading (the agency continues
"desde donde quedaron", §7), not a constant in code. It MUST be a positive whole number. Until it has
been set, issuing a recibo MUST be refused (there MUST be no silent default). It MAY be set, and
corrected, until the first recibo exists; from the moment the first recibo is issued, any attempt to
change it MUST be refused. The first recibo issued MUST carry exactly that number.

Only an Admin may set it: Volkode sets it at go-live under an Admin login (Decision 39). An Empleado
attempting it MUST be refused by the database, not only by the application, and the grant that allows
it is named in `access-control` below.

#### Scenario: The first recibo carries the cutover number

- GIVEN a starting number of 31001 and no recibo yet
- WHEN the first payment is committed
- THEN its locatario recibo MUST be 31001 and its propietario recibo 31002

#### Scenario: Only an Admin may set the starting number

- GIVEN no recibo yet
- WHEN an Empleado attempts to set the starting number, including by a direct `UPDATE`
- THEN it MUST be refused
- AND an Admin setting it MUST be accepted

#### Scenario: A mistyped starting number is corrected before use

- GIVEN a starting number set to 31001 and no recibo yet
- WHEN it is set to 31100
- THEN it MUST be accepted

#### Scenario: The starting number is locked after the first recibo

- GIVEN at least one recibo issued
- WHEN a change of the starting number is attempted
- THEN it MUST be refused, and subsequent numbering MUST be unaffected

#### Scenario: No number is issued before cutover is set

- GIVEN a counter whose starting number has not been set
- WHEN a payment is committed
- THEN it MUST be refused and nothing MUST be written

#### Scenario: An impossible starting number is refused

- GIVEN a starting number of 0 or a negative number
- WHEN it is set
- THEN it MUST be refused

### Requirement: A Recibo Is a Header and Typed Lines

A recibo MUST consist of a header and a set of lines, each line having a kind, a signed amount and its
place on the recibo. The header MUST carry the number, whether it is locatario or propietario, the
contract and its units, the tenant, the lessors, the settled period, the received date, the
non-fiscal legend and the total. The set of line kinds MUST be a catalog that a later change can
extend without changing the meaning of existing recibos. This follows the project rule that
receipt and collection concepts are catalog + lines, not fixed columns.

Every amount on a recibo MUST be a whole peso, truncated, held as `decimal`. Floating point MUST NOT
be used.

#### Scenario: A recibo reads as header plus lines

- GIVEN an issued recibo
- WHEN it is read
- THEN it MUST expose its header fields and an ordered set of lines, each with kind and signed amount
- AND its total MUST equal the sum of its signed lines

#### Scenario: No floating point

- GIVEN the implemented recibo
- WHEN it is inspected
- THEN every monetary member MUST be `decimal` and no `double` or `float` MUST appear

### Requirement: Ledger-Derived Lines Are Never Typed

The settled period's **rent**, the **honorarios contractuales instalment** due with it (if any) and
the **recargo charged** MUST be derived from the ledger and MUST NOT be typed by the operator. The
operator MUST NOT be able to supply or override them.

The locatario recibo carries all three that apply. The propietario recibo carries rent and recargo; it
does not carry the instalment (Decision 20, the tenant's fee prints only on the tenant's part, §2).

A period carried by an `OpeningBalance` has no rent line: both recibos print the opening-balance
figure on a **Saldo Deudor** line instead, derived from the ledger like the rent (default, Decision
49). Such a period has no recargo line (Decision 31).

#### Scenario: The locatario recibo carries rent, instalment and recargo

- GIVEN a period with 507,000 rent, an instalment of 30,000 and a recargo of 91,260
- WHEN the payment is committed
- THEN the locatario recibo MUST show those three lines with those amounts

#### Scenario: The propietario recibo omits the instalment

- GIVEN the same payment
- WHEN the propietario recibo is read
- THEN it MUST show rent and recargo
- AND it MUST NOT show the honorarios contractuales instalment

#### Scenario: An opening-balance month prints a Saldo Deudor line

- GIVEN a month loaded as an `OpeningBalance` of 160,000 and a contract rate of 8%
- WHEN the payment is committed
- THEN both recibos MUST carry a Saldo Deudor line of 160,000 and no rent or recargo line
- AND the propietario recibo's honorarios de administración line MUST be -12,800

#### Scenario: A derived line cannot be overridden

- GIVEN a payment in which the operator supplies an amount for the rent line
- WHEN it is attempted
- THEN the supplied amount MUST be ignored or refused, and the rent line MUST equal the ledger's

### Requirement: Tasa Municipal, Seguro and Otros Conceptos Are Typed at Closing and Never Touch the Ledger

At closing the operator MAY type up to three amounts: **tasa municipal**, **seguro**, and **Otros
Conceptos**, each its own line. They MUST be typed, never computed or accrued, and MUST NOT be written
to the contract account as movements. Each typed amount MUST appear identically on both recibos of
the payment. An omitted or zero amount MUST produce no line.

**Seguro and Otros Conceptos are signed**: each MUST accept a negative amount. The positive-only
guards that protect the monthly rent and the index levels MUST NOT be applied to them. Tasa
municipal MUST NOT be negative (Decision 25).

#### Scenario: A negative Otros Conceptos is accepted

- GIVEN a payment closed with Otros Conceptos typed as -23,382
- WHEN it is committed
- THEN the line MUST appear with that negative amount and reduce the recibo total
- AND the account MUST show no movement for it

#### Scenario: A negative seguro is accepted

- GIVEN a payment closed with a seguro typed as -5,000
- WHEN it is committed
- THEN the line MUST appear on both recibos with that negative amount

#### Scenario: A negative tasa municipal is refused

- GIVEN a payment closed with a tasa municipal typed as -1
- WHEN it is attempted
- THEN it MUST be refused and nothing MUST be written

#### Scenario: Typed amounts never reach the ledger

- GIVEN a payment closed with tasa municipal 28,900, seguro 4,000 and Otros Conceptos -23,382
- WHEN the account's movements and its balance are read
- THEN no movement for any of the three MUST exist
- AND the balance MUST be unchanged by them

#### Scenario: Seguro has its own line

- GIVEN a payment with both a seguro and an Otros Conceptos typed
- WHEN the recibo is read
- THEN they MUST be two separate lines

### Requirement: The Propietario Recibo Carries the Honorarios de Administración

The propietario recibo MUST carry one **honorarios de administración** line, **negative**, equal to
the administration rate stored on the contract applied to the fee base (rent of the settled period
plus the recargo charged on it, Decision 21; for a month carried by an opening balance, that figure
itself, Decision 49), truncated to a whole peso. The amount is computed at
issuance and stored on the recibo; a later change of the rate MUST NOT alter an issued recibo, and nothing MUST
recompute the line from the contract once issued (Decision 46). The
line MUST NOT appear on the locatario recibo and MUST NOT be written to the contract account (the
account covers rent, instalments and recargo only).

All the contract's lessors MUST appear together on **one** propietario recibo. This change does not
split anything among them; paying the owner is `liquidacion-al-dueno`.

A contract whose rate was never entered cannot be committed (Decision 22); the preview MUST say so. A
contract with a rate of 0% MAY be committed; its line is zero and the preview MUST warn about it.

#### Scenario: The line is negative and derived from the rate

- GIVEN a contract with a rate of 8%, rent 507,000 and a recargo of 91,260
- WHEN the payment is committed
- THEN the propietario recibo MUST carry an honorarios de administración line of -47,860
- AND the locatario recibo MUST carry no such line

#### Scenario: A later change of rate leaves an issued recibo alone

- GIVEN an issued propietario recibo with a line of -47,860
- WHEN it is read against a contract carrying a different rate (the contract has no operation that
  changes its rate after construction, so this is how a later change is represented)
- THEN the issued recibo MUST still read -47,860

#### Scenario: Several lessors, one propietario recibo

- GIVEN a contract with two lessors
- WHEN the payment is committed
- THEN one propietario recibo MUST exist, naming both lessors

#### Scenario: The fee never reaches the account

- GIVEN a committed payment
- WHEN the account's movements are read
- THEN no honorarios de administración movement MUST exist

#### Scenario: No rate, no commit

- GIVEN a contract whose administration rate was never entered
- WHEN a payment is previewed and then committed
- THEN the preview MUST state the missing rate
- AND the commit MUST be refused and write nothing

#### Scenario: A 0% rate is collected with a warning

- GIVEN a contract with an administration rate of 0%
- WHEN a payment is previewed and then committed
- THEN the preview MUST warn that the contract charges no administration fee
- AND the commit MUST succeed, with an honorarios de administración line of zero

### Requirement: The Recibo Total Is Not the Ledger Figure

A recibo's total is the sum of all its lines, including the typed ones. The ledger figure a payment
settles is rent + instalment + recargo only. The two MUST be named apart wherever shown, and the
recibo total MUST NOT be presented as the account balance or as the amount owed.

#### Scenario: Different totals, different names

- GIVEN a payment with a seguro of 4,000 typed
- WHEN the preview and the recibo are read
- THEN the recibo total MUST exceed the ledger figure by 4,000
- AND each MUST be labelled for what it is

### Requirement: A Recibo Is Never Edited or Deleted; an Annulment Is Appended

An issued recibo and its lines MUST NOT be updated or deleted, including by raw SQL. A wrong recibo
MUST be corrected by voiding (see `collection`) and issuing a new one. An annulment MUST be a separate
appended record that marks the recibo annulled, and every read of an annulled recibo MUST show it as
annulled with its number, reason, user and time.

#### Scenario: A recibo cannot be altered or deleted

- GIVEN an issued recibo
- WHEN an update or delete of the recibo or one of its lines is attempted, including by raw SQL
- THEN it MUST be refused

#### Scenario: An annulled recibo is shown as annulled

- GIVEN an annulled recibo
- WHEN it is read
- THEN it MUST be marked annulled and its lines MUST still be readable

### Requirement: The Recibo Is Labelled Non-Fiscal

Every recibo MUST carry a fixed legend stating that it is not a fiscal invoice. Electronic invoicing
is outside this system (§10). The stance that the recibo needs no fiscal numbering is **weak**
(Decision 10) and is to be confirmed with the agency's contador before go-live; it MUST NOT be
presented as legal fact.

#### Scenario: The legend is present on both kinds

- GIVEN a locatario and a propietario recibo
- WHEN each is read
- THEN each MUST carry the non-fiscal legend

---

## Capability: contract-account (MODIFIED)

Living spec: `openspec/specs/contract-account/spec.md`. Requirements not listed below are unchanged.

### ADDED Requirement: Payment and RecargoCharged Are Ledger Movements

The ledger MUST admit two further movement kinds:

- **`Payment`**: carries the period it settles, a negative amount, and is created only by collection.
- **`RecargoCharged`**: carries the same period and a positive amount, and is the stored form of the
  recargo at the instant it is charged. It is the moment the original cuenta-corriente decision named
  when it said recargo "becomes a stored movement when it is charged on a receipt".

Both are subject to "The Ledger Is Append-Only; a Correction Is a New Movement". The forward
reference "the payment movement itself is created by the collection change" stops being a forward
reference. The `RecargoCharged` and its `Payment` are always written in one transaction (see
`collection`); a ledger holding one without the other for a period is a defect.

#### Scenario: The new kinds are accepted and are append-only

- GIVEN an account
- WHEN a `Payment` and a `RecargoCharged` are recorded for a period
- THEN both MUST be accepted
- AND an update or delete of either, including by raw SQL, MUST be refused

#### Scenario: A reversal is a correction naming the original

- GIVEN a recorded `Payment`
- WHEN it is reversed
- THEN a `Correction` of opposite sign naming the `Payment` MUST be appended
- AND the `Payment` MUST remain unchanged

### RENAMED and MODIFIED Requirement: A Payment Settles the Oldest Unpaid Period → A Payment Settles the Period the Tenant Names, or the Oldest Unpaid One

(Previously: "A Payment Settles the Oldest Unpaid Period": the operator is never asked and cannot
override. Reason: CCyCN arts. 900–902 give the debtor the right to impute at payment, and the
agency's practice "el primero que adeuda" (§16) is the default that applies when the tenant does not
choose. Migration: the living Tests 14 and 15 are replaced by Tests 3 to 5 below; no other reference
changes.)

When a payment settles a period, that period MUST be the one the tenant named, provided it is payable:
no live payment settles it, and it is owed or in a month that has begun but not yet fallen due. When the tenant names none, it MUST be the
**oldest unpaid** period on the account or, when nothing is owed, the month in course. The
system MUST NOT refuse a named period merely because an older one is unpaid, and MUST NOT contain any
clause that forbids a choice or an override.

The oldest-unpaid default remains the account's own answer (`OldestUnpaidPeriod`), so every caller
gets the same default. The default makes the tenant pay the most expensive month first, because the
oldest period carries the most days of recargo. That is correct and is now only the default.

> **Open edge, weak.** Art. 900 forbids imputing to capital while interest is owed without the
> creditor's consent. Whether a named newer period may be paid while an older period's recargo is
> unpaid is for the agency's lawyer or contador. This requirement does not enforce it and does not
> forbid it.

The payment movement itself is created by `collection`; this capability owns the default rule and the
definition of "owed" (the period's movements do not net to zero).

#### Scenario: Three months behind, none named, the oldest is paid

- GIVEN an account with July, August and September all unpaid
- WHEN one period is paid and none is named
- THEN July MUST be the period settled
- AND August and September MUST remain unpaid, each still counting recargo from its own due date

#### Scenario: Three months behind, September named

- GIVEN the same account
- WHEN one period is paid and the tenant names September
- THEN September MUST be the period settled
- AND July and August MUST remain unpaid, each still counting recargo from its own due date

#### Scenario: The operator may choose

- GIVEN the same account
- WHEN the payment is recorded
- THEN the operator MUST be able to name a period
- AND no clause MUST forbid the choice

#### Scenario: The remaining periods keep their own clocks

- GIVEN the same account and a payment that settles September
- WHEN the amount owed is read afterwards
- THEN July and August MUST still project recargo from their own due dates

### MODIFIED Requirement: An Opening Balance Exists Only for a Contract That Predates the System

(Previously: "exactly one movement of kind `OpeningBalance`" per pre-existing account, with no
period, so the debt could never be settled by any payment. Reason: the opening debt MUST be payable,
one month at a time (Settled, Tomás 2026-10-13). Migration: living Tests 9 and 10 are replaced by
Tests 52 to 56 below.)

A contract that already existed when the system went live MUST have its debt entered as `OpeningBalance`
movements, entered by a person and never computed: **one per owed month**, each carrying **that
month's period** and **that month's real amount**. A month is an ordinary period from then on: it can
be named by a payment, it is eligible for the oldest-unpaid default, and it is paid in full or not at
all. A single opening figure settled whole, and an even split of a total, MUST NOT be accepted as the
way to load a debt, because an adjustment, a partial payment or a month's recargo makes months
unequal and a single figure could not be paid one month at a time.

A tenant who is up to date MUST be loaded as exactly one `OpeningBalance` of **zero** with no period,
so the account states that somebody asserted zero rather than that nobody looked (Decision 30). A
`OpeningBalance` with a non-zero amount and no period MUST be refused. Within one account there MUST be
at most one `OpeningBalance` per period and at most one period-less zero. A period carrying an
`OpeningBalance` MUST NOT also receive a `RentAccrual`.

The kind MUST stay distinguishable from every other kind, so no reader mistakes a typed figure for a
derived one. A period held by an `OpeningBalance` projects **no** recargo (it has no accrual from
which to measure; the typed figure is what the agency's spreadsheet says is owed, mora included,
Decision 31). Which months each tenant owes is a data-loading question (default, Decision 18), not a
modelling one.

A contract **created in the system** MUST NOT have any. Its account begins empty and its first
movement is its first rent accrual. An account MUST NOT be refused for having no opening balance.

#### Scenario: A contract signed after go-live opens with an empty account

- GIVEN a contract created in the system
- WHEN its account is created
- THEN it MUST be accepted with no movements at all
- AND its balance MUST be zero

#### Scenario: A pre-existing debt is loaded month by month

- GIVEN a contract that existed before go-live, owing 150,000 for May, 160,000 for June and 170,000 for July
- WHEN its account is opened
- THEN three `OpeningBalance` movements MUST be recorded, one per month, each with its period and amount
- AND the ledger balance MUST be 480,000

#### Scenario: One month of an old debt can be paid

- GIVEN the account above
- WHEN a payment names June
- THEN June MUST be settled for 160,000
- AND May and July MUST remain unpaid

#### Scenario: Old debt is paid oldest first by default

- GIVEN the same account
- WHEN a payment names no period
- THEN May MUST be the period settled

#### Scenario: Zero is a valid opening balance for a pre-existing contract

- GIVEN a pre-existing contract whose tenant is up to date
- WHEN its account is opened
- THEN exactly one `OpeningBalance` of zero with no period MUST be recorded

#### Scenario: A non-zero opening balance without a period is refused

- GIVEN a pre-existing contract
- WHEN an `OpeningBalance` of 150,000 with no period is attempted
- THEN it MUST be refused

#### Scenario: At most one opening balance per period

- GIVEN an account that already carries an `OpeningBalance` for June
- WHEN a second `OpeningBalance` for June is attempted
- THEN it MUST be refused

#### Scenario: A month loaded as opening balance receives no rent accrual

- GIVEN an account carrying an `OpeningBalance` for July
- WHEN a rent accrual for July is attempted
- THEN it MUST be refused

#### Scenario: An opening-balance month projects no recargo

- GIVEN a month loaded as `OpeningBalance` and long overdue
- WHEN the amount owed is read
- THEN no recargo MUST be projected for it
- AND a payment of that month MUST write no `RecargoCharged`

#### Scenario: An opening balance is never silently recomputed

- GIVEN an account with typed opening balances
- WHEN any later movement is added
- THEN each opening balance MUST remain exactly as it was entered

### ADDED Requirement: A Honorarios Contractuales Instalment Is Payable With Its Rent Period

A `ContractualFeeInstalment` movement MUST carry a period: instalment *k* (1 to the plan's count)
attaches to the *k*-th rent period from the contract's start. It is therefore settled by the same
payment, on the same recibo, under the same period rule as that period's rent, and it joins the
period's amount for recargo purposes. It MUST NOT be settled apart from the rent (default, Decision
17: whether the agency ever lets one be paid off-schedule is Open Question 4).

For a contract that predates the system, instalments for periods due on or before the cutover date MUST NOT be raised
(Decisions 32 and 41): they belong to the opening balance. The instalment for each period MUST be raised once, together with
that period's rent accrual, and never twice.

An instalment is the tenant's concept only: it appears in the account balance (which covers rent,
instalments and recargo) and on the locatario recibo, never on the propietario recibo.

#### Scenario: Instalment k rides with the k-th rent period

- GIVEN a contract with a plan of 3 instalments of 30,000, starting in January
- WHEN the January, February and March periods accrue
- THEN instalments 1, 2 and 3 MUST be raised for those periods respectively
- AND no instalment MUST be raised for April

#### Scenario: The instalment is paid with the rent

- GIVEN a period carrying a rent of 500,000 and an instalment of 30,000
- WHEN the period is paid
- THEN the payment MUST settle 530,000 plus recargo
- AND the period MUST net to zero

#### Scenario: An instalment cannot be paid alone

- GIVEN a period with a rent and an instalment
- WHEN a payment for the instalment alone is attempted
- THEN it MUST be refused as a partial amount

#### Scenario: Earlier instalments of a pre-existing contract are not raised

- GIVEN a contract that predates go-live with 6 instalments, the first two periods due on or before
  its cutover date
- WHEN accruals run after the cutover
- THEN instalments 1 and 2 MUST NOT be raised
- AND instalments 3 to 6 MUST be raised with their periods

#### Scenario: An instalment is never raised twice

- GIVEN an instalment already raised for a period
- WHEN it is raised again
- THEN it MUST be refused

### ADDED Requirement: A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date

The **cutover date** is stored on the contract. A contract loaded as one that predates the system
carries it from the moment it is loaded; a contract created in the system carries none. It MUST NOT be
derived from the account's movements: a pre-existing contract read before its opening balances are
typed would then have no cutover, and because reading an account (and the mora worklist, which reads
every account) materialises accruals onto an append-only ledger, its whole history would be written
permanently. The cutover date MUST NOT change once the account holds any movement (Decision 41).

For a contract that predates the system, the system MUST NOT raise a `RentAccrual` or a
`ContractualFeeInstalment` for any period whose due date falls on or before the cutover date: those
months are carried by the opening balance, month by month, or are not owed. This MUST hold however an
accrual is triggered (reading the account, the mora worklist, or a payment), including before any
opening balance has been entered. A period due after the cutover date accrues
as for any contract. A contract created in the system has no cutover date and is unaffected.

Instalment numbering MUST NOT shift: instalment *k* still belongs to the *k*-th rent period from the
contract's start, so the instalments skipped by this rule are not renumbered onto later periods
(Decisions 32 and 41).

#### Scenario: A loaded pre-existing contract accrues no history

- GIVEN a contract signed years before go-live, whose cutover date is 5 October
- WHEN its account is read, which triggers accrual
- THEN no `RentAccrual` MUST be written for any period due on or before 5 October
- AND the account's balance MUST equal its opening balances

#### Scenario: A period due after the cutover date accrues normally

- GIVEN the same account and a period due on 10 October
- WHEN the account is read on 11 October
- THEN a `RentAccrual` MUST be written for that period

#### Scenario: No instalment is raised for a period before the cutover

- GIVEN the same contract carrying a plan of 6 instalments whose first periods are due before 5 October
- WHEN accruals run
- THEN no instalment MUST be raised for those periods
- AND each later instalment MUST attach to its own *k*-th period

#### Scenario: A contract created in the system is unaffected

- GIVEN a contract created in the system
- WHEN its first period falls due
- THEN its `RentAccrual` MUST be written

#### Scenario: A pre-existing contract read before its opening balances are typed accrues nothing

- GIVEN a contract loaded as predating the system with a cutover date of 5 October, and no opening
  balance entered yet
- WHEN the mora worklist reads every account on 6 October
- THEN no `RentAccrual` and no instalment MUST be written for any period due on or before 5 October

### Unchanged requirements that this change relies on

- **Only Total Payments** is unchanged and is now enforced by `collection`.
- **Two Figures, Named Apart** is unchanged: the recibo total and the ledger figure are a third
  pairing that `receipt` names apart.
- **Recargo Is Derived, Never Accrued as Daily Movements** is unchanged. `RecargoCharged` is the
  *charged* case the requirement already allows: the passage of time alone still writes nothing.
- **The Balance Names What It Covers** is unchanged: tasa municipal, seguro and Otros Conceptos remain
  absent from the account.
- **The Account Outlives Its Contract** is unchanged: a payment lands after the contract ended.

---

## Capability: lease-contract (MODIFIED)

Living spec: `openspec/specs/lease-contract/spec.md`. Requirements not listed below are unchanged.

### ADDED Requirement: A Contract Carries a Honorarios Contractuales Plan

`Contract` MUST be able to store a honorarios contractuales plan: a **fixed amount** and an
**instalment count from 1 to 6**. Amount and count MUST be stored together or both absent. Paying in
full is simply a count of 1, not a separate case. The amount is a fixed sum, **never a percentage**,
and MUST NOT change when the rent adjusts. A contract with no plan MUST be storable (most have none).

The amount MUST be a positive whole peso and the count MUST be a whole number from 1 to 6. A plan MUST
NOT be changed after any of its instalments has been written to the account (Decision 32).

> **Not to be confused with the administration percentage.** The existing requirement "Honorarios
> Percentage Is Stored, Nullable" is the owner's monthly percentage. This one is the tenant's one-off
> fee. Every future mention MUST say which.

#### Scenario: A plan is stored

- GIVEN a contract being created with a plan of 3 instalments of 30,000
- WHEN it is saved
- THEN the stored amount MUST be 30,000 and the count 3

#### Scenario: A contract with no plan is valid

- GIVEN a contract created without a plan
- WHEN it is saved
- THEN it MUST be accepted

#### Scenario: A count outside 1 to 6 is refused

- GIVEN a plan with a count of 0, or of 7
- WHEN it is supplied
- THEN it MUST be refused

#### Scenario: An amount that is not positive is refused

- GIVEN a plan with an amount of 0 or a negative number
- WHEN it is supplied
- THEN it MUST be refused

#### Scenario: An amount without a count is refused

- GIVEN a plan with an amount and no count, or a count and no amount
- WHEN it is supplied
- THEN it MUST be refused

#### Scenario: The plan does not follow the rent

- GIVEN a contract with a plan of 30,000 whose rent is adjusted by 12%
- WHEN the plan is read
- THEN the amount MUST still be 30,000

#### Scenario: Existing contracts keep working

- GIVEN contracts that existed before this change
- WHEN the migration adding the plan runs
- THEN every one of them MUST carry no plan
- AND no existing contract row MUST be otherwise altered

#### Scenario: A plan cannot be changed once an instalment is written

- GIVEN a contract with an instalment already written
- WHEN a change of its plan is attempted
- THEN it MUST be refused

### MODIFIED Note: Honorarios Percentage Is Stored, Nullable

The requirement text and scenarios are unchanged. Only the note stating that honorarios contractuales
are "not modelled anywhere yet; it belongs to the collection change" MUST be updated to say they are
modelled by the requirement above, and that the administration rate is now read by `receipt` to compute
the propietario recibo's line. The OPEN note on where the rate belongs (owner or contract) stays open:
this change reads the rate where it lives today.

---

## Capability: access-control (MODIFIED)

Living spec: `openspec/specs/access-control/spec.md`. Requirements not listed below are unchanged.

### ADDED Requirement: The Collection Tables Are Granted Deliberately, and the Counter Is a Named Departure

Under "Every New Table Ships With Its GRANTs in the Same Migration", the migration that creates the
collection (payment event), recibo, recibo-line, annulment and numbering-counter tables MUST grant, in
the same migration, to both `inmobiliaria_admin` and `inmobiliaria_empleado` exactly:

- collections, recibos, recibo lines, annulment records: `SELECT` and `INSERT`, and nothing else.
  Neither role MUST hold `UPDATE` or `DELETE` on them (Decision 40).
- the numbering counter: `SELECT`, and `UPDATE` of the column that advances the counter, and not
  `INSERT` or `DELETE`. This is a **deliberate departure** from the insert-only pattern the other
  collection tables follow. The counter is the one row that must change, and both roles take
  payments. Its single row exists from the migration.
- the privilege to set the **starting number** is held by `inmobiliaria_admin` only
  (Decision 39). `inmobiliaria_empleado` MUST NOT be able to set it. This restriction is enforced by
  the database (a separate column-level grant or function), not by the UI.
- the annulment records are insertable by both roles, yet the time and role limits on voiding
  (Decision 45) MUST still be enforced by the database on that insert, because a grant cannot express
  them.
- the existing movements table keeps its insert-only grants; the new movement kinds need no new privilege.

**A named limit.** Under a `GRANT` on the counter's advancing column the database cannot distinguish an
increment made inside an issuance from any other `UPDATE` of that column. The protection against a tampered counter is
therefore not the grant but the unique constraint on the recibo number: a counter set back would
cause the next issuance to be refused, not to issue a duplicate. This is stated so no reader assumes
database enforcement that does not exist.

#### Scenario: Both roles can read and write exactly what they should

- GIVEN the migration has run
- WHEN each application role connects
- THEN each MUST be able to read the new tables, insert collections, recibos, lines and annulments, and advance the counter
- AND neither MUST be able to update or delete a collection, a recibo, a line or an annulment, or to insert into or delete from the counter
- AND only `inmobiliaria_admin` MUST be able to set the counter's starting number

#### Scenario: A counter set back cannot produce a duplicate

- GIVEN issued recibos up to 24270
- WHEN a role sets the counter back to 24200 with a direct `UPDATE` and a payment is then committed
- THEN the commit MUST be refused by the unique constraint
- AND no recibo with a duplicate number MUST exist

#### Scenario: The migration leaves no table unreachable

- GIVEN the migration has run
- WHEN either role queries any new table
- THEN it MUST not fail with a permission error for an operation this requirement grants

---

## Out of Scope

| Not here | Where it belongs |
|---|---|
| The liquidation to the owner: paying out, splitting among several lessors, where the administration rate should live | `liquidacion-al-dueno` |
| PDF rendering and printing of the recibo | `recibo-pdf` (open decision `pdf-library`, not resolved here) |
| Notifications to tenants | `notificaciones` (open decision `scheduler-mechanism`) |
| Payment plan after rescission, and paying an account whose recargo is frozen by a novación | `plan-de-pagos` |
| Electronic invoicing (ARCA) | A separate project |
| The optional rounding option the owner asked for | A later, additive change |
| Any collection screen | A later UI pass |
| A solvency check on `Contract.End()` | Never: Decision 14 |

## Parameters Left to Design

The spec states the observable requirement. These were design's to fix; the design's answers are
recorded here, and only item 1 remains pending verification:

1. Where the counter lives, and the effect of a transaction-mode pooler on its lock. Design answer: a
   dedicated one-row table advanced by a single `UPDATE ... RETURNING`; the app connects in session
   mode (design D5, D6). **Still pending verification against the real Supabase pooler (design
   PV1)**; not decided-and-verified.
2. Pre-existing contract's opening months: `OpeningBalance` entries carrying a period, with the
   ledger constraints widened to match (design D18).
3. The receipt-line catalog: a code enum mirrored by a `CHECK` list, not a table (design D11).
4. The instalment's period: set at creation, written with that period's rent (design D19).
5. "One live payment per period": an attempt number with a unique index and an insert trigger
   (design D8).

Decided by the 2026-10-14 amendment and no longer open: the back-date bound and who may back-date
(Decision 38), who may set the starting number (Decision 39), and who may void and until when
(Decision 45).

## Tests

Numbered, each naming the project it belongs in. `Inmobiliaria.Domain.Tests` is the default, because
`Inmobiliaria.Desktop` is excluded from the Linux CI slice and nothing is tested there.
`Inmobiliaria.Infrastructure.Tests` is used where a real Postgres is required (`postgres:17.6` via
Testcontainers, Docker running; a skipped test is an unrun test). Tests 3, 4 and 5 replace the living
`contract-account` Tests 14 and 15; Tests 52 to 56 replace living Tests 9 and 10.

**Collection**

1. **A partial amount is refused**, and writes no movement, recibo or number. *(Domain.Tests)*
2. **An excess amount is refused**, and writes nothing. *(Domain.Tests)*
3. **A named period is settled while an older one is owed.** With July, August and September unpaid,
   naming September settles September and leaves July and August unpaid. *(Domain.Tests)*
4. **With no period named, the oldest is settled.** The same account settles July. *(Domain.Tests)*
5. **The remaining periods keep their own clocks.** After September is settled, July and August still
   project recargo from their own due dates. *(Domain.Tests)*
6. **A period already settled, a month that has not begun, and a period not on the contract are each
   refused**, with nothing written. A period not on the contract includes a calendar month the
   contract never charges (Decision 43). *(Domain.Tests)*
7. **A payment against an account with a frozen recargo is refused.** *(Domain.Tests)*
8. **The recargo charged equals the recargo projected as of the received date**, truncated to a whole
   peso. *(Domain.Tests)*
9. **A committed payment leaves the period at zero**, and the amount owed read the next day projects
   no recargo for it (no double recargo). *(Domain.Tests)*
10. **A zero recargo writes no `RecargoCharged` and prints no recargo line.** *(Domain.Tests)*
11. **Received on defaults to today.** With no received date supplied, the preview resolves it to the
    clock's today and records which day that was; the request offers no way to supply a recorded-at.
    *(Domain.Tests)*
12. **A future received date is refused**, and so is one more than 7 calendar days back; 7 days back
    is accepted for either role (Decision 38). *(Domain.Tests)*
13. **A back-dated received date charges the recargo of that date**, lower than today's, and the
    recibo shows that date. *(Domain.Tests)*
14. **Recorded-at and recorded-by are set by the database**: a supplied recorded-at is ignored at
    creation, and neither can be updated, including by raw SQL. A collection inserted directly with a
    received date beyond the bound is refused. *(Infrastructure.Tests)*
15. **Atomicity, recargo then failure.** A failure forced after the `RecargoCharged` write leaves no
    `RecargoCharged`, no `Payment`, no recibo and an unchanged counter. *(Infrastructure.Tests)*
16. **Atomicity, recibo failure consumes no number.** A failure while writing the propietario recibo
    leaves nothing, and the next payment takes the number the failed one would have. *(Infrastructure.Tests)*
17. **Two sessions pay the same period at once**: exactly one succeeds, the other is refused with
    nothing written. *(Infrastructure.Tests)*
18. **The preview writes nothing.** Row counts and the counter value are identical before and after.
    *(Infrastructure.Tests)*
19. **The preview shows the period, received date, previous amount owed, each line of both recibos,
    both recibo totals, the ledger figure and the resulting balance.** *(Domain.Tests)*
20. **A commit that differs from its preview is refused.** Another payment between preview and
    commit, and the day turning over, each force a fresh preview. *(Domain.Tests)*
21. **A fractional typed amount is truncated toward zero and shown truncated.** *(Domain.Tests)*
22. **Voiding appends corrections.** The `Payment` and the `RecargoCharged` are each reversed by a
    `Correction` naming them; the period is unpaid again and projects recargo again. *(Domain.Tests)*
23. **Voiding either recibo annuls both.** *(Domain.Tests)*
24. **A voiding needs a reason**, and the voiding plan refuses a recibo already annulled. (User and
    time are the database's: Test 51.) *(Domain.Tests)*
25. **A voided number stays and is never reused.** The next payment takes the next numbers; the
    voided pair stays readable, marked annulled. *(Infrastructure.Tests)*
26. **A voided period accepts a new payment**, while two live payments for one period are impossible.
    *(Infrastructure.Tests)*
27. **A balance read for a date before a voiding still shows the payment.** A voiding of an early
    payment before its due date is dated on the due date, never before the payment it reverses
    (Decision 44). *(Domain.Tests)*

**Receipt**

28. **Each payment issues exactly one locatario and one propietario recibo**, stating the same period
    and received date. *(Domain.Tests)*
29. **The pair's numbers are consecutive**, the locatario recibo taking the lower. *(Domain.Tests)*
30. **Two payments are four recibos with four consecutive numbers.** *(Domain.Tests)*
31. **Gapless under concurrency.** Several sessions issue at once; the numbers form one unbroken run
    and each pair is adjacent. *(Infrastructure.Tests)*
32. **The database refuses a duplicate recibo number.** *(Infrastructure.Tests)*
33. **The first issuance takes the cutover number.** With a starting number of 31001 and no recibo,
    an issuance against the counter (its `UPDATE ... RETURNING`, run raw) returns 31001 for the
    locatario and 31002 for the propietario; a starting number corrected to 31100 before use is the
    one returned. *(Infrastructure.Tests)*
34. **The cutover number is refused after the first recibo.** *(Infrastructure.Tests)*
35. **No number is issued before cutover is set**: an issuance against a counter with no starting
    number, run raw, returns no row and leaves the counter unchanged; a zero or negative starting
    number is refused. *(Infrastructure.Tests)*
36. **The locatario recibo carries rent, instalment and recargo; the propietario recibo carries rent
    and recargo but no instalment.** *(Domain.Tests)*
37. **A derived line cannot be overridden by the operator.** *(Domain.Tests)*
38. **A negative Otros Conceptos is accepted**, appears on the recibo and never on the ledger.
    *(Domain.Tests)*
39. **A negative seguro is accepted**, on its own line, on both recibos. *(Domain.Tests)*
40. **A negative tasa municipal is refused.** *(Domain.Tests)*
41. **Tasa municipal, seguro and Otros Conceptos create no ledger movement** and leave the balance
    unchanged. *(Domain.Tests)*
42. **The propietario recibo carries a negative honorarios de administración line** computed from the
    contract's rate on rent plus recargo, truncated; the locatario recibo has none. *(Domain.Tests)*
43. **The stored honorarios de administración line is never recomputed.** Reading an issued recibo
    against a contract built with another rate leaves the line as issued (Decision 46).
    *(Domain.Tests)*
44. **Several lessors appear on one propietario recibo.** *(Domain.Tests)*
45. **A contract whose administration rate was never entered cannot be committed**, and the preview
    says so.
    *(Domain.Tests)*
46. **No honorarios de administración movement is ever written to the account.** *(Domain.Tests)*
47. **The recibo total and the ledger figure differ by the typed lines and are named apart.**
    *(Domain.Tests)*
48. **A recibo and its lines cannot be updated or deleted**, including by raw SQL. *(Infrastructure.Tests)*
49. **Both kinds of recibo carry the non-fiscal legend.** *(Domain.Tests)*
50. **A recibo's total equals the sum of its signed lines**, and no monetary member is `double` or
    `float`. *(Domain.Tests)*
51. **An annulled recibo is read as annulled**, with number, reason, user and time; the user and time
    are stamped by the database (`current_user`, `now()`), and a supplied value is ignored.
    *(Infrastructure.Tests)*

**contract-account**

52. **A pre-existing debt is loaded month by month**, three `OpeningBalance` movements with periods
    summing to the ledger balance. *(Domain.Tests)*
53. **One month of an old debt can be paid**, by name, leaving the others unpaid; by default the
    oldest is paid. *(Domain.Tests)*
54. **A non-zero `OpeningBalance` with no period is refused**; a zero with no period is accepted; a
    second `OpeningBalance` for one period is refused; a period holding one cannot also take a
    `RentAccrual`. *(Domain.Tests)*
55. **An opening-balance month projects no recargo**, and paying it writes no `RecargoCharged`.
    *(Domain.Tests)*
56. **An opening balance never changes** after later movements are added. *(Domain.Tests)*
57. **The database accepts `Payment` and `RecargoCharged`** and refuses to update or delete them.
    *(Infrastructure.Tests)*
58. **An instalment *k* attaches to the *k*-th rent period**, is paid with the rent, cannot be paid
    alone and is never raised twice. *(Domain.Tests)*
59. **Instalments for periods due on or before the cutover date are not raised** on a pre-existing
    contract, including a period due exactly on it.
    *(Domain.Tests)*

**lease-contract**

60. **A plan of amount and count is stored; no plan is valid.** *(Domain.Tests)*
61. **A count of 0 or 7, a non-positive amount, and an amount without a count (or the reverse) are
    refused.** *(Domain.Tests)*
62. **The plan amount does not move when the rent adjusts.** *(Domain.Tests)*
63. **A plan cannot be changed once an instalment has been written.** *(Domain.Tests)*
64. **Existing contracts carry no plan after the migration**, and no other column of theirs changes.
    *(Infrastructure.Tests)*

**access-control**

65. **Every new table ships with its GRANTs.** After the migration, each application role performs
    exactly its intended operations on the new tables: `SELECT` and `INSERT` on collections, recibos,
    lines and annulments, `SELECT` and advancing the counter on the counter, and no `UPDATE` or
    `DELETE` on the others. Only the Admin role may set the counter's starting number; the Empleado
    role is refused. *(Infrastructure.Tests)*
66. **A counter set back by a direct `UPDATE` cannot produce a duplicate number**: the next issuance
    against the counter, run raw, followed by its recibo insert, is refused by the unique constraint.
    *(Infrastructure.Tests)*
67. **The model matches the migration.** `dotnet ef migrations has-pending-model-changes` reports
    nothing pending for each slice that changes the model. (A command check rather than an xUnit
    test; it needs no database.)

**Added 2026-10-14 (Decisions 22 and 37)**

68. **The current month is paid before its due date**: settled with no recargo, its rent and its
    instalment (when it carries one) brought onto the account by the payment, and the later accrual
    raises neither again. The `Payment` is dated on the due date, not the received date
    (Decision 47). *(Domain.Tests)*
69. **A contract with a 0% rate is collected**, the preview warns, and the honorarios de
    administración line is zero. *(Domain.Tests)*
70. **An early payment racing the lazy accrual leaves one charge.** An early payment and the lazy
    accrual of the same period, committed concurrently from two real sessions, leave exactly one
    `RentAccrual` and at most one `ContractualFeeInstalment` for the period; the loser writes
    nothing (Decision 42). *(Infrastructure.Tests)*
71. **A pre-existing contract accrues nothing due on or before its cutover date.** Domain: the
    schedule, given a cutover date, yields no period due on or before it, for rent and instalments,
    and does not renumber instalments. Infrastructure: materialising a loaded pre-existing account
    writes no `RentAccrual` or instalment before the cutover and writes the later ones
    (Decision 41). *(Domain.Tests and Infrastructure.Tests)*
72. **The voiding window is enforced by the database.** An Empleado voiding a pair on day 7
    succeeds; on day 8 is refused; an Admin voiding on day 8 and on day 30 succeeds; anyone voiding
    on day 31 is refused; each refusal writes nothing, including when attempted by raw SQL
    (Decision 45). *(Infrastructure.Tests)*

**Added 2026-10-14, review pass (Decisions 37, 41, 48 and 49)**

73. **A voided early payment can be paid again before its due date.** A period due on the 10th, paid
    on the 2nd and voided on the 3rd, is payable on the 4th; the new payment writes no second
    `RentAccrual` or instalment, charges no recargo, and leaves the period at zero (Decision 37).
    *(Domain.Tests)*
74. **An early-written rent is final.** A rent adjustment taking effect on or before an early-paid
    period's due date and confirmed afterwards leaves that period's `RentAccrual` as written and
    generates no movement for it; the next period accrues at the adjusted canon (Decision 48).
    *(Domain.Tests)*
75. **An opening-balance month prints a Saldo Deudor line.** Paying a month loaded as an
    `OpeningBalance` of 160,000 under a rate of 8% puts a Saldo Deudor line of 160,000 on both recibos,
    no rent or recargo line, and an honorarios de administración line of -12,800 on the propietario
    recibo (Decision 49, a default). *(Domain.Tests)*
76. **A pre-existing contract read before its opening balances are typed accrues nothing.** A
    contract stored with a cutover date and no opening balance yet, read by the mora worklist, gains no
    `RentAccrual` or instalment for any period due on or before that date (Decision 41).
    *(Infrastructure.Tests)*
