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

A contract that already existed when the system went live MUST have exactly one movement of kind
`OpeningBalance`, entered by a person and never computed. Its amount MAY be zero, for a tenant who is
up to date. The kind MUST be distinguishable from every other kind, so no reader mistakes a typed
figure for a derived one.

A contract **created in the system** MUST NOT have one. Its account begins empty, and its first
movement is its first rent accrual. There is nothing to carry over, because the system watched the
contract from its first day.

An account MUST NOT be refused for having no opening balance. An earlier draft of this requirement
demanded one of every account; that would have made it impossible to register a tenant who signs
tomorrow — the ordinary case, and the one the system will spend most of its life doing.

The agency keeps the go-live figures in a spreadsheet today, so loading them is transcription, not
reconstruction.

#### Scenario: A contract signed after go-live opens with an empty account

- GIVEN a contract created in the system
- WHEN its account is created
- THEN it MUST be accepted with no movements at all
- AND its balance MUST be zero

#### Scenario: A contract that predates the system carries its figure

- GIVEN a contract that existed before go-live, owing 150,000
- WHEN its account is opened
- THEN exactly one `OpeningBalance` movement of 150,000 MUST be recorded

#### Scenario: Zero is a valid opening balance for a pre-existing contract

- GIVEN a pre-existing contract whose tenant is up to date
- WHEN its account is opened with a balance of zero
- THEN it MUST be accepted
- AND the movement MUST still be recorded, so the account states that somebody asserted zero rather
  than that nobody looked

#### Scenario: At most one opening balance ever

- GIVEN an account that already carries an opening balance
- WHEN a second `OpeningBalance` movement is attempted
- THEN it MUST be refused

#### Scenario: An opening balance is never silently recomputed

- GIVEN an account with a typed opening balance
- WHEN any later movement is added
- THEN the opening balance MUST remain exactly as it was entered

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

### Requirement: A Payment Settles the Oldest Unpaid Period

When a payment settles a period, that period MUST be the oldest unpaid one on the account. The
operator MUST NOT be asked to choose, and MUST NOT be able to override the choice.

The payment movement itself is created by the collection change; this capability owns the rule that
decides which period it settles.

#### Scenario: Three months behind, one month paid

- GIVEN an account with July, August and September all unpaid
- WHEN one period is paid
- THEN July MUST be the period settled
- AND August and September MUST remain unpaid, each still counting recargo from its own due date

#### Scenario: The operator is never asked

- GIVEN the same account
- WHEN the payment is recorded
- THEN no choice of period MUST be presented, and no override MUST exist

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

## Tests

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
