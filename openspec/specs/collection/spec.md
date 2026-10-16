# Collection Specification

## Purpose

Taking a tenant's payment against one period of a contract's account: total only, for the period the
tenant names or else the oldest unpaid, with the recargo turned into a stored charge at that instant,
the recibos issued, and the whole thing either entirely written or not written at all. It also owns
voiding. It does not pay the owner, render a PDF, or present a screen.

## Requirements

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

## Tests

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
