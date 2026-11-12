# Spec: notificaciones (telling tenants what they owe, from the application)

Single-file specification for the `notificaciones` change. One capability is **NEW**: **`tenant-notices`**. Two are
**MODIFIED**: **`desktop-shell`** and **`access-control`**.

Vocabulary, glossed once: a **notice** is one email to the tenant of one contract about one month; the **sending day** is
the agency's today when the operator presses the button (`IAgencyClock`); the **notice month** is the month of the sending
day; the **sent record** is the stored row saying a notice went out; a **loaded contract** is one that predates the system,
which carries a `CutoverDate` (`src/Inmobiliaria.Domain/Leasing/Contract.cs:54-72`); the **desk figure** is what the cash
desk would charge for a payment received that day, `ContractAccount.AmountOwedForPayment`
(`src/Inmobiliaria.Domain/Accounts/ContractAccount.cs:310-318`).

The proposal is `openspec/changes/notificaciones/proposal.md`, merged as PR #112 on 2026-11-12. The exploration is beside
it. Rules already in `contract-account`, `access-control`, `user-registry` and `desktop-shell` are cited, not restated.

**What this spec does not do.** It states what must be true, observably. Class names, method signatures, the table's
columns, the SMTP library's calls, XAML and the exact Spanish wording of the email and the screen are design's. Where a
parameter is design's, the text says so.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table, rows 9 to 18 | The calls this spec had to make beyond the proposal. Each has a strength. The **weak** and **medium** rows are where you can change my mind. |
| **Findings** box right after the table | What the code does that the proposal did not expect. F3 changes how a failed email is handled. |
| Requirements 1, 2 and 4 | Who gets a notice, what it says, and the promise that nobody gets the same one twice. |

**Skip** the Tests section and "Parameters Left to Design" unless you want the detail. **Skim** the rest; the scenarios
are there so a test can be written from each one.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code or a living spec (path and line given).
**SPEC CALL** = this spec had to decide because the proposal did not. Strength: **strong**, **medium**, **weak** (act on
it only provisionally).

Rows P1 to P8 are the proposal's Decisions, in the proposal's numbering, approved by Tomás on 2026-11-12. Rows 9 onwards
are this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | Option C: the application sends, when an operator presses the button. | CONFIRMED BY USER, 2026-11-12 | Strong |
| P2 | One press sends the whole list. | CONFIRMED BY USER, 2026-11-12 | Strong |
| P3 | Gmail over SMTP with an app password. | Proposal §5 | Medium |
| P4 | The app password is stored per PC in Windows Credential Manager, typed once by an Admin. | Proposal §5 | Medium |
| P5 | The send library is MailKit. | Proposal §5 | Medium |
| P6 | The first kind of notice is "what you owe". | Proposal §5 | Medium |
| P7 | No scheduler. | Proposal §5 | Strong for this change |
| P8 | Before `carga-inicial`, notices go only to contracts created in the system. | Proposal §5 | Medium |
| 9 | **The notice's amounts are the desk figure for a payment received on the sending day**, not the Mora list's `AmountOwed`. | SPEC CALL. The two differ (Finding F1); the tenant pays at the desk, and a notice that disagrees with the receipt is worse than none (proposal §4.1) | Strong |
| 10 | **A contract gets a notice when its desk figure on the sending day is above zero.** An account that owes nothing gets none. | SPEC CALL. "What you owe" with nothing owed is noise. Reopen on N6 if Nicolás wants a monthly statement for everyone | Medium |
| 11 | **The notice goes to the contract's one current tenant**, never to codeudores or lessors. | VERIFIED FACT: a contract has exactly one current tenant (`Contract.cs:384-386`); removed parties are filtered out of every read (`ContractPartyConfiguration.cs:32`). Codeudores wait on N9 | Strong |
| 12 | **A loaded contract is one whose `CutoverDate` is set**, and it gets no notice in this change. | VERIFIED FACT, Finding F4. No new field is needed, which removes the proposal's §9 caveat | Strong on the test; Medium on excluding all of them |
| 13 | **An ended contract that still owes gets a notice**; a contract cancelled as loaded by mistake never does. | SPEC CALL. The Mora list keeps an ended contract that owes (`contract-account/spec.md:330-370`); the debt is real and the tenant can still be reached | Medium |
| 14 | **The sent record is append-only**: both roles insert and read it, nobody updates or deletes it, enforced by a trigger as `recibos` already are. Who sent it is stamped by the database (`current_user`), not passed by the application. | VERIFIED FACT, precedent: `20261015204506_AddCollectionsAndRecibos.cs:205-255`, `ReciboGrantsTests.cs` | Strong |
| 15 | **A notice's sent row and its email form one step: the row is written in a transaction that commits only after Gmail accepts the email, and rolls back if it does not.** This replaces proposal §4.2's "write, then remove the row on failure". | SPEC CALL, forced by Finding F3: removing a row contradicts Decision 14. The uniqueness rule still stops a second PC (Requirement 4) | Strong |
| 16 | **The email states the date its figures are for, and that recargo keeps growing each day after it.** | SPEC CALL. The surcharge is 2% a day (`mora` proposal §1); a figure without its date is wrong the next morning | Strong |
| 17 | **The screen lists apart the contracts with no usable tenant email**, with the tenant's name, so the operator knows whom to call. "Usable" means present and shaped like an address; whether it is read is N9. | SPEC CALL | Medium |
| 18 | **Only an Admin is offered the mailbox setup**; both roles send. | SPEC CALL, following P4. It is a **new departure** from `access-control`, whose only Admin-only operations besides user governance are "the two go-live parameters" (`access-control/spec.md:12-16`), and unlike those it **cannot be enforced by the database**: the password never reaches it. It is a screen gate only. **CONFIRMED BY USER**, 2026-11-12: Tomás kept it Admin-only, knowing it is a screen gate | Strong |

### Findings (the code differs from what was assumed)

| # | Finding | Consequence |
|---|---|---|
| F1 | **The cash desk and the Mora list use two different figures.** The desk charges `AmountOwedForPayment(receivedOn, terms)`; the Mora list shows `AmountOwed(asOf, terms)`, which "dates the principal too" (`ContractAccount.cs:310-318`). They can differ on a frozen account or an adjustment difference for a month not yet begun. | Decision 9: the notice uses the desk's. Proposal §2 said "as the cash desk shows it" and §1 cited the Mora list; this settles which. |
| F2 | **There is no read of "what this account owes today" outside a payment preview.** The desk offers `ICollectionDesk.PreviewAsync(CollectionRequest)` (`src/Inmobiliaria.Domain/Collection/ICollectionDesk.cs:47`), built for one payment. | Design reads the desk figure through the same `ContractAccount` method, never a copy of its formula (Requirement 2, Test 5). |
| F3 | **Proposal §4.2 removes the sent row when the email fails, and proposal §3 says nobody may delete a row of it.** Both cannot hold. | Decision 15. |
| F4 | **A loaded contract is already told apart**: `Contract.CutoverDate` is null for one created here and set for one that predates the system (`Contract.cs:54-72`); `AccountListing` already exposes it (`IAccountDirectory.cs:48`). | Decision 12. Proposal §9 feared a missing field; there is none to add. |
| F5 | **No read returns a contract's tenant as a party.** Tenant names are built in the internal `AccountListings.ReadAllAsync` (`src/Inmobiliaria.Infrastructure/Persistence/AccountListings.cs:26-81`), which the desk search and Mora share. | Requirement 3: the notice names the tenant with that same code, and design adds the email beside it. |
| F6 | **A test counts the tables both roles can read** (`RolePermissionTests.cs:42`, `AllThirteenTables_AreSelectableByBothRoles`). | The new table changes that count on purpose; the test is updated, not deleted. |

## Table of Contents

- Capabilities covered
- Capability: tenant-notices (NEW)
  - Requirement 1: Today's Notices Are Listed Before Anything Is Sent
  - Requirement 2: A Notice States the Desk's Figures, Dated
  - Requirement 3: A Notice Names Who Owes and Goes to the Current Tenant
  - Requirement 4: No Notice Is Sent Twice
  - Requirement 5: A Failed Email Leaves No Record and Says So
  - Requirement 6: The Mailbox Password Never Leaves Credential Manager
- Capability: access-control (MODIFIED)
  - Requirement 7: The Sent Record Is Append-Only, for Both Roles
- Capability: desktop-shell (MODIFIED)
  - Requirement 8: Both Roles Reach Avisos; Only an Admin Sets Up the Mailbox
- Out of Scope
- Parameters Left to Design
- Questions for Tomás
- Preconditions
- Tests

## Capabilities covered

| Capability | Status | What changes |
|---|---|---|
| `tenant-notices` | NEW | Which notices are due on a day, what they say, the sent record, sending through a mail port |
| `access-control` | MODIFIED | One more append-only table, readable and insertable by both roles |
| `desktop-shell` | MODIFIED | One more screen, for both roles, and the mailbox setup, for Admin |

## Capability: tenant-notices (NEW)

### Requirement 1: Today's Notices Are Listed Before Anything Is Sent

Opening the screen MUST list, for the sending day, one notice per contract that:

- has a desk figure above zero on the sending day (Decision 10);
- is not a loaded contract (Decision 12);
- is not cancelled as loaded by mistake (Decision 13);
- has not already been sent a notice of this kind for the notice month (Requirement 4).

Contracts that meet the first three conditions but whose tenant has no usable email MUST be listed apart, with the
tenant's name (Decision 17). Notices already sent for the notice month MUST be shown as sent, with who sent them and when,
and MUST NOT be offered again. The operator MAY remove any row before sending; a removed row is not sent and not recorded,
and is offered again the next time the screen is read. Reading the list MUST NOT send anything and MUST NOT write a sent
row. Like the Mora list, reading brings each account up to date first (`mora` spec, Decision 10).

#### Scenario: A contract that owes is listed

- GIVEN a contract created in the system whose tenant has an email
- AND its desk figure on 3 December is 520,000
- WHEN the screen is read on 3 December
- THEN a notice for that contract MUST be listed with 520,000

#### Scenario: A contract that owes nothing is not listed

- GIVEN a contract whose desk figure on the sending day is 0
- WHEN the screen is read
- THEN no notice for that contract MUST be listed

#### Scenario: A loaded contract is not listed

- GIVEN a contract with a `CutoverDate`, owing on the sending day
- WHEN the screen is read
- THEN no notice for that contract MUST be listed, in either list

#### Scenario: No email is listed apart

- GIVEN a contract that owes and whose tenant has no email
- WHEN the screen is read
- THEN it MUST appear in the "no email" list with the tenant's name
- AND it MUST NOT appear among the notices to send

#### Scenario: A removed row is not sent

- GIVEN three listed notices
- WHEN the operator removes one and presses send
- THEN exactly two emails MUST be sent and two sent rows written
- AND reading the screen again MUST list the removed one

### Requirement 2: A Notice States the Desk's Figures, Dated

A notice's total MUST equal the desk figure for a payment received on the sending day (Decision 9), computed by the same
`ContractAccount` code the cash desk uses, never by a second formula. When that total includes recargo, the notice MUST
name the recargo apart, with the figure the desk would charge as recargo that day, and MUST state the days overdue of the
oldest unpaid period, counted as the Mora list counts them (`mora` spec, Requirement 2). It MUST NOT present the total
and the recargo as two amounts to add.

The notice MUST state the date its figures are for, that recargo keeps growing each day after that date (Decision 16), and
what the figures cover and do not cover, in the words the Mora screen uses ("The Balance Names What It Covers",
`contract-account`). The exact Spanish text is design's.

#### Scenario: A frozen account's notice states the frozen figure

- GIVEN an account frozen by a novación
- WHEN its notice is built
- THEN its total MUST equal the desk figure, which projects no further recargo

#### Scenario: The notice agrees with the desk

- GIVEN accounts covering every movement kind
- WHEN their notices are built on a day
- THEN each total and recargo MUST equal the cash desk's preview for a payment of the full amount that day

#### Scenario: The date is stated

- GIVEN a notice built on 3 December
- THEN its text MUST state 3 December as the date of its figures
- AND MUST say that recargo grows after it

### Requirement 3: A Notice Names Who Owes and Goes to the Current Tenant

A notice MUST be addressed to the contract's current tenant's email (Decision 11), and MUST name the tenant and the units
in the same text the cash desk search shows for that account (Finding F5). It MUST NOT be sent to a codeudor, a lessor, or
a tenant removed from the contract. It MUST be sent from the agency's configured Gmail address.

#### Scenario: A replaced tenant is not written to

- GIVEN a contract whose first tenant was removed and replaced by a second
- WHEN its notice is built
- THEN it MUST be addressed to the second tenant only

#### Scenario: Same name as the desk

- GIVEN any listed notice
- THEN its tenant and units MUST equal the cash desk search result for that account

### Requirement 4: No Notice Is Sent Twice

The database MUST refuse a second sent row for the same contract, kind and notice month. This MUST hold when two PCs press
send for the same notice at the same moment: exactly one email goes out and exactly one row is written. A notice refused
because it was already sent MUST be reported as "already sent", not as a failure, and MUST NOT send an email.

#### Scenario: Two PCs at once

- GIVEN one listed notice, read on two PCs
- WHEN both press send at the same moment
- THEN exactly one email MUST be sent
- AND exactly one sent row MUST exist
- AND the other PC MUST report it as already sent

#### Scenario: The next month is a new notice

- GIVEN a notice sent for November
- WHEN the screen is read on 3 December and the contract still owes
- THEN a notice for December MUST be listed

### Requirement 5: A Failed Email Leaves No Record and Says So

A notice's sent row MUST be committed only after Gmail accepts its email, and MUST NOT exist when Gmail refuses it or
cannot be reached (Decision 15). A failure on one notice MUST NOT stop the others. After a press, the screen MUST report
how many were sent, how many were already sent, and each one that failed with its reason. Nothing MUST be reported as sent
that was not. No exception may escape to the user.

When the mailbox is not set up, the send button MUST be disabled with that reason, and the list MUST still be readable.

#### Scenario: Gmail refuses one of three

- GIVEN three listed notices, and the mail port refuses the second
- WHEN the operator presses send
- THEN the first and third MUST be sent and recorded
- AND the second MUST have no sent row and MUST be reported with its reason
- AND reading the screen again MUST list the second

#### Scenario: No mailbox set up

- GIVEN a PC where no app password is stored
- WHEN the screen is opened
- THEN the list MUST be shown
- AND sending MUST be disabled with the reason

### Requirement 6: The Mailbox Password Never Leaves Credential Manager

The app password MUST be stored only in Windows Credential Manager on that PC (P4). It MUST NOT be written to a file, an
environment variable, the database, the repository, or the error log, and MUST NOT appear in any exception message the
application logs. The Gmail address is not secret and is configured where design says.

#### Scenario: A failed login to Gmail does not log the password

- GIVEN a stored app password that Gmail rejects
- WHEN a send fails for that reason
- THEN the error log MUST NOT contain the password

## Capability: access-control (MODIFIED)

### Requirement 7: The Sent Record Is Append-Only, for Both Roles

Both `inmobiliaria_admin` and `inmobiliaria_empleado` MUST hold exactly `SELECT` and `INSERT` on the sent record, and
neither MAY update or delete a row of it; a trigger MUST refuse an update or delete as `recibos` are refused (Decision 14).
The row MUST record who sent it as the database's `current_user`, stamped by the database, never a value the application
supplies.

#### Scenario: An Empleado cannot erase a sent notice

- GIVEN a sent row
- WHEN `inmobiliaria_empleado` tries to delete or update it
- THEN the database MUST refuse

#### Scenario: Who sent it cannot be forged

- GIVEN an insert that supplies another user's name as the sender
- THEN the stored sender MUST be the inserting `current_user`

## Capability: desktop-shell (MODIFIED)

### Requirement 8: Both Roles Reach Avisos; Only an Admin Sets Up the Mailbox

Both roles MUST reach the notices screen from the menu. With invalid recargo settings, the entry MUST be shown disabled
with the same reason as Caja de cobranza and Mora (`mora` spec, Decision 11), because the desk figure needs them. Only an
Admin MUST be offered the screen to enter or replace the Gmail address and app password (Decision 18); an Empleado MUST
NOT be offered it. This gate is the application's only: the database never holds the password, so it cannot refuse an
Empleado, and the spec does not claim it does.

#### Scenario: An Empleado sends but cannot set up

- GIVEN an Empleado's session on a PC with the mailbox set up
- THEN the notices screen MUST be reachable and sending enabled
- AND the mailbox setup MUST NOT be offered

#### Scenario: Invalid settings disable it with the reason

- GIVEN invalid recargo settings
- THEN the notices entry MUST be disabled with the same reason as Caja de cobranza

## Out of Scope

| Not here | Why |
|---|---|
| Sending without an operator pressing the button | P7; question N11 for Nicolás |
| Notices of a contract about to end, an adjustment applied, or rent about to fall due | P6; N6 |
| Notices on loaded contracts | P8, Decision 12; until `carga-inicial` |
| Codeudores and guarantors as recipients | Decision 11; N9 |
| Editing a notice's text before sending | Not requested; a wrong figure is fixed at the account, not in the email |
| Attaching a PDF of the account | Not requested |
| Retrying failed notices on their own | The operator presses again; P7 |
| WhatsApp, SMS, notices to owners | Proposal §2 |

## Parameters Left to Design

- The sent record's table and column names, the notice kind's stored value, and how the notice month is stored.
- How the desk figure and its recargo are read for every listed account in one pass (Finding F2), as long as they are the
  same `ContractAccount` methods the desk calls.
- How the tenant's email is added to what `AccountListings` reads (Finding F5).
- What "shaped like an address" checks (Decision 17), beyond present and non-blank.
- Where the Gmail address is configured, and the Credential Manager entry's name.
- How sending runs off the screen's thread and shows progress.
- The view-model's name, its states, and where Avisos sits in the navigation beside S12's entries and Mora's.
- The exact Spanish text of the email and of every message, and the screen's layout, which waits for Mateo's theme.

## Questions for Tomás

None open. Tomás approved this spec on 2026-11-12. Decisions 10, 13 and 17 are where a different preference of yours
would change it. The questions for
Nicolás stay in the exploration and the proposal (N5 to N11).

## Preconditions

| Phase | Docker | Supabase |
|---|---|---|
| This spec, design, tasks | No | No |
| T1 (notice, sent record and migration, today's list) | **Yes** | No |
| T2 (mail port, MailKit adapter, Credential Manager) | **Yes** for the full suite | No |
| T3 (view-model) | **Yes** for the full suite | No |
| T4 (view and mailbox setup, with Mateo's batch) | **Yes** for the full suite | No |
| Applying the migration to the test database | No | **Yes**, restored; Tomás applies it |
| A real send from Gmail | No | **Yes**; Tomás, with a test Gmail account, never the agency's |

## Tests

Each names the requirement it proves and the project it lives in. Infrastructure tests run against `postgres:17.6`
through Testcontainers; a skipped one is an unrun test. Tests 1 and 9 are **presence** assertions, so they cannot pass on
an empty list. No test sends a real email: the mail port is faked.

1. **A created contract that owes, with a tenant email, is listed with its desk figure.** R1 *(Infrastructure.Tests)*
2. **A contract owing nothing, a loaded contract, and a contract cancelled as loaded by mistake are not listed; an ended
   contract that owes is.** R1 *(Infrastructure.Tests)*
3. **A contract that owes with no tenant email is in the "no email" list with the tenant's name, and not among the
   notices.** R1 *(Infrastructure.Tests)*
4. **Reading the list writes no sent row and calls the mail port zero times.** R1 *(Infrastructure.Tests)*
5. **For accounts covering every movement kind, including a frozen one, each notice's total and recargo equal the cash
   desk's preview for a full payment that day.** R2 *(Infrastructure.Tests)*
6. **A notice's text states its date, says recargo grows after it, names the coverage, and never shows total and recargo
   as a sum.** R2 *(Domain.Tests)*
7. **A notice is addressed to the current tenant only: not a removed tenant, a codeudor or a lessor; its names equal the
   desk search's.** R3 *(Infrastructure.Tests)*
8. **A second sent row for the same contract, kind and month is refused by the database.** Proven failing with the
   uniqueness rule removed. R4 *(Infrastructure.Tests)*
9. **Two concurrent sends of one notice produce exactly one row and one call to the mail port; the other reports "already
   sent".** R4 *(Infrastructure.Tests)*
10. **A notice sent in November does not hide December's.** R4 *(Infrastructure.Tests)*
11. **When the mail port refuses one of three, the other two are recorded, the refused one has no row and is listed
    again.** R5 *(Infrastructure.Tests)*
12. **With no stored password, the list loads and sending is disabled with the reason.** R5 *(Presentation.Tests)*
13. **A rejected app password does not appear in the error log or any logged exception message.** R6
    *(Infrastructure.Tests)*
14. **Both roles hold exactly `INSERT,SELECT` on the sent record; neither can update or delete; the sender is
    `current_user` whatever the insert supplies.** R7 *(Infrastructure.Tests, beside `ReciboGrantsTests`)*
15. **The table count in `RolePermissionTests` is raised by one, and both roles can read the new table.** R7
    *(Infrastructure.Tests, existing)*
16. **Both roles reach Avisos; with invalid recargo settings it is disabled with Caja de cobranza's reason; only an Admin
    is offered the mailbox setup.** R8 *(Presentation.Tests, beside `ShellViewModelTests`)*
17. **After a press, the view-model reports sent, already sent and failed counts, and a removed row is not sent.** R1, R5
    *(Presentation.Tests)*
18. **The notices query resolves from the session's services with the configured recargo terms, and is not offered when
    they are invalid.** R8 *(Infrastructure.Tests, beside the existing composition guards)*

Verification, beyond the tests: `dotnet ef migrations has-pending-model-changes` is clean after the migration; Tests 8
and 9 are shown failing with the uniqueness rule removed; and Tomás sends one real notice from a test Gmail account to
himself and checks the figures against the cash desk.
