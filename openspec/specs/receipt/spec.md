# Receipt Specification

## Purpose

The recibo: the numbered document handed over when a tenant pays. A header plus signed, typed lines,
in two copies per payment (*locatario* and *propietario*), numbered gaplessly from the agency's own
sequence, never edited, annulled by an appended record. It is a document, not a PDF: rendering is
`recibo-pdf`.

## Requirements

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

## Tests

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
