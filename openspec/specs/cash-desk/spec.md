# Cash Desk Specification

## Purpose

Taking a tenant's payment from a screen and handing over the recibo, or voiding one by its printed
number. It owns the operator's flow only: every rule about what a payment or voiding is lives in
`collection` and `receipt`. It adds no rule to them, and it never computes an amount.

## Requirements

### Requirement: The Operator Finds an Account and Sees Whether It Is Ready

The desk MUST let the operator find a contract account by tenant name, by unit and by contract, and MUST
show, for every result, the tenant, the unit or units, the contract's status and its readiness. Search
MUST ignore case and accents (Decision 29). Every account is listed whatever its contract's status. The
directory MUST be readable by both roles.

Readiness (Decision 21) is three gates and one flag:

| Item | Kind | Not ready when |
|---|---|---|
| Honorarios rate entered | Gate | The contract's rate was never entered (`RateNeverEntered`; a rate of 0% **is** entered and is only warned about in the preview) |
| Account not frozen | Gate | A novación froze the recargo as of today (`AccountFrozen`) |
| Recibo starting number set | Gate | The agency's starting number was never set (Requirement 19) |
| Cutover date | **Flag only** | The contract has none. This is shown as information, not as a block |

An account that is not ready MUST show **which** gate and MUST tell the operator what to do about it. It
MUST remain selectable: readiness is shown so that the operator is not surprised, and the commit's own
refusal is still what decides.

#### Scenario: Find by tenant, ignoring case and accents

- GIVEN a tenant "Demaría"
- WHEN the operator searches "demaria"
- THEN the tenant's accounts MUST be listed

#### Scenario: Find by unit and by contract

- GIVEN an account for a unit and a contract
- WHEN the operator searches by the unit, and then by the contract
- THEN the same account MUST be listed each time

#### Scenario: A ready account says so

- GIVEN a rate entered, an unfrozen account and a starting number set
- WHEN it is listed
- THEN it MUST read as ready

#### Scenario: A missing rate is named

- GIVEN a contract whose honorarios rate was never entered
- WHEN it is listed
- THEN it MUST read as not ready, naming the missing rate

#### Scenario: A frozen account is named

- GIVEN an account whose recargo is frozen by a novación
- WHEN it is listed
- THEN it MUST read as not ready, naming the freeze

#### Scenario: An unset starting number makes every account not ready

- GIVEN no starting number has been set
- WHEN any account is listed
- THEN it MUST read as not ready, naming the starting number

#### Scenario: A 0% rate is ready

- GIVEN a contract with an administration rate of 0%
- WHEN it is listed
- THEN it MUST NOT read as not ready for the rate

#### Scenario: A missing cutover date is information, not a block

- GIVEN a contract with no cutover date, otherwise ready
- WHEN it is listed
- THEN it MUST read as ready
- AND it MUST show that it has no cutover date

#### Scenario: Both roles can search

- GIVEN an Empleado session
- WHEN she searches accounts
- THEN the results MUST be the same as an Admin's

#### Scenario: An ended contract is listed

- GIVEN an ended contract whose account still owes
- WHEN the operator searches for its tenant
- THEN it MUST be listed, showing that the contract has ended

### Requirement: The Desk Previews What Is Owed Before Asking for Money

Once an account is chosen, the desk MUST show a preview built by the existing domain preview, with the
optional **period**, optional **received date** and the three optional **closing lines** (tasa municipal,
seguro, Otros Conceptos) the operator may type. The preview MUST be requested automatically on choosing
the account, and again whenever any of those inputs changes, using a handed amount of 0 as the "what is
owed" step (Decision 26). It MUST show, from the preview and without computing any of it:

- the period being settled and the received date (defaulting to the agency's today, not the machine's);
- the previous amount owed, the ledger figure, and the resulting amount owed;
- every line and the total of the locatario recibo and of the propietario recibo as drafted;
- every warning (the 0% administration rate, and a pending rent adjustment: Decision 39) and every
  refusal in its operator text.

A refusal that is only that the handed amount is not the total (`AmountNotTotal`) at this step MUST NOT be
shown as an error. A refusal that means nothing can be collected (nothing payable, period already settled,
rate never entered, frozen, period not begun or not on the contract, a negative tasa municipal, a received
date in the future or beyond the bound, **or the recibo starting number never set**) MUST be shown, and the
operator MUST NOT be able to proceed to the summary while it stands.

The starting number being unset, which the domain reports as `CutoverNotSet`, MUST be explained to the
operator as a missing **starting recibo number** and MUST point to where an Admin sets it. It MUST NOT tell
the operator to set a contract's cutover date (Finding F1).

#### Scenario: Choosing an account previews it

- GIVEN an account with a payable period
- WHEN the operator chooses it
- THEN a preview MUST be requested with a handed amount of 0
- AND the screen MUST show the period, previous amount owed, ledger figure and both recibo drafts

#### Scenario: Changing an input re-previews

- GIVEN a preview on screen
- WHEN the operator types a seguro of 4,000
- THEN a new preview MUST be requested and shown, with the seguro on both drafts

#### Scenario: The default received date is the agency's today

- GIVEN the agency clock says 20 October, whatever the machine's clock says
- WHEN the operator opens the desk
- THEN the received date shown MUST be 20 October

#### Scenario: Not-the-total at the owed step is not an error

- GIVEN the owed-step preview, with handed amount 0
- WHEN it returns `AmountNotTotal`
- THEN the screen MUST show the figures and MUST NOT show an error

#### Scenario: A blocking refusal stops the flow

- GIVEN an account whose rate was never entered
- WHEN it is previewed
- THEN the refusal's operator text MUST be shown
- AND the operator MUST NOT be able to reach the summary

#### Scenario: An unset starting number is explained as such

- GIVEN a commit or preview refused as `CutoverNotSet` because no starting recibo number exists
- WHEN the screen shows it
- THEN the text MUST name the starting recibo number
- AND MUST NOT ask for a contract's cutover date

#### Scenario: A 0% rate warns and does not block

- GIVEN a contract with a 0% administration rate
- WHEN it is previewed
- THEN the warning MUST be shown
- AND the operator MUST be able to proceed

#### Scenario: A pending adjustment with a missing index warns and does not block

- GIVEN a contract whose adjustment fell due and is waiting for August's IPC
- WHEN it is previewed
- THEN a warning MUST name the pending adjustment, the index and the period (IPC, August)
- AND the preview MUST show the current canon, unchanged
- AND the operator MUST be able to proceed

#### Scenario: A pending adjustment ready to confirm warns too

- GIVEN a contract whose adjustment fell due, has every index value published, and is not confirmed
- WHEN it is previewed
- THEN a warning MUST say an adjustment is waiting for confirmation
- AND the operator MUST be able to proceed

#### Scenario: No pending adjustment, no warning

- GIVEN a contract with no adjustment due, or a fixed-price contract with no clause
- WHEN it is previewed
- THEN no adjustment warning MUST be shown

### Requirement: The Handed Amount Is Typed and Checked Against the Ledger Figure

The operator MUST type the handed amount. The desk MUST NOT pre-fill it, suggest it, or offer to copy it
from any figure. The desk MUST say that the handed amount is the amount the tenant hands over for the
period's rent, instalment and recargo, and it MUST show the **ledger figure** and each **recibo total**
under different names (`receipt`: "The Recibo Total Is Not the Ledger Figure"; Decision 25). The handed
amount is compared by the domain with the ledger figure and never with a recibo total. Fractional pesos
are truncated by the domain and shown truncated.

#### Scenario: The field starts empty

- GIVEN a preview on screen
- WHEN the handed amount field is first shown
- THEN it MUST be empty

#### Scenario: Typed lines do not change what is handed over

- GIVEN a ledger figure of 598,260 and a seguro of 4,000 typed
- WHEN the operator reads the screen
- THEN the ledger figure MUST read 598,260 and the locatario recibo total MUST read 602,260, each under its own name
- AND the operator who types 598,260 MUST be allowed to proceed

#### Scenario: Typing the recibo total is refused

- GIVEN the same preview
- WHEN the operator types 602,260
- THEN the preview MUST be refused as not the total, in its operator text

#### Scenario: A fractional amount is shown truncated

- GIVEN a seguro typed as 1,250.80
- WHEN the preview is shown
- THEN the seguro line MUST read 1,250

### Requirement: A Summary Is Confirmed Before Anything Is Committed

Per convention `confirm-with-a-summary`, committing a payment MUST be a separate, explicit act that
follows a summary. The summary MUST show everything `collection` ("A Confirmation Preview Precedes Every
Payment") says a summary shows, as the preview gave it: the period and received date, the previous amount
owed, every line of both recibos with sign, the recibo totals and the ledger figure named apart, the
resulting amount owed and the warnings. The commit action MUST be unavailable until a handed amount has
been typed and the preview built with it can be committed, and MUST be unavailable again the moment the
operator changes any input the summary was built from (account, period, received date, closing lines,
handed amount). The commit MUST apply the preview that was shown. A successful commit MUST show both
recibo numbers (locatario first), and MUST NOT print (Requirement 12).

#### Scenario: The commit needs a summary

- GIVEN a committable preview for the handed amount typed
- WHEN the operator reaches the summary
- THEN every field the `collection` summary requires MUST be shown
- AND the commit MUST be available

#### Scenario: The commit is unavailable until an amount is typed

- GIVEN the owed-step preview with no handed amount typed
- WHEN the operator looks for the commit
- THEN it MUST NOT be available

#### Scenario: Changing an input withdraws the summary

- GIVEN a confirmed-ready summary
- WHEN the operator changes the received date
- THEN the summary MUST be withdrawn and the commit MUST be unavailable until a new preview is shown

#### Scenario: A successful commit shows both numbers

- GIVEN a commit that writes recibos 31001 and 31002
- WHEN the result reaches the screen
- THEN both numbers MUST be shown, 31001 as locatario and 31002 as propietario
- AND the recibos MUST be shown on screen for reading

#### Scenario: A refused commit shows the refusal and writes nothing

- GIVEN a commit refused for any reason
- WHEN the result reaches the screen
- THEN the refusal's operator text MUST be shown
- AND no recibo numbers MUST be shown

### Requirement: A Stale or Concurrent Change Re-Previews and Shows What Changed

When a commit is refused as `PreviewStale` or `ConcurrentChange` (`CollectionPlanner.cs:50-53`), or a
voiding as `PreviewStale` (`VoidingPlanner.cs:20`), the desk MUST request a fresh preview immediately,
MUST show the operator what differs between the preview they confirmed and the new one (at minimum the
period, the received date, the previous amount owed, the ledger figure, the resulting amount owed, and
any recibo line whose amount changed), and MUST clear the confirmation (Decision 27). The typed handed
amount stays on screen. The desk MUST NOT commit again until the operator confirms the new summary, and
MUST NOT retry the commit on its own at any point.

#### Scenario: Another operator paid in between

- GIVEN a confirmed preview, and another operator has since paid the same period
- WHEN the commit is refused as `ConcurrentChange`
- THEN a fresh preview MUST be requested and shown
- AND the screen MUST say that the account changed and show what differs
- AND the commit MUST NOT be repeated

#### Scenario: The day turned over

- GIVEN a confirmed preview whose received date defaulted to today, and midnight has passed
- WHEN the commit is refused as `PreviewStale`
- THEN the new preview MUST show the new received date and the changed recargo
- AND the confirmation MUST be cleared

#### Scenario: The handed amount is kept but must be confirmed again

- GIVEN a stale refusal after the operator typed 598,260
- WHEN the fresh preview is shown with a ledger figure of 605,000
- THEN 598,260 MUST still be on screen
- AND the preview MUST be refused as not the total
- AND the commit MUST be unavailable

#### Scenario: A voiding goes stale the same way

- GIVEN a voiding preview, and the pair is annulled by someone else before confirmation
- WHEN the voiding is refused
- THEN a fresh voiding preview MUST be requested and its result shown, and the voiding MUST NOT be repeated

### Requirement: A Command Runs Once, and Not While Busy

While a preview, commit, voiding, print preparation or setup operation is running, every command that
starts a money-moving or state-changing operation (commit, void, set cutover date, set starting number)
MUST be unavailable. Invoking the commit command twice in quick succession (a double click) MUST result in
at most one commit being sent to the desk. A command that is finished MUST become available again whatever
the outcome, including a failure.

#### Scenario: A double click commits once

- GIVEN a committable summary
- WHEN the commit command is invoked twice before the first completes
- THEN the desk MUST receive exactly one commit

#### Scenario: The commands return after a failure

- GIVEN a commit that raises an exception
- WHEN the error has been shown
- THEN the operator MUST be able to use the screen again

### Requirement: Nothing Prints Automatically

The desk MUST NOT print, open a print dialog, or send anything to a printer as a consequence of a
committed collection or of a voiding (Decision P13). Printing is an action the operator chooses, available
whenever a recibo is on screen (Requirement 14).

#### Scenario: A commit does not print

- GIVEN a committed payment
- WHEN the result is shown
- THEN no print dialog MUST be opened and nothing MUST be sent to a printer
- AND a Print action MUST be available for the shown recibos

#### Scenario: A voiding does not print

- GIVEN a completed voiding
- WHEN the result is shown
- THEN no print dialog MUST be opened and nothing MUST be sent to a printer

#### Scenario: Skipping print loses nothing

- GIVEN a committed payment whose recibos were never printed
- WHEN the operator leaves the screen and later finds the recibo by its number
- THEN the same recibos MUST be shown and printable

### Requirement: An Issued Recibo Is Found by Number or From the Recent Collections

The desk MUST let the operator (either role) find an issued payment by the printed number of **either** of
its two recibos, and from a list of **recent collections** (newest first, by when they were recorded). A
list entry MUST show both numbers, the tenant, the period, the received date, the recorded date and
whether the payment is **annulled**. Opening an entry, or finding it by number, MUST display both
recibos exactly as issued: number, copy name, tenant, lessors, units, period, received date, each line
with its kind and signed amount, the total, and the non-fiscal legend (`receipt`: "A Recibo Is a Header
and Typed Lines", "The Recibo Is Labelled Non-Fiscal"). When it is annulled the display MUST also show
that it is annulled with the number, the reason, the user and the time (`receipt`: "A Recibo Is Never
Edited or Deleted; an Annulment Is Appended"). A number that matches no recibo MUST say so and show
nothing else. How many entries the list holds is design's (Parameters Left to Design).

#### Scenario: Either number finds the pair

- GIVEN a payment with recibos 31001 and 31002
- WHEN the operator finds 31002, and separately 31001
- THEN both recibos of the payment MUST be shown each time

#### Scenario: A number that does not exist

- GIVEN no recibo 99999
- WHEN the operator finds it
- THEN the screen MUST say no recibo has that number and MUST NOT show any recibo

#### Scenario: The recent list shows the annulled flag

- GIVEN one live payment and one annulled payment
- WHEN the recent collections are listed
- THEN each entry MUST show its numbers, tenant, period, received and recorded dates
- AND the annulled one MUST be flagged

#### Scenario: An annulled recibo shows who, when and why

- GIVEN an annulled pair
- WHEN it is opened
- THEN the screen MUST show that it is annulled, with the reason, the user and the time of the annulment
- AND its lines MUST still be readable

#### Scenario: What is displayed is what was issued

- GIVEN a recibo issued with a honorarios de administración line of -47,860
- WHEN a later change of the contract's rate has happened and the recibo is opened
- THEN the line MUST still read -47,860

#### Scenario: Both roles can find recibos

- GIVEN an Empleado session
- WHEN she finds a recibo issued by an Admin
- THEN it MUST be shown

### Requirement: A Recibo Prints on Demand, at Any Time, on the Paper the Operator Chooses

A recibo on screen (just issued, found by number, or opened from the recent list) MUST offer a **Print**
action, and the same action MUST work however long ago the recibo was issued (Decision P13; no age limit).
Print MUST open the Windows print dialog and print from the printer and the paper the operator chooses
there. A recibo prints as **one sheet carrying the recibo twice, ORIGINAL and DUPLICADO, with identical
content** (Decisions 15 and 16); the Print action offers the shown recibo and "print both recibos of the
payment", which prints the locatario sheet and the propietario sheet, each with its own number and lines.

**Content of each half** (from the agency's sample; the visual arrangement is the agency's design and not
fixed here):

- the agency's fixed data: logo, address, phone, email, tax-status text, from configuration (Decision 37);
- the non-fiscal legend "Documento no válido como Factura" in a box (`Recibo.cs:15`; the sample's casing of
  "Factura" is the agency's design), the caption ORIGINAL or DUPLICADO, "N° " and the recibo number, the
  received date as day, month and year, the word RECIBO and the recipient type (Locatario or Propietario);
- the band "COBRO de ALQUILER/SERVICIOS e IMPUESTOS por CUENTA y ORDEN de TERCEROS", the tenant's and the
  lessors' names and the "COBRO" line, from the stored header;
- the lines of the paper's fixed list and **Importe Neto** (Decisions 34 and 35);
- the rental-activity legend (from configuration), a blank Locador signature line, **"Son Pesos" followed
  by the total in words and "con NN/100.-"** (Decision 36), the "que RECIBO de" payer line with "-" for the
  CUIT, the five I.V.A. boxes unticked (Decision 38), and the broker's signature block with the matrícula
  number, from configuration (Decision 37).

**Fixed rows.** The rows of the paper's list the system models print always, at 0 when the recibo has no such
line; the rows it does not model print their label with an empty amount; every line the recibo has prints,
on an added row if the paper has none for it (Decision 35, Q1). Importe Neto MUST equal the recibo's
total, which is the sum of its signed lines.

**Paper.** The layout reproduces the agency's own design on blank paper (P14). **A4 landscape is the
default**, the two halves side by side. It MUST be derived from the printable area of the paper chosen in
the print dialog. If that paper is portrait, or too narrow for two legible halves, each half goes on its
own page, ORIGINAL first (Decision 19). Nothing MUST be clipped: a line wider than its half wraps, and a
half taller than the page continues on a following page. There MUST be no layout editor and no
positioning for pre-printed stationery. Cancelling the print dialog MUST print nothing and change nothing.
Printing MUST NOT alter the recibo, record anything, or consume a number (Decision 20).

**What this spec does not fix**: where each field sits, fonts, borders, the logo's size and the exact
casing of the captions. Until the design is applied by design/apply, a neutral layout carries the same
content.

#### Scenario: Print is offered, not performed

- GIVEN a recibo on screen
- WHEN nothing is clicked
- THEN nothing MUST be sent to a printer
- AND a Print action MUST be available

#### Scenario: Print opens the Windows dialog

- GIVEN a recibo on screen
- WHEN the operator chooses Print
- THEN the Windows print dialog MUST open

#### Scenario: Cancelling prints nothing

- GIVEN the print dialog is open
- WHEN the operator cancels it
- THEN nothing MUST be printed, no number MUST be consumed, and the recibo MUST be unchanged

#### Scenario: An old recibo prints

- GIVEN a recibo issued 90 days ago, found by its number
- WHEN the operator chooses Print
- THEN it MUST print, by an Empleado as by an Admin

#### Scenario: Original and duplicate are identical

- GIVEN recibo 31001
- WHEN it is printed
- THEN the sheet MUST carry it twice, captioned ORIGINAL and DUPLICADO
- AND every number, date, name, line, amount and the words MUST be identical in the two halves

#### Scenario: Print both prints two sheets

- GIVEN a payment with recibos 31001 (locatario) and 31002 (propietario)
- WHEN the operator chooses "print both recibos of the payment"
- THEN a locatario sheet numbered 31001 and a propietario sheet numbered 31002 MUST be printed
- AND no line of the propietario recibo MUST appear on the locatario sheet, nor the reverse

#### Scenario: The legend is on each half

- GIVEN any recibo printed
- WHEN the output is read
- THEN the legend "Documento no válido como Factura" MUST be on both halves

#### Scenario: A4 landscape is the default, two-up

- GIVEN a printer whose default paper is A4
- WHEN the operator accepts the dialog unchanged
- THEN the layout MUST be A4 landscape with ORIGINAL and DUPLICADO side by side on one page

#### Scenario: Portrait paper gives one half per page

- GIVEN the operator chooses A4 portrait in the print dialog
- WHEN the recibo is printed
- THEN ORIGINAL MUST be on the first page and DUPLICADO on the second
- AND every number, line, amount and the words MUST appear in full on each

#### Scenario: Another landscape size reflows and clips nothing

- GIVEN the operator chooses Carta landscape, and separately a size too narrow for two legible halves
- WHEN the recibo is printed
- THEN every number, line, amount and the legend MUST appear in full
- AND content that does not fit a half's width MUST wrap and content that does not fit its height MUST continue on a following page

#### Scenario: Agency data comes from configuration

- GIVEN the configured address, phone, tax-status text, broker name and matrícula
- WHEN a recibo is printed
- THEN each MUST appear as configured
- AND a missing item MUST stop the print with a message naming it, and nothing MUST be invented

#### Scenario: The fixed rows print as decided

- GIVEN a recibo with a rent line, a recargo line and no tasa municipal
- WHEN it is printed
- THEN Tasa Municipal MUST print with 0
- AND Sellado Provincial, Imp. Inmobiliario, Luz EPE and the other unmodelled rows MUST print their label with an empty amount, not 0
- AND Importe Neto MUST equal the recibo's total

#### Scenario: A stored line with no paper row still prints

- GIVEN a recibo with a seguro of 4,000
- WHEN it is printed
- THEN the seguro MUST appear on an added row
- AND Importe Neto MUST include it

#### Scenario: The amount in words

- GIVEN a recibo with a total of 47,860
- WHEN it is printed
- THEN it MUST read "Son Pesos cuarenta y siete mil ochocientos sesenta con 00/100.-" on both halves (capitalisation is design's)

#### Scenario: Unfilled sample fields

- GIVEN a recibo printed
- WHEN the output is read
- THEN the CUIT MUST read "-", the five I.V.A. boxes MUST be unticked and the Locador signature line MUST be blank

#### Scenario: No layout editor

- GIVEN the print dialog or any screen of this change
- WHEN the operator looks for a way to move fields or to align to pre-printed forms
- THEN none MUST exist

### Requirement: A Voided Recibo Is Printed as Voided

A recibo that has been annulled MAY be displayed and printed (Requirement 14), and every such print MUST
carry the word **ANULADO** across both halves (original and duplicate) together with the annulment's date and reason (Decision 18).
The annulment MUST be read from the database when the print is prepared, not carried over from what the
screen showed earlier. A recibo that is not annulled MUST NOT carry the mark, and its reprint MUST be
identical to its first print (Decision 17). The mark MUST NOT change or hide any number, line, amount or
the legend.

#### Scenario: A voided pair reprints marked on both halves of each sheet

- GIVEN a pair annulled with the reason "cobro duplicado"
- WHEN either recibo is printed
- THEN both halves MUST carry ANULADO, the annulment's date and the reason "cobro duplicado"
- AND every number, line and amount MUST still be readable

#### Scenario: Printed before the voiding, reprinted after

- GIVEN a pair whose recibos were printed, and then voided
- WHEN the pair is found by its number and printed again
- THEN the new print MUST carry ANULADO

#### Scenario: A voiding that happens while the screen is open

- GIVEN a recibo on screen that was live when it was opened, and another operator then voids it
- WHEN the operator chooses Print
- THEN the print MUST carry ANULADO

#### Scenario: A live recibo reprints unmarked and identical

- GIVEN a live recibo printed twice, a week apart
- WHEN the two outputs are compared
- THEN their content MUST be identical and neither MUST carry ANULADO or any "reprint" mark

### Requirement: Voiding Is Found by Printed Number, Reasoned, Previewed and Confirmed

The operator MUST be able to start a voiding by typing a printed recibo number or by choosing an entry in
the recent collections (Requirement 13), whichever recibo of the pair. The desk MUST require a **reason**
before it requests the preview, MUST show the voiding preview (what is reversed, the resulting amount
owed, and both recibos that will be annulled), and MUST require an explicit confirmation before it
voids (`collection`: "Voiding Reverses the Payment and Annuls Both Recibos"; convention
`confirm-with-a-summary`). The screen MUST pre-gate by the operator's role and the age of the pair from
the table in `collection` (up to 7 days: both roles; 8 to 30 days: Admin only; 31 days or more: nobody),
explaining why an action is unavailable, and MUST NOT be the rule: the preview's refusals
(`ReasonRequired`, `AlreadyAnnulled`, `AdminOnly`, `WindowClosed`, `PreviewStale`) and the database's
decide (Decision 28). An already-annulled pair MUST NOT offer a voiding. After a voiding the screen MUST
show that the pair is annulled and MUST NOT print (Requirement 12).

#### Scenario: Void by the number of either recibo

- GIVEN a live pair 31001 and 31002
- WHEN the operator types 31002
- THEN the pair MUST be found and its voiding preview MUST be offered

#### Scenario: Void from the recent list

- GIVEN a live payment in the recent collections
- WHEN the operator chooses it to void
- THEN the same voiding preview MUST be offered

#### Scenario: A reason is required before the preview

- GIVEN a live pair found
- WHEN the operator tries to preview the voiding with no reason
- THEN the voiding MUST NOT proceed, and the refusal "debe indicar el motivo" MUST be shown

#### Scenario: The preview shows what will be reversed

- GIVEN a live pair and a reason
- WHEN the voiding is previewed
- THEN the screen MUST show the corrections, the resulting amount owed, and that both recibos will be annulled

#### Scenario: An Empleado is pre-gated at day 8

- GIVEN a pair recorded 8 days ago and an Empleado
- WHEN she finds it
- THEN the voiding MUST NOT be offered, and the screen MUST say only an Admin may void after 7 days
- AND an Admin MUST be offered it

#### Scenario: Nobody voids at day 31

- GIVEN a pair recorded 31 days ago
- WHEN an Admin finds it
- THEN the voiding MUST NOT be offered, and the screen MUST say the window has closed

#### Scenario: The database overrules a stale pre-gate

- GIVEN a voiding offered to an Empleado at day 7 that the database refuses at day 8 (the day changed)
- WHEN she confirms
- THEN the voiding's refusal text MUST be shown and nothing MUST be written

#### Scenario: An annulled pair offers no voiding

- GIVEN a pair already annulled
- WHEN it is found
- THEN no voiding MUST be offered
- AND it MUST show as annulled

#### Scenario: A voiding does not print

- GIVEN a completed voiding
- WHEN the result is shown
- THEN nothing MUST be sent to a printer, and Print MUST be available for the now-annulled recibos

## Tests

8. **The account directory finds by tenant, unit and contract, ignoring case and accents, for both roles,
   including ended contracts.** *(Infrastructure.Tests, run as each role)*
9. **Readiness: a missing rate, a frozen account and an unset starting number each make an account not
   ready and are named; a 0% rate and a missing cutover date do not.** *(Infrastructure.Tests for the
   flags; Presentation.Tests for the display)*
10. **Choosing an account requests a preview with handed amount 0; changing the period, received date or
    a closing line requests another.** *(Presentation.Tests)*
11. **The default received date is the agency clock's today, not the machine's.** *(Presentation.Tests)*
12. **`AmountNotTotal` at the owed step is shown as figures, not as an error; every blocking refusal
    keeps the operator from the summary.** *(Presentation.Tests)*
13. **An unset starting number reported as `CutoverNotSet` is explained as the starting recibo number and
    never as a contract's cutover date.** *(Presentation.Tests)*
13a. **A due, unconfirmed adjustment on the previewed contract warns, naming each missing index and period,
    or saying it is ready to confirm; the operator can still proceed; a contract with nothing due shows no
    adjustment warning.** *(Presentation.Tests)*
14. **The handed amount starts empty; with a seguro typed, the ledger figure and the recibo total are
    shown under different names; typing the ledger figure is accepted and typing the recibo total is
    refused.** *(Presentation.Tests)*
15. **The commit is unavailable until an amount is typed and a committable summary is shown, and is
    withdrawn when any input changes.** *(Presentation.Tests)*
16. **A successful commit shows both numbers, locatario first, shows the recibos, and sends nothing to the
    printer.** *(Presentation.Tests)*
17. **`PreviewStale` and `ConcurrentChange` each cause one fresh preview, show what differs, clear the
    confirmation, keep the typed amount, and never a second commit without a new confirmation. Proved by
    counting the desk's commit calls.** *(Presentation.Tests)*
18. **Invoking the commit twice before the first completes sends one commit; commands return after a
    failure.** *(Presentation.Tests)*
19. **A voiding preview and a voiding that go stale re-preview the same way.** *(Presentation.Tests)*
20. **Nothing is sent to a printer after a commit or after a voiding; Print is available; cancelling the
    print dialog changes nothing.** *(Presentation.Tests, print seam faked)*
21. **Finding a recibo by either number returns the whole pair; an unknown number shows nothing; the
    recent list shows numbers, tenant, period, received and recorded dates and the annulled flag.**
    *(Infrastructure.Tests, run as each role)*
22. **The reader returns an annulled recibo with its reason, user and time, read at the moment of the
    call; a recibo voided after an earlier read reads as annulled on the next.** *(Infrastructure.Tests)*
23. **The reader returns the recibo as stored: after the contract's rate or the tenant's name changes, the
    honorarios line and the header text read as issued.** *(Infrastructure.Tests)*
24. **The print model of a recibo carries ORIGINAL and DUPLICADO with identical content, the legend, the
    received date, and for an annulled recibo the ANULADO mark, date and reason on both halves and none
    for a live one; the pair's two sheets never share a line; two prints of a live recibo are identical.**
    *(Presentation.Tests)*
24a. **Fixed rows: modelled rows print 0 when absent, unmodelled rows print an empty amount, a stored line
    with no paper row prints on an added row, and Importe Neto equals the recibo's total.**
    *(Presentation.Tests)*
24b. **Amount in words**: 47,860 reads "cuarenta y siete mil ochocientos sesenta", 507,000 "quinientos siete
    mil", 598,260 "quinientos noventa y ocho mil doscientos sesenta", 21,000 "veintiún mil", 31,001
    "treinta y un mil uno", 1,000,000 "un millón", 100 "cien", 101 "ciento uno", 0 "cero"; the suffix is
    always "con 00/100.-"; a negative total is prefixed "menos". *(Presentation.Tests)*
24c. **Missing agency data (any configured item) stops the print and names the item; nothing is invented.**
    *(Presentation.Tests)*
24d. **The paper rule**: A4 landscape gives two-up; portrait or too narrow gives ORIGINAL then DUPLICADO
    on separate pages. *(Presentation.Tests for the decision; M1 for the real output)*
25. **Voiding is found by either number and from the recent list; a reason is required before the preview;
    the preview shows the corrections, the resulting amount owed and both recibos.** *(Presentation.Tests)*
26. **The voiding pre-gate follows the table (7 days both roles, 8 to 30 Admin only, 31 none, annulled
    none), and a database refusal after an out-of-date pre-gate is shown as the refusal.**
    *(Presentation.Tests for the pre-gate; Infrastructure.Tests already prove the database's side)*

**By hand, in verify (printing's paper behaviour; nothing here can run on CI)**

M1. **A4 landscape gives ORIGINAL left and DUPLICADO right on one page; A4 portrait and Carta give a
    layout with every number, line, amount, the words and the legend present and nothing clipped.**
    *(Tomás, real printer or Microsoft Print to PDF)*

M2. **A voided pair reprints with ANULADO, the date and the reason on both halves, and the numbers still
    readable under it.** *(Tomás)*

M3. **A commit and a voiding on a real run open no print dialog by themselves.** *(Tomás, in the live
    smoke if the database is awake)*
