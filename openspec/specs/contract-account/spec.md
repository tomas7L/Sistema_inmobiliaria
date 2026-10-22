# Contract Account Specification

## Purpose

A running account per contract: an append-only sequence of movements whose sum answers **what is
owed, and why**. It accrues debts and reports balances. It does not take money, issue receipts, or
decide what a receipt says — collection does those, and calls this capability to find out what to
charge.

## Requirements

### Requirement: An Account Belongs to Exactly One Contract

Each account MUST belong to exactly one `Contract`, and each contract MUST have exactly one account.
A tenant holding several contracts MUST have one account per contract, never one combined account.
A view that shows "what this tenant owes" MUST be a sum over that tenant's contracts, computed for
display, and MUST NOT be a stored account of its own.

Rent is owed under a contract, at a canon that contract defines, on a due date that contract sets.
The recargo clock starts from that contract's due date, so two contracts late by different amounts
cannot share one clock.

#### Scenario: One account per contract

- GIVEN a tenant holds two contracts
- WHEN their accounts are read
- THEN there MUST be two accounts, one per contract
- AND each MUST carry its own balance and its own due dates

#### Scenario: A tenant total is computed, never stored

- GIVEN the same tenant with two contracts
- WHEN "what does this tenant owe" is asked
- THEN the answer MUST be the sum over their contracts' accounts
- AND no stored account keyed by tenant MUST exist

### Requirement: The Ledger Is Append-Only; a Correction Is a New Movement

A movement, once recorded, MUST NOT be updated or deleted. A correction MUST be a new movement
carrying the opposite sign and a reference to the movement it corrects.

This mirrors `rent-adjustment`'s existing rule that a confirmed adjustment is never rewritten, and
it is what lets the account be read truthfully for any past date.

#### Scenario: A recorded movement cannot be altered

- GIVEN an account with a recorded movement
- WHEN an update or a delete is attempted against that movement, including by raw SQL
- THEN it MUST be refused

#### Scenario: A mistake is corrected by appending

- GIVEN a movement recorded for the wrong amount
- WHEN it is corrected
- THEN a new movement MUST be appended carrying the difference and naming the movement it corrects
- AND the original movement MUST remain readable and unchanged

### Requirement: Two Figures, Named Apart — the Ledger Balance and the Amount Owed

The system MUST distinguish two figures and MUST NOT use one name for both.

- The **ledger balance** on a date MUST equal the sum of the account's movements dated on or before
  it. It is what has been written.
- The **amount owed** as of a date MUST equal that ledger balance plus the recargo projected to that
  same date. It is what the tenant actually owes.

They differ because recargo is deliberately not a movement until it is frozen or charged. A positive
figure means the tenant owes; a negative one means they are in credit.

Any presentation MUST say which of the two it is showing. The figure an operator acts on is almost
always the **amount owed**; the ledger balance is what reconciles against the movements.

#### Scenario: The ledger balance is the sum of movements

- GIVEN an account with an opening balance of 150,000 and a rent accrual of 500,000, neither overdue
- WHEN its ledger balance is read
- THEN it MUST be 650,000

#### Scenario: The amount owed adds the projected recargo

- GIVEN the same account, with the 500,000 accrual 10 days overdue
- WHEN the amount owed is read for today
- THEN it MUST be 650,000 plus the recargo projected over those 10 days
- AND the ledger balance MUST still read 650,000, unchanged

#### Scenario: A historical balance reads correctly

- GIVEN an account whose movements span three months
- WHEN its balance is asked for a date two months ago
- THEN only movements dated on or before that date MUST be counted

### Requirement: An Opening Balance Exists Only for a Contract That Predates the System

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

### Requirement: A Period Accrues at the Canon in Force, and Never Re-Prices

A rent accrual MUST record the canon in force for the period it covers, and MUST NOT change when a
later adjustment is confirmed — including an adjustment confirmed late, whose due date fell inside an
already-accrued period.

`rent-adjustment` already requires that a late adjustment generate no retroactive charge. This
requirement is that rule seen from the ledger's side, and it is what makes a stored balance correct
rather than a cache that can go stale.

#### Scenario: A late adjustment leaves accrued periods untouched

- GIVEN periods for July, August and September accrued at a canon of 500,000
- AND an adjustment that fell due on 1 July is confirmed on 1 October
- WHEN the account is read
- THEN those three accruals MUST still be 500,000 each
- AND no new movement MUST be generated for them

#### Scenario: The next period uses the new canon

- GIVEN the same late-confirmed adjustment
- WHEN the next period accrues
- THEN it MUST use the adjusted canon

### Requirement: Only Total Payments

A payment MUST settle a period in full or not at all. The system MUST NOT represent a partially paid
period, and MUST NOT allocate a fraction of a payment across several periods.

#### Scenario: A partial amount is refused

- GIVEN a period owing 500,000
- WHEN a payment of 300,000 is offered against it
- THEN it MUST be refused
- AND no movement MUST be recorded

### Requirement: A Payment Settles the Period the Tenant Names, or the Oldest Unpaid One

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

> **Resolved edge.** Art. 900 forbids imputing to capital while interest is owed without the
> creditor's consent. The agency's contador confirmed (2026-10-16) that when the tenant exercises
> the right to impute a payment to a specific period, and pays that period in full, the system has
> no legal obligation to collect older periods' recargo or interest first, and the owner cannot
> refuse the payment because older periods are unpaid. This requirement therefore does not enforce
> recargo-first.

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

### Requirement: Recargo Is Derived, Never Accrued as Daily Movements

Recargo MUST be computed when it is asked for, from the amount owed, the obligation's terms, and the
date of the question. The passage of time alone MUST NOT create any movement.

It is 2% per day, simple, from the day of mora, **with no cap**. The system MUST NOT cap it: the
limit is legal — the agency refers the matter to lawyers at roughly three months — and a cap in code
would quietly contradict the contract.

A contract three months late would otherwise carry ninety rows that say nothing.

#### Scenario: Time alone writes nothing

- GIVEN an account with an unpaid period, read on three different days
- WHEN each read happens
- THEN the recargo reported MUST grow with each day
- AND the number of movements on the account MUST be identical on all three days

**Why no cap, and what replaces one.** A ceiling in code would compute something different from
what the two parties signed, and the day a debt reaches a lawyer the system's figure would not match
the paper's. The real brake is not arithmetic, it is acting sooner: cláusula QUINTA already allows
the contract to be resolved after two unpaid months, and the agency refers the matter at roughly
three. So this capability does not slow the number down — it makes the situation visible early
enough that nobody reaches 200 days by accident. See "Accounts in Mora Are Queryable" below.

#### Scenario: The surcharge is not capped

- GIVEN a period unpaid for 200 days
- WHEN its recargo is computed
- THEN it MUST be the full simple 2% per day over those 200 days, with no ceiling applied

### Requirement: Recargo Reads Its Terms From the Obligation It Measures

The rate and the grace period used to compute recargo MUST come from the obligation being measured,
never from a constant in code. A lease supplies its own due day and rate; a future payment plan
supplies whatever its parties signed.

This is the pattern the project already follows for the adjustment clause, the honorarios percentage
and the rent split: what the paper says, the record carries.

#### Scenario: Two contracts with different due days

- GIVEN two contracts whose due days differ
- WHEN recargo is computed for an equally overdue period on each
- THEN each MUST count its days of mora from its own contract's due day

#### Scenario: No rate constant exists

- GIVEN the implemented calculation
- WHEN it is inspected
- THEN the 2% rate and the grace period MUST be read from the obligation
- AND no literal rate MUST appear in the calculation itself

### Requirement: Accounts in Mora Are Queryable, and the List Is Reminder-Ready

The accounts currently in mora MUST be queryable in a form a reminder can consume without any
knowledge of this capability's internals. For each one it MUST expose **how many days overdue**, the
**amount owed**, and the **recargo accrued so far**.

A reminder can then say *"Abregu is 38 days overdue, owing 500,000 plus 380,000 in recargo"* rather
than firing a blind monthly alert. This capability owes the notifications change that query; it
sends nothing and schedules nothing.

The query exists because of how this surcharge behaves. Two percent per day, simple and uncapped,
doubles a debt in fifty days. Nobody intends to let that happen, and nobody notices it either if the
only way to find out is to open each account one at a time. Making the list cheap to ask for is what
turns an uncapped rule into a safe one.

**Delivering the reminder is the notifications change, not this one**, and it also depends on the
open `scheduler-mechanism` decision in `openspec/config.yaml`, because a closed desktop application
sends nothing.

#### Scenario: The worklist names days, amount and surcharge

- GIVEN three accounts overdue by 5, 38 and 200 days
- WHEN the mora worklist is queried
- THEN all three MUST appear
- AND each MUST carry its days overdue, its amount owed and its recargo accrued so far

#### Scenario: An account up to date does not appear

- GIVEN an account with no unpaid period
- WHEN the mora worklist is queried
- THEN that account MUST NOT appear

#### Scenario: A frozen account reports its frozen figure

- GIVEN an account whose recargo was frozen by a novación
- WHEN the mora worklist is queried
- THEN its recargo MUST be the frozen amount, not a figure that kept growing

### Requirement: A Novación Freezes the Recargo on a Dated Movement

When a contract is rescinded and both parties sign a payment plan, the original obligation is
**extinguished and replaced**. The recargo MUST stop on the signing date, and the amount accrued up
to that date MUST be recorded as a movement carrying that date.

It MUST NOT be modelled as a boolean on the contract or the account. A flag cannot answer "frozen
since when", and the date is the whole of the legal effect.

#### Scenario: The surcharge stops on the signing date

- GIVEN an account accruing recargo on an unpaid balance
- AND a payment plan signed on 2026-11-15
- WHEN the account is read on 2026-12-20
- THEN the recargo MUST be exactly what had accrued up to 2026-11-15
- AND it MUST NOT have grown in the intervening days

#### Scenario: The frozen figure is a dated movement

- GIVEN the same signing
- WHEN the account's movements are read
- THEN a movement MUST exist carrying the frozen amount and the signing date
- AND no boolean flag MUST carry this state

#### Scenario: An earlier date still reads correctly

- GIVEN the same account
- WHEN its balance is asked for a date before the signing
- THEN the recargo as of that earlier date MUST be reported, unaffected by the later freeze

### Requirement: The Account Outlives Its Contract

An account MUST keep accepting payments after its contract reaches `Ended`, for as long as a balance
remains. Ending a contract MUST NOT close, freeze or archive its account, and MUST NOT be refused
because money is owed.

A contract that ends owing nothing simply has an account at zero with nothing further to accrue.
Ending is not what closes an account; a zero balance with no further accrual is.

A system that could not represent a rescission with a debt would have to state something false — a
contract reading `Active` for a tenant who has already moved out.

#### Scenario: A contract is rescinded owing money

- GIVEN a contract whose account owes 300,000
- WHEN the contract is ended with reason "mutual agreement"
- THEN it MUST succeed
- AND the account MUST remain open with its balance intact

#### Scenario: Payments land after the contract ended

- GIVEN the rescinded contract above
- WHEN a payment is recorded against its account
- THEN it MUST be accepted
- AND the balance MUST decrease accordingly

#### Scenario: No rent accrues after the contract ended

- GIVEN the same rescinded contract
- WHEN a period after the end date would otherwise come due
- THEN no rent accrual MUST be recorded

### Requirement: Whole Pesos, Truncated, With Decimal Arithmetic

Every amount this capability records or reports MUST be a whole peso, **truncated** rather than
rounded. All monetary arithmetic MUST use `decimal`; floating point MUST NOT be used at any point.

This repeats the rule already established in `rent-adjustment` and confirmed by the agency owner:
cent coins no longer circulate, and the agency settles any difference physically at the register.

#### Scenario: A computed recargo is truncated

- GIVEN a recargo computing to 47,860.80
- WHEN it is recorded or reported
- THEN it MUST be 47,860

#### Scenario: No floating point anywhere

- GIVEN the implemented calculation
- WHEN it is inspected
- THEN every monetary type MUST be `decimal`, and no `double` or `float` MUST appear

### Requirement: The Balance Names What It Covers

Wherever a balance is presented, it MUST state which concepts it includes. The balance covers
**rent, honorarios contractuales instalments, and recargo** — and nothing else.

Tasa municipal, seguros and "Otros Conceptos" are typed when a receipt is closed and never accrue,
so they are absent from this figure by design. A reader who assumes the balance is the whole of what
will be charged would be wrong, and the screen must not let them assume it.

#### Scenario: The figure is labelled

- GIVEN an account balance shown to an operator
- WHEN it is displayed
- THEN the display MUST name the concepts the figure covers

#### Scenario: Receipt-only amounts are absent

- GIVEN an account
- WHEN its balance is computed
- THEN no amount for tasa municipal, seguros or "Otros Conceptos" MUST be included

---

### Requirement: Payment and RecargoCharged Are Ledger Movements
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

### Requirement: A Honorarios Contractuales Instalment Is Payable With Its Rent Period
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

### Requirement: A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date
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

## Tests

> Tests 9, 10, 14 and 15 predate `cobranza-y-recibos`, which modified the requirements they
> exercise (the opening balance, and which period a payment settles). The current behaviour and its
> tests are in `openspec/changes/archive/2026-10-15-cobranza-y-recibos/spec.md`; where the two
> disagree, that change wins.

1. **One account per contract.** A tenant holding two contracts has two accounts, each with its own

2. **A tenant total is computed, not stored.** The sum over a tenant's contracts is produced on

3. **A recorded movement cannot be updated.** A raw SQL `UPDATE` against a movement row is refused.

4. **A recorded movement cannot be deleted.** A raw SQL `DELETE` against a movement row is refused.

5. **A correction appends.** Correcting a wrong amount produces a new movement naming the one it

6. **The ledger balance is the sum of movements**, and the **amount owed** for the same account

7. **A historical balance counts only movements up to its date.** A balance asked for a past date

8. **A contract signed after go-live opens with an empty account.** Creating its account with no

9. **A pre-existing contract carries its typed figure**, and zero is valid for a tenant who is up to

10. **An opening balance never changes.** After later movements are added, it reads exactly as
    entered.

11. **A late adjustment leaves accrued periods untouched.** Three periods accrued at the old canon
    stay at that canon after an adjustment is confirmed late, and no new movement appears for them.

12. **The next period uses the new canon.** The first period accruing after a late confirmation uses
    the adjusted figure.

13. **A partial payment is refused**, and records no movement.

14. **A payment settles the oldest unpaid period.** With July, August and September unpaid, paying
    one settles July.

15. **The remaining periods keep their own clocks.** After July is settled, August and September
    still count recargo from their own due dates.

16. **Time alone writes nothing.** An account read on three different days reports growing recargo
    and an identical movement count.

17. **Recargo is not capped.** A period 200 days overdue yields the full simple 2% per day with no
    ceiling.

18. **Two contracts count mora from their own due days.** Equally overdue periods on contracts with
    different due days produce different day counts.

19. **No rate constant exists in the calculation.** The rate and grace period are read from the
    obligation; no literal rate appears in the computation.

20. **The mora worklist names days, amount and surcharge.** Three accounts overdue by 5, 38 and 200
    days all appear, each carrying its days overdue, its amount owed and its recargo so far.

21. **An account up to date does not appear on the mora worklist.**

22. **A frozen account reports its frozen surcharge on the worklist**, not a figure that kept
    growing after the novación.

23. **A novación stops the surcharge on its signing date.** An account read a month after signing
    reports exactly the recargo accrued up to the signing date.

24. **The frozen figure is a dated movement, not a flag.** A movement carrying the frozen amount and
    the signing date exists, and no boolean carries this state.

25. **A date before the freeze still reads correctly.** The balance asked for an earlier date reports
    the recargo as of then, unaffected by the later freeze.

26. **A contract can be rescinded owing money.** Ending a contract whose account owes 300,000
    succeeds, and the account remains open with its balance intact.

27. **Payments land after the contract ended.** A payment against a rescinded contract's account is
    accepted and reduces the balance.

28. **No rent accrues after the contract ended.** A period falling after the end date produces no
    accrual.

29. **A computed recargo is truncated.** 47,860.80 is recorded as 47,860.

30. **No floating point anywhere.** Every monetary member is `decimal`; no `double` or `float`
    appears in this capability.

31. **The balance names what it covers.** The presented figure states that it includes rent,
    honorarios contractuales instalments and recargo.

32. **Receipt-only amounts are absent from the balance.** No tasa municipal, seguro or "Otros
    Conceptos" amount is included in a computed balance.

33. **Every new table ships with its GRANTs.** After the migration, connecting as each application
    role confirms it can perform exactly its intended operations on the new tables — none silently
    unreachable.
