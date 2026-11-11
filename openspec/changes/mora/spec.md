# Spec: mora (the overdue-accounts worklist)

Single-file specification for the `mora` change. No capability is **NEW**. Two are **MODIFIED**: **`contract-account`** and
**`desktop-shell`**.

Vocabulary, glossed once: an account is **in mora** when it holds an unpaid period past its due date; an **opening-balance
month** is a month a pre-existing contract owed at go-live, typed as an `OpeningBalance` movement with that period; a
**difference-only period** is a period whose only unpaid charge is a late adjustment's difference (`AdjustmentDifference`);
the **worklist** is `IMoraWorklistQuery` and the screen built on it; **Mora** is that screen.

The proposal is `openspec/changes/mora/proposal.md`, merged as PR #108 on 2026-11-11. The exploration is beside it. Rules
already in `contract-account`, `access-control` and `desktop-shell` are cited, not restated.

**What this spec does not do.** It states what must be true, observably. Class names, method signatures, the query's shape,
XAML and the exact Spanish wording are design's. Where a parameter is design's, the text says so.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table, rows 8 to 14 | The calls this spec had to make beyond the proposal. Each has a strength. The **weak** and **medium** rows are where you can change my mind. |
| **Findings** box right after the table | What the code does that you may not expect. F1 is the reason this change exists. |
| Requirements 1 and 3 | The hole and the promise that closing it changes no figure the agency already sees. |

**Skip** the Tests section and "Parameters Left to Design" unless you want the detail. **Skim** the rest; the scenarios
are there so a test can be written from each one.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code or a living spec (path and line given).
**SPEC CALL** = this spec had to decide because the proposal did not. Strength: **strong**, **medium**, **weak** (act on
it only provisionally).

Rows P1 to P7 are the proposal's Decisions, in the proposal's numbering, approved by Tomás on 2026-11-11. Rows 8 onwards
are this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | Its own small change, not folded into `gestion-de-alquileres` S12. | Proposal §5 | Medium |
| P2 | An opening-balance month counts its days from that month's due date on the contract, computed by the existing `AccrualSchedule.DueDateFor`. Its recargo stays as the cash desk shows it: none projected. | Proposal §4.2; `cobranza-y-recibos` Decision 31 | Medium |
| P3 | The fix lives in the worklist; `ContractAccount` is not changed. | Proposal §5 | Strong |
| P4 | Days overdue count from the due date itself, not after grace days. | Proposal §5; `appsettings.json` `Recargo:GraceDays` is 0 | Strong |
| P5 | Both roles see the list. | `access-control/spec.md:22` | Strong |
| P6 | The list shows; it warns nobody and changes no figure. | Proposal §5 | Strong |
| P7 | A row opens that account at the cash desk. | Proposal §5 | **Weak** |
| 8 | **Each row names the tenant, the units, the contract's short id and the contract's status**, in the same text the cash desk's account search shows for that account. | SPEC CALL. `AccountListing` already builds these (`src/Inmobiliaria.Domain/Collection/IAccountDirectory.cs:23-40`); two screens naming one account differently would confuse the operator. Status matters because an ended contract can still owe (`MoraWorklistQueryTests.cs:349`) | **Medium** on the columns |
| 9 | **The list is always computed for the agency's today** (`IAgencyClock`). Asking it about another day is not supported. | SPEC CALL. `OldestUnpaidPeriod()` ignores the date it is asked about (Finding F4). Every caller this change and `notificaciones` foresee asks about today, so fixing it would touch `ContractAccount` for no user | Strong |
| 10 | **Opening the list brings every account up to date first**, writing any rent or instalment already due, as reading one account at the cash desk does. | VERIFIED FACT: required by `contract-account` "Accounts in Mora Are Queryable" and implemented (`MoraWorklistQuery.cs:40-48`). Both roles already hold the grants it uses | Strong |
| 11 | **With invalid recargo settings, the Mora entry is shown disabled with the reason**, as Caja de cobranza and Anular un cobro already are. | VERIFIED FACT, precedent: `ShellViewModel.cs:33-45`, citing design D16. The list's recargo needs the same terms | Strong |
| 12 | **An empty list says in words that no account is in mora**, never shows a blank table. | SPEC CALL. A blank table cannot be told apart from a list that failed to load | Medium |
| 13 | **The list is read when the screen opens and again when the operator asks**; it does not refresh on its own. | SPEC CALL. Reading writes accruals and takes a second or two (Finding F5); a timer would do that all day for nobody | Medium |
| 14 | **Ties in days overdue are broken by tenant name**, not by the contract id the query uses today (`MoraWorklistQuery.cs:69-72`). | SPEC CALL. An id order is meaningless to an operator; the query has no other consumer yet, so nothing depends on the old order | **Weak** |

### Findings (the code differs from what was assumed)

| # | Finding | Consequence |
|---|---|---|
| F1 | **An account whose oldest unpaid period has no rent accrual drops off the list entirely.** The row is built only from a `RentAccrual` on that period (`MoraWorklistQuery.cs:90-96`). An opening-balance month never gets one (`ContractAccount.cs:478-481`, applied by `AccountMaterialiser.cs:73-77`), and neither does a period holding only a difference. Found by reading code; no test seeds either case. | Requirement 1. Test 1 is written first and must fail against the current code; if it passes, this Finding is wrong and the spec is revised before anything else. |
| F2 | **A row carries only ids** (`MoraAccount.cs`): no name, no unit. | Requirement 4. |
| F3 | **The worklist does not load the contract**, only accounts and their movements (`MoraWorklistQuery.cs:50-55`), so it has no due day to compute an opening-balance month's due date from. | Design adds the contract's due day to what the query reads. |
| F4 | **`ContractAccount.OldestUnpaidPeriod()` ignores the date asked about** (`ContractAccount.cs:364-372`), while the balance honours it. Asked about a past day, the two would describe different moments. | Decision 9. Recorded so nobody relies on a past-date answer. |
| F5 | **The worklist is linear in accounts**: it brings each one up to date before reading (`archive/2026-10-10-cuenta-corriente/archive-report.md:96-98`). About a second or two at ~100 contracts. | Decision 13; the screen shows that it is loading. Making it cheaper is out of scope. |

## Table of Contents

- Capabilities covered
- Capability: contract-account (MODIFIED)
  - Requirement 1: Every Overdue Account Appears, Whatever Its Oldest Unpaid Period Holds
  - Requirement 2: Days Overdue Count From That Period's Due Date
  - Requirement 3: The Worklist's Figures Are the Cash Desk's Figures
  - Requirement 4: Each Row Names Who Owes
- Capability: desktop-shell (MODIFIED)
  - Requirement 5: Both Roles Reach the Mora List
  - Requirement 6: The List Is Read Fresh, Worst First, and Says What It Covers
  - Requirement 7: A Row Opens Its Account at the Cash Desk
- Out of Scope
- Parameters Left to Design
- Questions for Tomás
- Preconditions
- Tests

## Capabilities covered

| Capability | Status | What changes |
|---|---|---|
| `contract-account` | MODIFIED | "Accounts in Mora Are Queryable, and the List Is Reminder-Ready" (`spec.md:330`) gains the opening-balance month and the difference-only period, the identifying fields, and the date it is computed for. Its three existing scenarios stand unchanged. |
| `desktop-shell` | MODIFIED | One more screen, for both roles, under the existing rule that invalid settings disable an entry with its reason. |
| `access-control` | **Not modified** | Both roles already perform this work (`spec.md:22`), and the reads and accrual writes it needs are already granted to both. |

---

## Capability: contract-account (MODIFIED)

### Requirement 1: Every Overdue Account Appears, Whatever Its Oldest Unpaid Period Holds

An account MUST appear on the worklist whenever its oldest unpaid period is past its due date, whichever charge that
period holds: a rent accrual, an opening-balance month, a difference, or any combination. An account MUST NOT be left out
because that period has no rent accrual. This closes Finding F1.

The existing rules stand: an account with no unpaid period does not appear, a cancelled contract's account drops off when
its balance reaches zero, and an ended contract that still owes stays (`contract-account/spec.md:330-370`).

#### Scenario: An opening-balance month at the front of the debt

- GIVEN a pre-existing contract with due day 10, loaded with opening-balance months August and September, both unpaid
- AND October's and November's rent accrued and unpaid
- WHEN the worklist is read on 20 November
- THEN the account MUST appear
- AND its oldest unpaid period MUST be August

#### Scenario: A difference-only period

- GIVEN an ended contract whose only unpaid charge is a difference on its last period
- WHEN the worklist is read after that period's due date
- THEN the account MUST appear

#### Scenario: An opening-balance month that is paid does not keep the account on the list

- GIVEN an account whose opening-balance months are all paid and with no other unpaid period
- WHEN the worklist is read
- THEN the account MUST NOT appear

### Requirement 2: Days Overdue Count From That Period's Due Date

Days overdue MUST be counted from the oldest unpaid period's due date to the agency's today (Decision 9), and MUST be
greater than zero for the account to appear. The due date MUST be:

- for a period holding a rent accrual or a difference, that movement's own date, which is already its due date;
- for an opening-balance month, that month's due date on the contract, computed by the same rule rent uses
  (`AccrualSchedule.DueDateFor(period, contract.DueDay)`), never by a second formula and never from the date the balance
  was typed.

Grace days MUST NOT shorten the count (P4). On the due date itself the account is not yet in mora.

#### Scenario: An opening-balance month counts from its own due date

- GIVEN the account of Requirement 1's first scenario
- WHEN the worklist is read on 20 November
- THEN its oldest unpaid due date MUST be 10 August
- AND its days overdue MUST be 102

#### Scenario: A contract with another due day

- GIVEN the same account on a contract with due day 5
- WHEN the worklist is read on 20 November
- THEN its oldest unpaid due date MUST be 5 August

#### Scenario: The due date itself is not mora

- GIVEN an account whose only unpaid period falls due today
- WHEN the worklist is read today
- THEN the account MUST NOT appear

### Requirement 3: The Worklist's Figures Are the Cash Desk's Figures

For every account it lists, the worklist's ledger balance, amount owed and recargo MUST equal what the cash desk reads for
that account on the same day with the same terms. An opening-balance month MUST contribute no projected recargo
(`cobranza-y-recibos` Decision 31); later periods keep theirs. A frozen account MUST still report its frozen figure.

Closing Finding F1 MUST NOT change any figure the cash desk, a receipt or a voiding shows for any account (P3).

#### Scenario: An opening-balance month adds no recargo of its own

- GIVEN the account of Requirement 1's first scenario
- WHEN the worklist is read
- THEN its recargo MUST be the recargo of October's and November's rent only
- AND its amount owed MUST equal the cash desk's amount owed for that account on that day

#### Scenario: The fix changes no existing figure

- GIVEN accounts covering every movement kind
- WHEN their balances are read before and after this change
- THEN every figure MUST be identical

### Requirement 4: Each Row Names Who Owes

Each row MUST carry the tenant, the units, the contract's short id and the contract's status, in the same text the cash
desk's account search shows for that account (Decision 8), beside the figures it already carries. A reminder built on the
worklist MUST be able to name the debtor without reading anything else.

#### Scenario: A row names the tenant as the cash desk does

- GIVEN an account in mora whose tenant is "Prueba, Maria"
- WHEN the worklist is read
- THEN its row MUST carry "Prueba, Maria", its units and its contract's short id, identical to the cash desk's search
  result for that account

#### Scenario: An ended contract says so

- GIVEN an ended contract that still owes
- WHEN the worklist is read
- THEN its row MUST carry the ended status

---

## Capability: desktop-shell (MODIFIED)

### Requirement 5: Both Roles Reach the Mora List

The navigation MUST offer Mora to Admin and Empleado alike (P5). With invalid recargo settings, the entry MUST be shown
disabled with the same reason Caja de cobranza shows (Decision 11), and the list MUST NOT be computed with any default.

#### Scenario: An Empleado opens Mora

- GIVEN "maria" holds the Empleado role and the settings are valid
- WHEN she looks at the navigation
- THEN Mora MUST be offered and available

#### Scenario: Invalid settings disable it with the reason

- GIVEN the recargo settings are invalid
- WHEN either role looks at the navigation
- THEN Mora MUST be shown disabled, naming the setting to fix

### Requirement 6: The List Is Read Fresh, Worst First, and Says What It Covers

The screen MUST read the worklist when it opens and again when the operator asks (Decision 13), never patch it from what it
showed before, and MUST show that it is loading meanwhile. Rows MUST be ordered by days overdue, most first, then by tenant
name (Decision 14). Each row MUST show the tenant, units, short id, status, days overdue, amount owed and recargo, and a
frozen account MUST show the date its recargo was frozen.

The screen MUST say what the figures cover and what they do not: rent, instalments and recargo; never tasa municipal,
seguros or otros conceptos (`contract-account` "The Balance Names What It Covers", `spec.md:450`). It MUST NOT add amount
owed and recargo together: the first already includes the second (`MoraAccount.cs`).

An empty list MUST say that no account is in mora (Decision 12). A failure reading it MUST show a message and leave the
window open, as `desktop-shell` already requires of every screen.

#### Scenario: Worst first

- GIVEN accounts overdue by 5, 38 and 200 days
- WHEN Mora opens
- THEN the 200-day account MUST be first and the 5-day account last

#### Scenario: Nobody is in mora

- GIVEN no account in mora
- WHEN Mora opens
- THEN it MUST say so in words

#### Scenario: Reading fails

- GIVEN the database cannot be reached
- WHEN Mora opens
- THEN it MUST show a message and the window MUST stay open

### Requirement 7: A Row Opens Its Account at the Cash Desk

Choosing a row SHOULD open that account at the cash desk (P7). This requirement is **weak**: design MAY drop it if
opening the desk on a given account needs more than the navigation already offers, and says so in its Findings.

#### Scenario: From the list to the account

- GIVEN Mora lists an account
- WHEN the operator chooses its row
- THEN the cash desk MUST open on that account

---

## Out of Scope

| Not here | Why |
|---|---|
| Sending any notice, or deciding who is warned | `notificaciones`; waits on `scheduler-mechanism` and `carga-inicial` |
| The list as of a past day | Decision 9, Finding F4 |
| Making the query cheaper | Finding F5 |
| Contact details on the row | Not requested; the tenant's data lives with the person |
| Any change to a balance, recargo, receipt or `ContractAccount` | P3, P6 |
| Printing or exporting the list | Not requested |

## Parameters Left to Design

- How the query reads the contract's due day and the identifying text, as long as Requirement 4's text is the cash desk's.
- Whether `MoraAccount` gains the fields or a second type carries them.
- The view-model's name, its loading state and its refusal handling.
- Where Mora sits in the navigation relative to `gestion-de-alquileres` S12's five entries.
- How the cash desk is opened on a given account (Requirement 7), or the Finding that drops it.
- The exact Spanish text of every message, and the screen's layout, which waits for Mateo's theme.

## Questions for Tomás

None open. Decisions 8, 12, 13 and 14 are where a different preference of yours would change the spec.

## Preconditions

| Phase | Docker | Supabase |
|---|---|---|
| This spec, design, tasks | No | No |
| M1 (the hole, identifying fields, registration, view-model) | **Yes** | No |
| M2 (the view, with Mateo's batch) | **Yes** for the full suite | No |
| verify | **Yes** | No. Optional: Tomás opens Mora on the test database |

## Tests

Each names the requirement it proves and the project it lives in. Infrastructure tests run against `postgres:17.6` through
Testcontainers; a skipped one is an unrun test. Tests 1 and 2 are **presence** assertions: an absence test cannot catch
this class of bug, because a broken list also returns absence (`archive/2026-10-10-cuenta-corriente/archive-report.md:145-147`).

1. **An account whose oldest unpaid periods are opening-balance months, followed by unpaid rent, appears with August as its
   oldest unpaid period.** Written first; must fail against the current code (Finding F1). R1 *(Infrastructure.Tests)*
2. **An ended contract whose only unpaid charge is a difference appears.** R1 *(Infrastructure.Tests)*
3. **An account whose opening-balance months are all paid, with nothing else unpaid, does not appear.** R1
   *(Infrastructure.Tests)*
4. **An opening-balance month's due date is that month's due day on the contract: 10 August with due day 10, 5 August with
   due day 5; 102 days overdue on 20 November.** R2 *(Infrastructure.Tests)*
5. **An account whose only unpaid period falls due today does not appear; the next day it appears with 1 day.** R2
   *(Infrastructure.Tests)*
6. **For accounts covering every movement kind, the row's ledger balance, amount owed and recargo equal the cash desk's for
   the same account and day; an opening-balance month adds no recargo.** R3 *(Infrastructure.Tests)*
7. **The existing worklist tests pass unchanged**: rent-only rows, the frozen figure, an ended contract that owes, a
   cancelled contract dropping off, cutover respected. R1, R3 *(Infrastructure.Tests, existing)*
8. **A row's tenant, units and short id equal the cash desk search result for the same account, and its status is the
   contract's.** R4 *(Infrastructure.Tests)*
9. **Both roles see Mora; with invalid recargo settings it is disabled with the same reason as Caja de cobranza.** R5
   *(Presentation.Tests, beside `ShellViewModelTests`)*
10. **The view-model orders rows by days overdue, then tenant name; shows the frozen date when there is one; never shows
    amount owed plus recargo as a sum; and carries the coverage text.** R6 *(Presentation.Tests)*
11. **An empty list produces the "no account in mora" state; a failing read produces a message and no exception.** R6
    *(Presentation.Tests)*
12. **Refreshing reads the port again and replaces every row.** R6 *(Presentation.Tests)*
13. **Choosing a row asks the shell to open the cash desk on that account**, unless design drops Requirement 7. R7
    *(Presentation.Tests)*
14. **The worklist resolves from the session's services with the configured recargo terms, and is not offered when they are
    invalid.** R5 *(Infrastructure.Tests, beside the existing composition guards)*

Verification, beyond the tests: a before-and-after dump of every cash desk figure for a set of accounts covering every
movement kind matches byte for byte (Requirement 3), as S10's verifier did; and each of Tests 1, 2 and 4 is shown failing
when the fix is reverted.
