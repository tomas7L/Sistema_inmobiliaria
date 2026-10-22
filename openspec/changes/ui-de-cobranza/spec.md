# Spec: ui-de-cobranza (the cash desk, the recibo on paper, and go-live setup)

Single-file specification for the `ui-de-cobranza` change. Three capabilities are **NEW**:
**`desktop-shell`**, **`cash-desk`**, **`go-live-setup`**. Three are **MODIFIED**: **`contract-account`**,
**`access-control`**, **`receipt`**. `collection` and `lease-contract` are **not** modified (see "Capabilities
covered").

Domain vocabulary, glossed once: **cobranza** = collection; **recibo** = receipt; **locatario** = tenant;
**locador / propietario** = lessor / owner of the property (the *propietario recibo* is the owner's copy);
**recargo** = late-payment surcharge; **tasa municipal**, **seguro**, **Otros Conceptos** = the three lines
typed at closing; **honorarios** = the agency's fees; **anulación** = annulment (voiding); **cutover** =
the go-live boundary of a contract that predates the system; **ANULADO** = the word printed on a voided
recibo.

The proposal is `openspec/changes/ui-de-cobranza/proposal.md` (the file, not the stale Engram mirror: it
predates Decisions 13 and 14). Owner answers are cited as `§N` of
`openspec/domain/respuestas-del-dueno.md`. Rules already in the archived `collection`, `receipt`,
`contract-account` and `access-control` specs are cited, not restated.

**What this spec does not do.** It states what must be true, observably. Class names, port names, SQL,
XAML, the visual layout of the recibo and the exact Spanish wording of screens are design's. Where a
parameter is design's, the text says so.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table below, rows 15 to 38 | These are the calls this spec had to make. Each has a strength. The **weak** and **medium** rows are where you can change my mind. Rows 15 to 19 and 34 to 38 are about the printed sheet and were rewritten after the agency's real recibo sample was read. |
| **Findings** box right after the table | Six places where the proposal or the code says something different from what was assumed. Row F1 changes what the operator sees on go-live day; row F6 is the sample against the recibo model. |
| **Questions for Tomás** (last section before Tests) | Three, ranked. Q1 (the fixed rows) was answered on 2026-10-22; Q2 and Q3 are still open and block nothing. |
| Requirements 12 to 15 (printing) and 16 (voiding) | The two things you corrected on 2026-10-22, written as behaviour. |

**Skip** the Tests section and "Parameters left to design" unless you want the detail. **Skim** the rest;
the scenarios are there so a test can be written from each one. There is no code to read in this change
yet.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code or a living spec (path
and line given). **SPEC CALL** = this spec had to decide because the proposal did not. Strength:
**strong**, **medium**, **weak** (act on it only provisionally).

Rows P1 to P14 are the proposal's Decisions, in the proposal's numbering, listed only where this spec
leans on them. Rows 15 onwards are this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | The Admin go-live screen (cutover date per contract, recibo starting number) is in this change. | CONFIRMED BY USER (Tomás, 2026-10-16) | Strong |
| P2 | Recibo printing is in: on-screen recibo plus system printing, no PDF library. PDF stays in `recibo-pdf`. | CONFIRMED BY USER (2026-10-16) | Strong |
| P4 | `contracts.cutover_date` is Admin-only at the database. | CONFIRMED BY USER (2026-10-16); `contracts` has table-level `GRANT INSERT, UPDATE` to both roles (`20260924202549_AddUsersAndRoles.cs:161`) | Strong |
| P8 | Three new ports (account directory, recibo reader, go-live setup) feed the screens. | Exploration gaps 1 to 3 | Strong |
| P11 | Recargo terms (2%, no grace) come from configuration, not a constant. | `RecargoTerms.cs:14` has no default by design | Strong |
| P12 | Hiding Admin screens from an Empleado is convenience; the database is the enforcement. | Proposal; `access-control` spec | Strong |
| P13 | **Printing is always optional and repeatable.** Nothing prints by itself after a collection or a voiding. Any issued recibo can be found later and printed or reprinted. | CONFIRMED BY USER (Tomás, 2026-10-22) | Strong |
| P14 | **A4 by default, the agency's own design on blank paper, sized from the paper chosen in the Windows print dialog.** No layout editor, no pre-printed stationery. | CONFIRMED BY USER (2026-10-22) on A4 and own design; the size adaptation was the assistant's proposal | Strong on A4 and own design; **medium** on reflow |
| 15 | **The unit of printing is the recibo, and a sheet is one recibo printed twice.** The agency's sample is one **A4 landscape** sheet split vertically: **ORIGINAL** on the left, **DUPLICADO** on the right, identical content. A payment's two recibos (locatario, propietario) are therefore **two sheets**. Print offers the shown recibo and "print both recibos of the payment"; either number finds the payment. | VERIFIED FACT from the agency's sample (read by the orchestrator, 2026-10-22); replaces my earlier reading of one sheet carrying both recibos | **Strong** on original and duplicate being identical; medium on offering both |
| 16 | **Original and duplicate carry identical content**: the same number, date, recipient type, names, lines, importe neto and amount in words. They differ only in their caption (ORIGINAL, DUPLICADO). The locatario and propietario recibos are separate sheets with their own lines (the propietario carries a negative honorarios de administración line and no instalment; `receipt/spec.md:140-165`, `222-277`). | VERIFIED FACT from the sample | **Strong** |
| 17 | **A reprint of a live recibo is identical to its first print and carries no "reprint" mark.** The system does not record that a print happened, because printing is optional (P13), so it cannot know which print is "the first". | CONFIRMED BY USER (Tomás, 2026-10-22, answering Q3) | Strong |
| 18 | **A recibo that has been annulled prints with the word ANULADO across both halves (original and duplicate), plus the annulment's date and reason.** Annulling either recibo annuls the pair (`collection`, Decision 19), so both recibos' sheets are marked. The annulment is read from the database at the moment of printing, never remembered by the screen, so a recibo printed before its voiding and reprinted after it is marked. The wording and placement are design's. | SPEC CALL (Tomás asked for a proposal) | **Medium** on the mark and the content; weak on the placement |
| 19 | **The printed layout is derived from the paper's printable area, and nothing is clipped.** The application asks for A4 landscape and lays out two halves side by side. If the paper chosen is portrait, or a landscape size too narrow for two halves at the smallest legible type size, **each half goes on its own page, ORIGINAL first and then DUPLICADO**. Inside a half, content wraps to the half's width and continues on a following page if it exceeds the height. The smallest legible size and the switch point are design's. | SPEC CALL following P14 and the sample | **Medium** |
| 20 | **No record of printing is kept**: no counter, no "printed by", no log. | SPEC CALL (nothing in the proposal asks for one) | Medium |
| 21 | **Readiness has three hard gates and one informational flag.** Gates: honorarios rate entered, account not frozen, recibo starting number set. Flag: the contract's cutover date is set or not set. A missing cutover date is **not** a gate, because `CutoverDate` is null both for a contract that predates the system and has not been loaded yet, and for a contract created in the system, which legitimately has none (`Contract.cs:75`; `contract-account` spec, "A contract created in the system has no cutover date and is unaffected"). | VERIFIED FACT; corrects the proposal (Finding F1, F2) | **Strong** that it cannot be a gate; medium that "informational flag" is the right display |
| 22 | **The cutover date's value rule**: it MUST NOT be after the agency's today, and MUST NOT be before the contract's start date. Nothing else is bounded. A future date would silently suppress the accrual of periods that are neither on the opening balance nor yet due; a date before the start suppresses nothing and means nothing. | SPEC CALL (proposal Decision 9 says one is needed; Open Item 3 leaves the rule to design, so design MAY refine the lower bound, but MUST NOT admit a future date) | **Medium**; weak on the lower bound |
| 23 | **The cutover hardening covers UPDATE only.** An INSERT of a contract carrying a cutover date by an Empleado is not blocked by it (the existing trigger is `BEFORE UPDATE OF cutover_date`, `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:171-173`). Named as a limit under the `access-control` rule that requires limits to be named. | VERIFIED FACT; SPEC CALL to leave it | **Medium** (low consequence: an inserted contract has no movements, and the Empleado cannot change the date afterwards) |
| 24 | **The `access-control` "Operation Rule" is amended**: it says Empleado is excluded from *exactly two things* and "there is no third exclusion" (`access-control/spec.md:11`). Setting the starting number is already a third, accepted in `cobranza-y-recibos` (Decision 39) but never reflected there. This change names the two go-live parameters as one bounded departure. | VERIFIED FACT; SPEC CALL | **Strong** that the text is inconsistent; medium on the wording |
| 25 | **The handed amount is never pre-filled.** The two figures keep their names: the **ledger figure** (rent + instalment + recargo, what the tenant hands over for the period) and the **recibo total** of each recibo (which also carries the typed lines). The operator is told which one the handed amount is compared with. | VERIFIED FACT (`CollectionPlanner.cs:102`; `collection` Decision 27); the labelling is a SPEC CALL closing proposal Decision 10 | **Medium-strong** |
| 26 | **A preview with a handed amount of 0 is the "what is owed" step.** It carries every figure and both drafts and is refused only as `AmountNotTotal` (`CollectionPlanner.cs:33`). The screen shows that case as "what is owed", not as an error. | Exploration; proposal 4.4 | Strong (technical) |
| 27 | **After a stale or concurrent-change refusal the typed handed amount is kept on screen but the confirmation is cleared**; the operator must read the new summary and confirm again. Nothing is retried on the operator's behalf. | SPEC CALL following proposal 4.4 and `collection` Decision 29 | **Medium** |
| 28 | **The voiding screen pre-gates by role and age but never decides.** It offers or withholds the action from the age of the pair and the operator's role as the table in `collection` says, and it still submits whatever the operator is allowed to attempt; the refusal text of the domain and the database is what is shown if the pre-gate and the database disagree. | `collection` Decision 45; P12 | Strong |
| 29 | **The account picker lists every contract account, whatever the contract's status.** An ended contract can still owe. The status is shown. Search ignores case and accents ("Demaria" finds "Demaría"). | SPEC CALL (the proposal does not say) | **Weak**; low consequence, one line to narrow |
| 30 | **The go-live screen lists every contract, including those with no account**, which are shown as "cannot be set: no account" because `Contract.SetCutoverDate` requires the contract's own account (`Contract.cs:199-202`) and nothing in `src` creates accounts yet (exploration gap 4; initial data load follows). | VERIFIED FACT | Medium |
| 31 | **A setup refusal is a refusal, not an error.** The database's "cutover locked", "cutover Admin-only", "starting number locked" and the starting number's own bound are shown as operator text on the setup screen, like the desk's refusals. | SPEC CALL following `CollectionDesk.cs:544-558` precedent | Medium |
| 32 | **The recibo starting number is bounded by what the database stores**: a whole number, at least 1 (`ck_recibo_numbering_starting_positive`, `20261015204506_AddCollectionsAndRecibos.cs:65`) and at most the largest `int` (`IReciboNumbering.SetStartingNumberAsync(int)`, `IReciboNumbering.cs:14`). The screen refuses anything else before calling the port. | VERIFIED FACT | Strong |
| 33 | **Recargo terms are read once, when the post-login services are built.** A missing or invalid value stops the cash desk from opening, with a message naming the setting; the application never falls back to 2%. The shipped configuration carries 0.02 and 0 days. | SPEC CALL following P11 and `RecargoTerms.cs:14` ("fail loudly") | **Medium-strong** |
| 34 | **The paper has a fixed list of rows; the system models only some.** Sample rows, in paper order: Alquiler (with its period), Tasa Municipal, Sellado Provincial, Imp. Inmobiliario, Aguas Prov. Sta. Fe, Litoral Gas, Luz EPE, CUOTA 1, CUOTA 2, Dep. en Garantía, Recargo, Reserva, Expensas, Saldo a Favor, Saldo Deudor, Honorarios, Otros Conceptos, then Importe Neto. The model has eight kinds (`ReciboLineKind.cs:20-50`): Rent, ContractualFeeInstalment, Recargo, TasaMunicipal, Seguro, OtrosConceptos, SaldoDeudor, HonorariosAdministracion. **Modelled and on the paper**: Alquiler, Tasa Municipal, Recargo, Saldo Deudor, Honorarios, Otros Conceptos. **On the paper, not modelled**: Sellado Provincial, Imp. Inmobiliario, Aguas Prov. Sta. Fe, Litoral Gas, Luz EPE, Dep. en Garantía, Reserva, Expensas, Saldo a Favor. **Modelled, with no row on the sample**: Seguro. **CUOTA 1 and CUOTA 2** sit indented under Luz EPE on the sample, so they are read as the two instalments of the electricity bill: unmodelled rows, not `ContractualFeeInstalment`, which therefore prints on an added row. This change adds **no** kind to the domain. | VERIFIED FACT (sample against `ReciboLineKind.cs`) | Strong on the lists; the CUOTA reading is **medium** (from the sample's indentation; ask Nicolás only if the layout proves it wrong) |
| 35 | **The fixed rows: modelled rows always print, unmodelled rows print their label with an empty amount, and every stored line prints.** A modelled row with no line on the recibo prints 0 (as the sample prints $0). A row the system does not model prints its label and **no amount** (neither 0 nor a figure): "0" would state something the system does not know (a tenant may have paid Luz elsewhere). A stored line whose kind has no row on the paper (Seguro, and the instalment if it is not CUOTA) prints on an added row, so Importe Neto still equals the sum of the stored lines. Row order on paper follows the agency's design, not the catalog's print order. Rejected alternative: print only the lines the recibo has. | CONFIRMED BY USER (Tomás, 2026-10-22, answering Q1) | Strong |
| 36 | **The amount in words is printed on every recibo**: "Son Pesos" followed by the recibo's total in Spanish words and "con NN/100.-" for the centavos. Amounts are whole pesos (`receipt`, "A Recibo Is a Header and Typed Lines"), so NN is always 00. A total of zero reads "cero". A negative total reads "menos" before the words. | VERIFIED FACT from the sample; the zero and negative cases are SPEC CALLS | **Strong** on the line; weak on the negative case (cannot arise from today's lines in practice) |
| 37 | **The agency's fixed printed data comes from configuration, never from a constant in code**: agency name, address, phone, email, tax status text ("RESPONSABLE MONOTRIBUTO"), the rental-activity legend, the broker's name, the words "CORREDOR INMOBILIARIO" and the matrícula number, and the logo. Missing data stops printing with a message naming it; nothing is invented. None of it is a secret. | CONFIRMED BY USER (orchestrator, 2026-10-22); precedent P11 | **Strong** |
| 38 | **Printed fields the stored recibo cannot supply.** The stored header holds the tenant's name, the lessors' names and the units only (`ReciboHeader.cs:11-15`): it holds **no CUIT/CUIL and no payer other than the tenant**. The sample prints "que RECIBO de <payer> - C.U.I.T: <cuit or -->". This change prints the tenant (locatario recibo) or the lessors (propietario recibo) as payer, with "-" for the CUIT, and adds nothing to the header. The five I.V.A. boxes print **unticked**, and the Locador signature line prints blank. The date (DIA MES AÑO) is the recibo's **received date**. Printing the CUIT is a later change that snapshots it at issuance. | VERIFIED FACT (header fields); the payer for the propietario recibo is a SPEC CALL | **Medium**; the lessors as payer of a propietario recibo is **strong**: the sample (a propietario recibo) prints the lessors' names there |
| 39 | **A pending rent adjustment is shown at the desk as a warning, never as a block.** When the contract being previewed has an adjustment whose due date has arrived and is not confirmed, the preview warns: naming each missing index and its period when a value is not yet published, or saying the adjustment is ready to confirm when every value is. The operator may still collect at the current canon, which is what the living rule already charges (`rent-adjustment`: "Missing Index Value Leaves the Adjustment Pending"). Read from the existing worklist port, filtered to the contract (`IDueAdjustmentQuery.cs:9-12`, `DueAdjustment.cs:27-35`); nothing is added to the domain. | CONFIRMED BY USER (Tomás, 2026-10-22) | Strong on warn-and-continue; **medium** on also warning when the adjustment is ready but unconfirmed |

### Findings (the proposal or the code differs from what was assumed)

| # | Finding | Evidence | What this spec does |
|---|---|---|---|
| F1 | `CollectionRefusal.CutoverNotSet` is raised when the **recibo starting number** is unset, not when a contract's cutover date is unset. Its operator text, though, says *"Al contrato le falta la fecha de corte. Cárguela antes de cobrar."* An operator on go-live day who forgot the starting number would be sent to the wrong screen. | Raised at `CollectionDesk.cs:120-123`; text at `CollectionPlanner.cs:81-82`; proven by `CollectionDeskAtomicityTests.cs:112-124` | Requirement 7 makes the screen tell the operator the starting number is missing and where to set it. Whether the text is corrected in Domain or the screen maps it is design's. |
| F2 | A contract with **no cutover date is never refused** by the desk. The planner never emits `CutoverNotSet` (archive `tasks.md:256`). The proposal's readiness list ("cutover set") and risk row ("readiness gaps make every commit refuse") treat it as a commit-refusing gap. | `CollectionDesk.cs:55-64`, `120-123`; `CollectionPlanner.cs:47` | Decision 21: informational flag, not a gate. |
| F3 | The domain `Recibo` has **no annulment state, no recorded-at and no collection id**. An annulment carries only the number and reason; the user and time are stamped by the database. A recibo reader that returns `Recibo` cannot show "annulled with reason, user and time", which `receipt` requires of every read of an annulled recibo. | `Recibo.cs:9-58`; `ReciboAnnulment.cs:8-22`; `CollectionRecord.cs:11-18` | Requirements 13 and 15 state what the reader must expose. The shape is design's. |
| F4 | `access-control` still says Empleado has exactly two exclusions. | `access-control/spec.md:11` | Decision 24, Requirement 21. |
| F5 | I first read "one sheet with two parts" (§1) as one sheet carrying both recibos. **The sample says otherwise**: a sheet is one recibo printed twice (ORIGINAL left, DUPLICADO right, identical), and the two recibos of a payment are two sheets. | Agency's sample; `receipt/spec.md:140-165`, `222-277` | Decisions 15 and 16 replace my first reading. Resolved. |
| F6 | The sample's fixed row list has **nine rows the model has no kind for**, one modelled kind (Seguro) with no row, and CUOTA 1 and 2 (read as the Luz EPE instalments). The stored header has no CUIT. The proposal assumed the recibo's content was what the system already stores. | `ReciboLineKind.cs:20-50`; `ReciboHeader.cs:11-15`; Decisions 34 and 38 | Decisions 34, 35, 38. No domain change in this change. Q1. |

Smaller points, not findings: the database stores recibo numbers as `int` while the domain's are `long`
(`CollectionDesk.cs:181`, `:224`; `Recibo.cs:19`), so the reader must not widen what the database cannot
hold; and `lease-contract` carries no cutover text (a search of that spec finds none), so the instruction
to delta it is not needed, because the cutover requirement lives in `contract-account`
(`contract-account/spec.md:548`).

## Table of Contents

1. [Capabilities covered](#capabilities-covered)
2. [Capability: desktop-shell (NEW)](#capability-desktop-shell-new)
   1. [Requirement 1: The Main Window Receives the Session and Shows Who Is Working](#requirement-1-the-main-window-receives-the-session-and-shows-who-is-working)
   2. [Requirement 2: Admin-Only Screens Are Hidden From an Empleado, and the Database Still Decides](#requirement-2-admin-only-screens-are-hidden-from-an-empleado-and-the-database-still-decides)
   3. [Requirement 3: Post-Login Services Are Wired From the Session, One Database Context per Operation](#requirement-3-post-login-services-are-wired-from-the-session-one-database-context-per-operation)
   4. [Requirement 4: A Failure That Is Not a Refusal Never Crashes the Window](#requirement-4-a-failure-that-is-not-a-refusal-never-crashes-the-window)
   5. [Requirement 5: Recargo Terms and the Agency's Printed Details Come From Configuration](#requirement-5-recargo-terms-and-the-agencys-printed-details-come-from-configuration)
3. [Capability: cash-desk (NEW)](#capability-cash-desk-new)
   1. [Requirement 6: The Operator Finds an Account and Sees Whether It Is Ready](#requirement-6-the-operator-finds-an-account-and-sees-whether-it-is-ready)
   2. [Requirement 7: The Desk Previews What Is Owed Before Asking for Money](#requirement-7-the-desk-previews-what-is-owed-before-asking-for-money)
   3. [Requirement 8: The Handed Amount Is Typed and Checked Against the Ledger Figure](#requirement-8-the-handed-amount-is-typed-and-checked-against-the-ledger-figure)
   4. [Requirement 9: A Summary Is Confirmed Before Anything Is Committed](#requirement-9-a-summary-is-confirmed-before-anything-is-committed)
   5. [Requirement 10: A Stale or Concurrent Change Re-Previews and Shows What Changed](#requirement-10-a-stale-or-concurrent-change-re-previews-and-shows-what-changed)
   6. [Requirement 11: A Command Runs Once, and Not While Busy](#requirement-11-a-command-runs-once-and-not-while-busy)
   7. [Requirement 12: Nothing Prints Automatically](#requirement-12-nothing-prints-automatically)
   8. [Requirement 13: An Issued Recibo Is Found by Number or From the Recent Collections](#requirement-13-an-issued-recibo-is-found-by-number-or-from-the-recent-collections)
   9. [Requirement 14: A Recibo Prints on Demand, at Any Time, on the Paper the Operator Chooses](#requirement-14-a-recibo-prints-on-demand-at-any-time-on-the-paper-the-operator-chooses)
   10. [Requirement 15: A Voided Recibo Is Printed as Voided](#requirement-15-a-voided-recibo-is-printed-as-voided)
   11. [Requirement 16: Voiding Is Found by Printed Number, Reasoned, Previewed and Confirmed](#requirement-16-voiding-is-found-by-printed-number-reasoned-previewed-and-confirmed)
4. [Capability: go-live-setup (NEW)](#capability-go-live-setup-new)
   1. [Requirement 17: The Admin Sees Each Contract's Cutover State](#requirement-17-the-admin-sees-each-contracts-cutover-state)
   2. [Requirement 18: The Admin Sets a Contract's Cutover Date While It Is Still Changeable](#requirement-18-the-admin-sets-a-contracts-cutover-date-while-it-is-still-changeable)
   3. [Requirement 19: The Admin Sees and Sets the Recibo Starting Number](#requirement-19-the-admin-sees-and-sets-the-recibo-starting-number)
5. [Capability: contract-account (MODIFIED)](#capability-contract-account-modified)
   1. [Requirement 20 (MODIFIED): A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date](#requirement-20-modified-a-pre-existing-contract-accrues-nothing-due-on-or-before-its-cutover-date)
6. [Capability: access-control (MODIFIED)](#capability-access-control-modified)
   1. [Requirement 21 (MODIFIED): The Operation Rule Is Stated Over Actions, Not Screens](#requirement-21-modified-the-operation-rule-is-stated-over-actions-not-screens)
   2. [Requirement 22 (ADDED): The Cutover Date Is Admin-Only, Enforced by the Database](#requirement-22-added-the-cutover-date-is-admin-only-enforced-by-the-database)
7. [Capability: receipt (MODIFIED)](#capability-receipt-modified)
   1. [Requirement 23 (MODIFIED): Purpose Narrowed](#requirement-23-modified-purpose-narrowed)
   2. [Requirement 24 (ADDED): A Printed Recibo Says What the Issued Recibo Says](#requirement-24-added-a-printed-recibo-says-what-the-issued-recibo-says)
8. [Out of Scope](#out-of-scope)
9. [Parameters Left to Design](#parameters-left-to-design)
10. [Questions for Tomás](#questions-for-tomás)
11. [Preconditions for the later phases](#preconditions-for-the-later-phases)
12. [Tests](#tests)

---

## Capabilities covered

| Capability | Kind | Where its requirements are |
|---|---|---|
| `desktop-shell` | NEW | Requirements 1 to 5 |
| `cash-desk` | NEW | Requirements 6 to 16 |
| `go-live-setup` | NEW | Requirements 17 to 19 |
| `contract-account` | MODIFIED | Requirement 20 |
| `access-control` | MODIFIED | Requirements 21, 22 |
| `receipt` | MODIFIED | Requirements 23, 24 |
| `collection` | **Not modified** | Its Purpose ("does not present a screen", `collection/spec.md:3-8`) stays true of that capability; the screen is `cash-desk`. |
| `lease-contract` | **Not modified** | It holds no cutover requirement; see the note under Findings. |

Per the project's one-file rule, new capabilities are written as full requirements here and modified ones
as deltas. At archive, the new capabilities are promoted to `openspec/specs/<capability>/spec.md` and the
modified ones merge into their living specs.

---

## Capability: desktop-shell (NEW)

### Purpose

What the operator sees after logging in: a main window that knows who is logged in, offers the screens
that role may use, wires everything from the session, and survives failures that are not refusals. It
holds no business rule.

### Requirement 1: The Main Window Receives the Session and Shows Who Is Working

After a successful login and any forced password change, the main window MUST be built from that login's
session. It MUST show the user's display name and role, and MUST offer navigation to every screen that
role may use. It MUST NOT be reachable without a session, and it MUST NOT hold the database credential
beyond what the session already holds (`access-control`: no credential on disk).

Today the main window receives nothing (`App.xaml.cs:104`, `MainWindow.xaml:9-11`).

#### Scenario: The window shows the user and the role

- GIVEN an Admin "nicolas" has logged in
- WHEN the main window opens
- THEN it MUST show nicolas's display name and the role Admin

#### Scenario: A forced password change that is cancelled never reaches the main window

- GIVEN a login that requires a password change, and the change window is closed without success
- WHEN the bootstrap continues
- THEN no main window MUST be built (the existing behaviour, `App.xaml.cs:88-98`, MUST be unchanged)

### Requirement 2: Admin-Only Screens Are Hidden From an Empleado, and the Database Still Decides

The go-live screen (Requirements 17 to 19) MUST NOT be offered to an Empleado. Hiding it is a
convenience (Decision P12): an operation an Empleado reaches by any other means MUST still be refused by
the database (`access-control`). Voiding is **not** Admin-only (Empleado may void within 7 days) and MUST
be offered to both roles.

#### Scenario: An Empleado does not see the go-live screen

- GIVEN "maria" holds the Empleado role
- WHEN the main window opens
- THEN its navigation MUST NOT contain the go-live screen
- AND it MUST contain the cash desk, the recent collections and the void action

#### Scenario: An Admin sees every screen

- GIVEN an Admin
- WHEN the main window opens
- THEN its navigation MUST contain the go-live screen as well

#### Scenario: Hiding is not enforcement

- GIVEN an Empleado who reaches the go-live operation without the screen, by any means
- WHEN she submits a change of a cutover date
- THEN the database MUST refuse it (Requirement 22)

### Requirement 3: Post-Login Services Are Wired From the Session, One Database Context per Operation

Everything the screens use after login MUST be built from the session and its database factory in one
place, and that wiring MUST be provable by an automated test, as the pre-login wiring already is. Each
preview, commit, voiding and setup operation MUST use its own database context, because a context is not
safe to use from two operations at once. The wiring MUST NOT depend on an Admin-only privilege to build
for an Empleado.

#### Scenario: The wiring builds for each role

- GIVEN a session for an Admin and, separately, one for an Empleado
- WHEN the post-login services are built from each
- THEN every service a screen needs MUST resolve, without opening a connection at build time

#### Scenario: Two operations never share a context

- GIVEN two operations started one after the other from the same session
- WHEN each runs
- THEN each MUST have obtained its own database context from the factory

### Requirement 4: A Failure That Is Not a Refusal Never Crashes the Window

A refusal (a `CollectionRefusal`, a `VoidingRefusal`, or a setup refusal under Decision 31) is shown as
its operator text on the screen where it happened. Anything else the layers below raise (an unmapped
database error, "no account exists", missing header data, a lost connection) MUST be caught at one place
in the shell and shown as a generic message that does **not** include a connection string, a credential, a
stack trace or a database error text. The window MUST stay open, the operator's typed data MUST remain
on screen, and nothing MUST be retried silently. After a failure of a **commit** the operator MUST be
told that the outcome is unknown and MUST be shown how to check whether the payment was recorded (the
recent collections list), because a lost connection after sending a commit can have written it.

#### Scenario: An unmapped error becomes a generic message

- GIVEN the desk raises an exception that is not a refusal
- WHEN the operator is committing a payment
- THEN a generic error message MUST be shown
- AND it MUST NOT contain the exception's text
- AND the window MUST remain open with the typed values intact

#### Scenario: A refusal is not shown as a generic error

- GIVEN the desk refuses a commit as `RateNeverEntered`
- WHEN the result reaches the screen
- THEN the refusal's own operator text MUST be shown, not the generic message

#### Scenario: An uncertain commit points to the recent collections

- GIVEN a commit that ended in an exception
- WHEN the error is shown
- THEN the message MUST say the payment may or may not have been recorded
- AND MUST point to the recent collections as the way to check

### Requirement 5: Recargo Terms and the Agency's Printed Details Come From Configuration

The agency's fixed printed data (Decision 37: name, address, phone, email, tax-status text, the
rental-activity legend, the broker's name and matrícula, the logo) MUST also come from configuration or
shipped assets, never from constants in code, and its absence MUST stop printing with a message naming
the missing item (Requirement 14). It holds no secret.

The daily recargo rate and the grace days the desk uses MUST come from the application's configuration
(Decision P11), never from a constant. The shipped configuration MUST carry 0.02 and 0 days
(`RecargoTerms.cs:19-29`). A missing, non-numeric or out-of-range value (a rate below 0 or above 1,
negative grace days, per `RecargoTerms.cs:35-52`) MUST stop the cash desk from opening, with a message
naming the setting (Decision 33); the application MUST NOT fall back to any default. The configuration
holds no secret, and the existing guard that fails the build on anything credential-shaped in it MUST
keep passing.

#### Scenario: The shipped values are the agency's

- GIVEN the shipped configuration
- WHEN the post-login services are built
- THEN the desk's terms MUST be a daily rate of 0.02 and 0 grace days

#### Scenario: A missing setting stops the desk

- GIVEN a configuration with no recargo rate
- WHEN the operator opens the cash desk
- THEN it MUST NOT open
- AND the message MUST name the missing setting
- AND no preview MUST be attempted

#### Scenario: An out-of-range setting stops the desk

- GIVEN a configured daily rate of 2 (a misplaced decimal point)
- WHEN the services are built
- THEN the desk MUST NOT open, and no default MUST replace the value

---

## Capability: cash-desk (NEW)

### Purpose

Taking a tenant's payment from a screen and handing over the recibo, or voiding one by its printed
number. It owns the operator's flow only: every rule about what a payment or voiding is lives in
`collection` and `receipt`. It adds no rule to them, and it never computes an amount.

### Requirement 6: The Operator Finds an Account and Sees Whether It Is Ready

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

### Requirement 7: The Desk Previews What Is Owed Before Asking for Money

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

### Requirement 8: The Handed Amount Is Typed and Checked Against the Ledger Figure

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

### Requirement 9: A Summary Is Confirmed Before Anything Is Committed

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

### Requirement 10: A Stale or Concurrent Change Re-Previews and Shows What Changed

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

### Requirement 11: A Command Runs Once, and Not While Busy

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

### Requirement 12: Nothing Prints Automatically

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

### Requirement 13: An Issued Recibo Is Found by Number or From the Recent Collections

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

### Requirement 14: A Recibo Prints on Demand, at Any Time, on the Paper the Operator Chooses

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

### Requirement 15: A Voided Recibo Is Printed as Voided

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

### Requirement 16: Voiding Is Found by Printed Number, Reasoned, Previewed and Confirmed

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

---

## Capability: go-live-setup (NEW)

### Purpose

The two things go-live day requires and that only an Admin does: giving each pre-existing contract its
cutover date, and setting the number the first recibo will carry. The screen shows, for each, whether it
can still be changed, so that nobody discovers a lock by being refused.

### Requirement 17: The Admin Sees Each Contract's Cutover State

The go-live screen MUST list every contract (Decision 30) with its tenant and unit or units, its cutover
date or the fact that it has none, and whether the date can still be changed. The date is **changeable**
while the contract's account holds no movement, and **locked** from the first movement (`contract-account`:
"A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date"). A contract with no account
MUST be listed as not settable, with the reason. The screen MUST be available to an Admin only
(Requirement 2).

#### Scenario: A contract with no cutover date and no movement

- GIVEN a contract with no cutover date and an account with no movement
- WHEN the screen is shown
- THEN it MUST read "no cutover date" and changeable

#### Scenario: A contract whose date is set and unlocked

- GIVEN a cutover date of 5 October and no movement
- WHEN the screen is shown
- THEN it MUST show 5 October and changeable

#### Scenario: A locked contract

- GIVEN an account holding any movement
- WHEN the screen is shown
- THEN it MUST show the date (or none) and locked, and MUST NOT offer a change

#### Scenario: A contract with no account

- GIVEN a contract with no contract account
- WHEN the screen is shown
- THEN it MUST be listed as not settable, naming that it has no account

### Requirement 18: The Admin Sets a Contract's Cutover Date While It Is Still Changeable

The Admin MUST be able to set a contract's cutover date from the screen, and to correct it while the
contract's account holds no movement. The change MUST go through the contract's own domain operation
(`Contract.SetCutoverDate`, `Contract.cs:199-210`), so that the domain's rules apply: the date MUST NOT
be after the agency's today and MUST NOT be before the contract's start date (Decision 22; `contract-account`
below), and the account MUST hold no movement. The screen MUST show a refused value or a lock in operator
text (Decision 31) and MUST refresh the contract's state afterwards. There is no operation to clear a
cutover date.

#### Scenario: An Admin sets a date

- GIVEN a changeable contract and an Admin
- WHEN the Admin sets 5 October (the agency's today being 6 October or later)
- THEN the date MUST be stored and the screen MUST show 5 October

#### Scenario: A mistyped date is corrected before use

- GIVEN a date of 5 October set and no movement
- WHEN the Admin sets 6 October
- THEN it MUST be accepted

#### Scenario: A future date is refused

- GIVEN the agency's today is 20 October
- WHEN the Admin sets 21 October
- THEN it MUST be refused with operator text, and nothing MUST be stored

#### Scenario: A date before the contract's start is refused

- GIVEN a contract starting 1 March 2025
- WHEN the Admin sets 28 February 2025
- THEN it MUST be refused with operator text

#### Scenario: Today and the start date are accepted

- GIVEN a contract starting 1 March 2025 and today 20 October 2026
- WHEN the Admin sets 20 October 2026, and separately 1 March 2025
- THEN each MUST be accepted

#### Scenario: A locked contract refuses

- GIVEN an account holding a movement
- WHEN the Admin attempts to set a date, including when the screen's state is out of date
- THEN it MUST be refused with operator text saying the date is locked, and the screen MUST refresh to show locked

#### Scenario: A concurrent first movement is a refusal, not a crash

- GIVEN a contract that was changeable when the screen loaded, and a payment is then committed
- WHEN the Admin submits a date
- THEN the database's lock MUST be shown as a refusal and not as a generic error

### Requirement 19: The Admin Sees and Sets the Recibo Starting Number

The screen MUST show the state of the agency's recibo starting number: **not set**, **set to N and
changeable** (no recibo exists yet), or **set to N and locked** (a recibo exists, `receipt`: "The
Starting Number Is a Cutover Parameter, Set Before the First Recibo"). The Admin MUST be able to set it, and
correct it, while no recibo exists. The screen MUST refuse before calling the port anything that is not a
whole number of at least 1 and at most the largest `int` (Decision 32). The database's refusals (locked,
not Admin, below 1) MUST be shown as operator text (Decision 31). The state MUST be readable by both roles
(the cash desk's readiness depends on it, Requirement 6) while only an Admin can see the screen and set it.

#### Scenario: Not set

- GIVEN a counter whose starting number was never set
- WHEN the Admin opens the screen
- THEN it MUST read "not set" and changeable

#### Scenario: Set and changeable

- GIVEN a starting number of 31001 and no recibo
- WHEN the screen is shown
- THEN it MUST read 31001 and changeable

#### Scenario: Locked after the first recibo

- GIVEN a starting number of 31001 and one recibo issued
- WHEN the screen is shown
- THEN it MUST read 31001 and locked, and MUST NOT offer a change

#### Scenario: A mistyped number is corrected before use

- GIVEN 31001 set and no recibo
- WHEN the Admin sets 31100
- THEN it MUST be accepted, and the first recibo issued MUST carry 31100

#### Scenario: An impossible number is refused before the port

- GIVEN any state
- WHEN the Admin enters 0, -5, 12.5, text, or a number above the largest `int`
- THEN it MUST be refused with operator text and the port MUST NOT be called

#### Scenario: A locked number refused by the database is a refusal

- GIVEN a screen showing the number changeable, and a recibo is issued before the Admin submits
- WHEN the Admin submits a new number
- THEN the database's lock MUST be shown as operator text, not as a generic error

---

## Capability: contract-account (MODIFIED)

Only one living requirement changes. The block below is the full living requirement
(`contract-account/spec.md:548-598`) with the edit applied; at archive it replaces the living one.

### Requirement 20 (MODIFIED): A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date

The **cutover date** is stored on the contract. A contract loaded as one that predates the system
carries it from the moment it is loaded; a contract created in the system carries none. It MUST NOT be
derived from the account's movements: a pre-existing contract read before its opening balances are
typed would then have no cutover, and because reading an account (and the mora worklist, which reads
every account) materialises accruals onto an append-only ledger, its whole history would be written
permanently. The cutover date MUST NOT change once the account holds any movement (Decision 41).

**The value of a cutover date MUST NOT be after the agency's today and MUST NOT be before the
contract's start date.** A value outside those bounds MUST be refused and MUST leave the stored date
unchanged. **Only an Admin may set or change it** (`access-control`, Requirement 22).

(Previously: the rule refused only a change after a movement and accepted any date from anyone with
`UPDATE` on contracts.)

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

#### Scenario: A cutover date in the future is refused

- GIVEN the agency's today is 20 October
- WHEN a cutover date of 21 October is set on a contract whose account holds no movement
- THEN it MUST be refused and the stored date MUST be unchanged

#### Scenario: A cutover date before the contract's start is refused

- GIVEN a contract starting 1 March 2025
- WHEN a cutover date of 28 February 2025 is set
- THEN it MUST be refused and the stored date MUST be unchanged

#### Scenario: The bounds themselves are accepted

- GIVEN a contract starting 1 March 2025 and an agency's today of 20 October 2026
- WHEN a cutover date of 20 October 2026, and separately of 1 March 2025, is set
- THEN each MUST be accepted

#### Scenario: A cutover date cannot change after a movement

- GIVEN an account that holds any movement
- WHEN a change of the cutover date is attempted, by the domain or by raw SQL
- THEN it MUST be refused

---

## Capability: access-control (MODIFIED)

### Requirement 21 (MODIFIED): The Operation Rule Is Stated Over Actions, Not Screens

The block below is the full living requirement (`access-control/spec.md:9-35`) with the edit applied.

Empleado MUST own every operational action. Empleado MUST be excluded from exactly two **categories**:
system governance (creating, deactivating, or resetting users) and aggregate business reporting
(agency-level income, profitability, and totals). **There is one named, bounded departure: the two go-live
parameters, the starting recibo number and a contract's cutover date, are set by an Admin only**
(`receipt`: Decision 39 of `cobranza-y-recibos`; the cutover date: this change). It is a departure, not a
new category: it is not a reservation of "money-moving" or "financially sensitive" operations, which this
requirement continues to refuse, and it MUST NOT be read as licence to reserve any other operation.

(Previously: "Empleado MUST be excluded from exactly two things ... There is no third exclusion.", which
the starting-number rule already contradicted.)

No operation is reserved to Admin for being financially sensitive. Collecting rent, issuing a receipt, and confirming a rent adjustment are ordinary operational work that **both** roles perform. Empleado MUST retain the ability to see and act on contracts, tenants, owners, rents, receipts, and the honorarios percentage printed on a receipt she issues herself.

An earlier draft of this requirement also reserved to Admin "every operation that moves money or writes append-only history". That clause is **REMOVED, not merely unimplemented.** It was never decided by the agency, it contradicted the exclusion list standing beside it, and the two people who use this system do the same operational work. It MUST NOT be reintroduced by a reader who finds its absence surprising.

#### Scenario: Empleado performs ordinary operational work

- GIVEN "maria" holds the Empleado role
- WHEN she registers a new tenant, records a unit, or uploads a contract document
- THEN each operation MUST succeed

#### Scenario: Empleado is excluded from user governance

- GIVEN "maria" holds the Empleado role
- WHEN she attempts to create or deactivate another user
- THEN the operation MUST be refused

#### Scenario: Empleado is excluded from aggregate business reporting, not from operational data

- GIVEN "maria" holds the Empleado role
- WHEN she opens a contract to see its rent, its honorarios percentage, and its tenant
- THEN all of that MUST be visible to her
- AND WHEN she attempts to view agency-level aggregate income or profitability totals
- THEN that MUST be refused

#### Scenario: Empleado cannot set a go-live parameter

- GIVEN "maria" holds the Empleado role
- WHEN she attempts to set the starting recibo number or a contract's cutover date
- THEN each MUST be refused by the database
- AND she MUST still be able to collect rent, void within 7 days and end a contract

### Requirement 22 (ADDED): The Cutover Date Is Admin-Only, Enforced by the Database

Changing `contracts.cutover_date` MUST be refused by PostgreSQL unless the session's login role is a
member of `inmobiliaria_admin`, in addition to the existing rule that it cannot change once the account
holds a movement (`contracts_cutover_lock`, `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:153-173`).
The refusal MUST hold for raw SQL from any client, because `contracts` is granted `INSERT, UPDATE` to both
roles as a whole table (`20260924202549_AddUsersAndRoles.cs:161`) and a column grant cannot separate the
operation without removing the Empleado's ability to update the rest of the contract.

The refusal applies to a **change**: an `UPDATE` that names `cutover_date` without changing it, and an
`UPDATE` of any other column, MUST still succeed for an Empleado (ending a contract, entering a rate).
The migration that does this MUST NOT change the EF model; if design finds that it would, the model change
and the migration ship in one PR (project rule).

**A named limit** (Decision 23). The database cannot distinguish, in an `INSERT` of a contract, a cutover
date entered by an Empleado from one entered by an Admin, because this change hardens `UPDATE` only. An
Empleado can therefore insert a contract that carries a cutover date; she cannot change it afterwards.
This is stated so no reader assumes database enforcement that does not exist.

#### Scenario: An Admin changes the cutover date

- GIVEN a contract whose account holds no movement
- WHEN a session in `inmobiliaria_admin` updates its cutover date, directly or through the application
- THEN it MUST succeed

#### Scenario: An Empleado is refused by the database, not only hidden

- GIVEN "maria" holds `inmobiliaria_empleado` and connects with pgAdmin
- WHEN she executes an `UPDATE contracts SET cutover_date = ...` that changes the value
- THEN PostgreSQL MUST refuse it and the stored date MUST be unchanged

#### Scenario: An Empleado's other updates still work

- GIVEN a contract with a cutover date and "maria" as Empleado
- WHEN she ends the contract, or updates its honorarios percentage, or updates the contract with
  `cutover_date` named but unchanged
- THEN each MUST succeed

#### Scenario: The lock after a movement still holds for both roles

- GIVEN an account holding a movement
- WHEN an Admin, and separately an Empleado, changes the cutover date
- THEN both MUST be refused

#### Scenario: An Empleado can insert a contract with a date (the named limit)

- GIVEN "maria" as Empleado
- WHEN she inserts a new contract that carries a cutover date
- THEN the insert MUST succeed
- AND she MUST then be refused when she changes that date

#### Scenario: The migration reverses cleanly

- GIVEN the migration applied
- WHEN it is reverted
- THEN the previous trigger function MUST be restored, and no data MUST change

---

## Capability: receipt (MODIFIED)

### Requirement 23 (MODIFIED): Purpose Narrowed

The living Purpose (`receipt/spec.md:3-8`) ends with the sentence "It is a document, not a PDF: rendering
is `recibo-pdf`." It MUST read instead (the rest of the Purpose is unchanged):

> It is a document, not a PDF. Showing it on screen and printing it through the operating system's print
> dialog belong to `cash-desk`; producing a PDF file is `recibo-pdf`.

(Previously: all rendering belonged to `recibo-pdf`.) Held **medium** that this is a spec change rather
than wording only (proposal §3); it changes who may be asked to "render" a recibo, which is why it is
here and not only in the proposal.

#### Scenario: The Purpose names both

- GIVEN the living `receipt` spec after archive
- WHEN its Purpose is read
- THEN it MUST say that on-screen display and system printing are in `cash-desk` and that PDF is `recibo-pdf`

### Requirement 24 (ADDED): A Printed Recibo Says What the Issued Recibo Says

Whatever is displayed or printed of a recibo MUST be the recibo as stored: its number, copy, tenant,
lessors, units, period, received date, lines with kind and signed amount, total and non-fiscal legend.
Nothing on a display or a print MUST be recomputed from the contract, the rate, the ledger or the clock
(`receipt`: the stored honorarios line is never recomputed, Decision 46). A recibo's display or print MUST
add, beyond what is stored, only: the annulment mark and the annulment's date and reason when it has been
annulled (`cash-desk`, Requirement 15); the agency's fixed data from configuration; the amount in words
derived from the stored total; the caption ORIGINAL or DUPLICADO; and the paper's empty rows
(`cash-desk`, Requirement 14). Nothing else. A recibo that is annulled MUST be shown as
annulled in every read, including this one.

#### Scenario: A print equals the stored recibo

- GIVEN an issued recibo
- WHEN it is displayed and printed
- THEN each line, each amount and the total MUST equal the stored ones, and the total MUST equal the sum of the signed lines

#### Scenario: A later change to the contract does not reach a print

- GIVEN an issued recibo, and the tenant's name is later changed on the party record
- WHEN the recibo is reprinted
- THEN it MUST carry the tenant text as issued (the header is a snapshot, `ReciboHeader.cs:3-8`)

#### Scenario: A recibo number never appears twice with different content

- GIVEN a printed recibo and its reprint
- WHEN the two are compared
- THEN the number, lines and total MUST be identical

---

## Out of Scope

| Not here | Where it belongs |
|---|---|
| Loading parties, units, contracts, accounts and opening balances | Initial data load, its own change (proposal Decision 3) |
| A marker on the contract for "predates the system" | Initial data load (Q2) |
| PDF generation | `recibo-pdf` |
| A record of prints (who printed, how many times) | Not requested (Decision 20) |
| A layout editor, pre-printed stationery, a choice among designs | Not wanted (P14) |
| New recibo line kinds for Sellado, Imp. Inmobiliario, Aguas, Gas, Luz, Dep. en Garantía, Reserva, Expensas, Saldo a Favor | A later domain change, if Tomás wants them modelled (Q1) |
| Printing the tenant's or lessor's CUIT/CUIL | A later change that snapshots it on the header (Decision 38) |
| Ticking the I.V.A. boxes | They print unticked; the agency's VAT status is outside the system (`receipt`, non-fiscal legend) |
| Contract, party, unit and user screens | Later UI passes |
| Opening-balance entry | Initial data load |
| Owner liquidation and payout, notifications | `liquidacion-al-dueno`, `notificaciones` |
| The optional rounding option | A later additive change |
| De-duplicating `BuildPreview` and the accrual materialiser | Archive follow-up |
| Restricting reprint by age or role | Not wanted (Q3 answered) |
| Charging the difference of a late adjustment for the months already collected at the old canon (an operator choice when confirming it) | Its own later change: it amends `rent-adjustment` "A Late Adjustment Is Not Charged Retroactively" and adds a new charge to the account (Tomás, 2026-10-22) |
| Blocking an Empleado's INSERT of a contract with a cutover date | Named limit (Decision 23); initial data load decides how contracts are inserted |

## Parameters Left to Design

| Parameter | Note |
|---|---|
| Port names and shapes (account directory, recibo reader, go-live setup) | Requirements 6, 13, 17, 19 say what they must expose, including the annulment's reason, user and time (Finding F3) |
| How many entries the recent collections list holds, and its paging | Newest first by recorded time |
| Minimum search length and the result limit of the account picker | Requirement 6 states the matching, not the limits |
| The exact cutover value rule's lower bound | Decision 22; design MAY refine, MUST NOT admit a future date |
| How the cutover rule gets "today" and the contract's start | An argument or a clock, design's |
| Whether `CutoverNotSet` is retexted in Domain or mapped by the screen | Finding F1 |
| The constraint names of the new database refusals | Follow `contracts_cutover_locked` precedent |
| The legible minimum type size and the width at which two-up gives way to one half per page | Decision 19 |
| Where agency data and the logo live (settings keys, shipped asset) | Decision 37 fixes behaviour only |
| Whether the paper's row order or the catalog's governs the on-screen recibo | Paper order governs the printed sheet (Decision 35) |
| The Spanish number-to-words algorithm (a library or a small function) | Decision 36 and Tests 24b fix behaviour only |
| Where the print's service sits so tests can assert it was not called | A test seam, not behaviour |
| The wording of every operator message beyond the existing Spanish refusal texts | |
| The visual layout of the recibo and the captions of its two parts | Waits for the agency's sample (Open Item 2) |
| The settings' key names for the recargo terms | Decision 33 fixes behaviour only |

## Questions for Tomás

Ranked by how much they would change what is built. Only the first blocks anything.

1. **Answered 2026-10-22: (A), the full template**, as recommended; Decision 35 is now strong. CUOTA 1
   and 2 are read as the Luz EPE instalments (Decision 34, medium). The original question, kept for the
   record: **The paper's fixed rows. Blocks the print layout, not this spec.** The agency's paper lists seventeen
   rows whether or not they apply, but the system models only six of them (Decision 34); nine (Sellado,
   Imp. Inmobiliario, Aguas, Gas, Luz, Dep. en Garantía, Reserva, Expensas, Saldo a Favor) it knows
   nothing about, and one it does model (Seguro) has no row. Do we print (A) every row of the paper, with
   0 on modelled rows and an **empty** amount on unmodelled ones (Decision 35, my recommendation), or
   (B) only the lines the recibo actually has? A is faithful to the agency's own design; B never prints a
   row the system cannot fill. I do not recommend printing 0 on unmodelled rows: it states something the
   system does not know. Adding the nine kinds to the domain is a separate change either way. **Held
   medium.** While answering, please also say what CUOTA 1 and CUOTA 2 are (the tenant's honorarios
   contractuales instalments, or the Luz payment in instalments?). **Held weak** on my reading.
2. **Is the go-live screen enough to catch a forgotten cutover date?** The system cannot tell a contract
   that predates it from one created in it, so the cash desk can only *show* "no cutover date", not block
   (Decision 21, strong). A marker on the contract would let it block, but it is a data-model change that
   belongs to the initial data load. I recommend no marker now: the go-live list is the check. **Held
   medium.**
3. **Answered 2026-10-22: unlimited and unmarked**, as recommended; Decision 17 is now strong. The
   original question, kept for the record: **Should a reprint be limited, or marked?** As written, any operator can reprint any recibo of any age
   and a live one carries no mark (Decisions 17 and P13). The risk is a tenant handed a reprint as if it
   were new. I recommend leaving it unmarked, since the system cannot know which print was first and a
   voided one is marked anyway. **Held medium.**

## Preconditions for the later phases

| Phase | Docker | Supabase |
|---|---|---|
| design, tasks | No | No |
| Anything with `Inmobiliaria.Infrastructure.Tests` (directory, reader, setup, wiring, trigger) | **Yes** (a skipped Testcontainers test is an unrun test) | No |
| Applying the cutover migration to the live database | No | **Restored by Tomás, who applies it** after the merge |

## Tests

Numbered for this change; they will be renumbered when merged into the living specs. Where each runs is
in italics. `Presentation.Tests` runs on the Linux CI job with the ports faked; **Desktop cannot gate the
Linux job**, so nothing behavioural is placed there. Printing's paper and layout behaviour cannot be
unit-tested from a faked port and is checked by hand (tests M1 to M3), by Tomás, on a real printer or the
"Microsoft Print to PDF" printer, as part of verify.

**desktop-shell**

1. **The main window shows the display name and role of the session it was built from.**
   *(Presentation.Tests)*
2. **An Empleado's navigation has no go-live screen and has the desk, the recent list and voiding; an
   Admin's has all.** *(Presentation.Tests)*
3. **The post-login wiring resolves every screen's services for an Admin and for an Empleado, without
   opening a connection.** *(Infrastructure.Tests, beside `CompositionGuardTests`)*
4. **Two operations from one session obtain two database contexts.** *(Infrastructure.Tests)*
5. **An exception from the desk becomes a generic message with no exception text, leaves the window and the
   typed values in place, and a failed commit points to the recent collections.** *(Presentation.Tests)*
6. **A refusal is shown as its own operator text, not as the generic error.** *(Presentation.Tests)*
7. **The shipped configuration yields a rate of 0.02 and 0 grace days; a missing or out-of-range value
   stops the desk and names the setting; the credential-shape guard still passes on the configuration.**
   *(Infrastructure.Tests)*

**cash-desk**

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

**go-live-setup**

27. **The setup list shows each contract's cutover date or none, changeable or locked, and a contract with
    no account as not settable.** *(Infrastructure.Tests for the port; Presentation.Tests for the display)*
28. **Setting a cutover date through the setup port stores it; correcting it before any movement is
    accepted; after a movement it is refused as a refusal.** *(Infrastructure.Tests)*
29. **The starting number's state is not set, set and changeable, or set and locked, readable by both
    roles.** *(Infrastructure.Tests)*
30. **A starting number of 0, -5, 12.5, text or above the largest `int` is refused before the port is
    called; 31001 then 31100 before any recibo is accepted and the first recibo carries 31100.**
    *(Presentation.Tests for the refusal before the port; Infrastructure.Tests for the rest)*
31. **A database lock that arrives after the screen showed "changeable" is shown as a refusal, for the
    cutover date and for the starting number.** *(Presentation.Tests with the port raising the mapped
    refusal)*

**contract-account (modified)**

32. **A cutover date after the agency's today, before the contract's start, is refused and leaves the
    stored date unchanged; today and the start date themselves are accepted.** *(Domain.Tests)*
33. **The existing cutover scenarios (no history accrued, later period accrues, instalments, system-created
    contract, mora worklist before opening balances) still hold.** *(Domain.Tests, existing tests
    unchanged)*
34. **A cutover date cannot change after a movement, by the domain or by raw SQL, for either role.**
    *(Domain.Tests for the domain; Infrastructure.Tests for raw SQL)*

**access-control (modified)**

35. **An Admin changes the cutover date directly and succeeds; an Empleado changing it directly through a
    raw `UPDATE` is refused by the database. The refusing trigger must be seen failing with the Admin
    check removed before it is trusted.** *(Infrastructure.Tests)*
36. **An Empleado can still end a contract, update the honorarios percentage, and update a contract
    naming `cutover_date` without changing it.** *(Infrastructure.Tests)*
37. **The lock after a movement still refuses both roles.** *(Infrastructure.Tests)*
38. **An Empleado can insert a contract carrying a cutover date and then cannot change it (the named
    limit).** *(Infrastructure.Tests)*
39. **The migration's `Down` restores the previous function body.** *(Infrastructure.Tests)*
40. **`dotnet ef migrations has-pending-model-changes` reports no pending change after the migration.**
    *(Run by the assistant; needs no database or Docker)*

**receipt (modified)**

41. **A display and a print equal the stored recibo: lines, amounts and total (the total is the sum of the
    signed lines), the header as a snapshot, no recomputation from the clock or the contract.**
    *(Infrastructure.Tests for the reader; Presentation.Tests for the print model)*

**By hand, in verify (printing's paper behaviour; nothing here can run on CI)**

M1. **A4 landscape gives ORIGINAL left and DUPLICADO right on one page; A4 portrait and Carta give a
    layout with every number, line, amount, the words and the legend present and nothing clipped.**
    *(Tomás, real printer or Microsoft Print to PDF)*

M2. **A voided pair reprints with ANULADO, the date and the reason on both halves, and the numbers still
    readable under it.** *(Tomás)*

M3. **A commit and a voiding on a real run open no print dialog by themselves.** *(Tomás, in the live
    smoke if the database is awake)*
