# Proposal: notificaciones (telling tenants what they owe, from the application)

Written 2026-11-12, from `openspec/changes/notificaciones/exploration.md`. Vocabulary: a **notice** is one email to the
tenants of one contract about one month; **sending day** is the day the operator presses the button; **the sent
record** is the stored row that says a notice went out.

Rules come from the `contract-account`, `access-control`, `user-registry` and `desktop-shell` specs. This proposal cites
them; it does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope, §5 Decisions and §10 Questions for Nicolás. **Skim** §4 Approach and §9
Size. **Skip** §6 to §8 and §11 unless you want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Decisions (with how strongly each is held)
6. Affected Areas
7. Risks
8. Rollback Plan
9. Size Forecast
10. Questions for Nicolás
11. Success Criteria

---

## 1. Intent

Today the agency tells a tenant what they owe by writing an email by hand from its Gmail account, usually when the tenant
asks (exploration, N6 as Tomás recalls it). Every figure in that email already exists in the system: the cash desk shows
the balance, and the Mora list (PRs #110 and #111) shows who is overdue, by how long, and how much of it is recargo.

After this change, an operator opens one screen, sees the notices the system would send today, removes any that look
wrong, and sends all of them **with one press**, from the agency's own Gmail address.

This is option C of the exploration, and **Tomás decided on 2026-11-12 that it is built whatever Nicolás answers**.
Sending without anyone pressing anything is not part of this change; whether the agency wants it is question N11 (§10),
and how it could be added on top is recorded in the exploration, "Mixing C with automatic sending".

**Success, in one line:** the agency sends the month's notices in one press instead of one handwritten email per tenant,
and no tenant gets the same notice twice.

---

## 2. Scope

### In Scope

- **One kind of notice: what the tenant owes.** The balance as the cash desk shows it, and, when the account is in mora,
  the days overdue and how much of the total is recargo, as `MoraAccount` already names them. Other kinds wait on N6.
- **A screen listing today's notices**, one row per contract with a tenant email, each row removable before sending.
- **Sending through the agency's Gmail**, behind a port, faked in tests.
- **The sent record:** which notice went to which contract for which month, by which operator and when. It stops a second
  operator, or a second press, from sending the same notice again, and shows the operator what already went out.
- **The mailbox setup:** where the Gmail app password is typed and kept (Decision 4).
- **Contracts with no tenant email** are listed apart, so the operator knows who still needs a call.
- **The view**, built from Mateo's theme in the same batch as `gestion-de-alquileres` S12, like Mora's.

### Out of Scope

| Not here | Where it goes, and why |
|---|---|
| Sending without an operator pressing the button | N11. If Nicolás wants it, a later change adds it on top of this one: the sent record built here is what stops it sending twice |
| Notices of a contract about to end, an adjustment applied, or rent about to fall due | N6. Each is a different trigger and text; none was asked for |
| Debt notices on contracts loaded before go-live | Wait on `carga-inicial`: until opening balances are loaded, those debts read wrong (exploration, "What exists") |
| Codeudores and guarantors | N9 |
| WhatsApp or SMS, notices to owners | Nobody asked; owners are `liquidacion-al-dueno` |
| A scheduler of any kind | `scheduler-mechanism` closes as "no scheduler" if C stands alone (§5, Decision 7) |

---

## 3. Capabilities

### New Capabilities

- **`tenant-notices`**: which notices are due on a day, the sent record, and sending through a mail port.

### Modified Capabilities

- **`desktop-shell`**: one more screen, for both roles, and the mailbox setup.
- **`access-control`**: both roles may read and write the sent record; nobody may change or delete a row of it.

---

## 4. Approach

### 4.1 The figures are read, never computed again

The notice's amounts come from the same code the cash desk and the Mora list already use. No new formula for a balance or
a recargo is written for an email. A notice that disagrees with the receipt the tenant is handed at the desk would be worse
than no notice.

### 4.2 The sent record is the guard, in the database

A uniqueness rule on (contract, kind, month) in the database, not a check in the application. Two PCs pressing the button
at once must not both send: an application check reads, then writes, and both PCs can read "not sent" before either
writes. The database rule makes the second insert fail.

Order of operations, per notice: write the sent row, then send the email. If the email fails, the row is removed and the
failure shown. The other order (send, then write) leaves a sent email with no row when the write fails, and the next press
sends it again.

### 4.3 Mail through a port

`ITenantNoticeSender` in the domain, an SMTP adapter in infrastructure. The suite fakes the port, so tests send nothing.
The adapter speaks to Gmail's SMTP server with an app password (Decision 3).

### 4.4 Logic now, the view with Mateo's batch

Same rule as `gestion-de-alquileres` §4.2 and `mora` §4.4: the port, the sent record, the query and the view-model do not
depend on how the screen looks, so they are built and tested now; the XAML waits for the theme.

---

## 5. Decisions

| # | Decision | Held | Why |
|---|---|---|---|
| 1 | Option C: the application sends, when an operator presses the button | **Firm** | Tomás, 2026-11-12. Adds no secret to the database and needs no change to `user-registry` (exploration, "Recommendation") |
| 2 | One press sends the whole list, not one press per email | **Firm** | Tomás: one press per email "se hace muy extenso" (exploration, N6) |
| 3 | Gmail over SMTP with an app password | **Moderate** | The agency uses Gmail (N8, as Tomás recalls). An app password needs 2-Step Verification on that account: to confirm when the account is set up. OAuth would avoid the app password but needs a Google Cloud project, far more setup for one mailbox |
| 4 | The app password is stored per PC in Windows Credential Manager, typed once by an Admin | **Moderate** | Typing it every session would push people back to Gmail by hand. It is a mailbox password, not a database credential, so `user-registry`'s rule against stored database credentials does not apply; Credential Manager encrypts it for that Windows user. Reopen if the agency prefers it typed per session |
| 5 | The send library is MailKit | **Moderate** | Microsoft documents `System.Net.Mail.SmtpClient` as not recommended for new development and points to MailKit. A new dependency: high-risk item 5, so it gets the verifier |
| 6 | The first kind of notice is "what you owe", not "rent about to fall due" | **Moderate** | It is the email the agency writes by hand today. Reopen on N6 |
| 7 | No scheduler | **Firm for this change** | If N11 says yes, a later change adds automatic sending. Nothing built here has to be undone for it |
| 8 | Before `carga-inicial`, notices go only to contracts created in the system, not loaded ones | **Moderate** | A loaded contract's debt reads wrong until its opening balances exist. How a loaded contract is told apart is a spec decision |

---

## 6. Affected Areas

| Area | Files |
|---|---|
| Domain | a new `Notices/` folder: the notice, the port, the sent-record port |
| Infrastructure | the SMTP adapter, the sent-record table, the query of today's notices; `Access/SessionServices.cs` |
| Migrations | one: the sent-record table, its uniqueness rule and its GRANTs for both roles |
| Presentation | a new `Notices/` folder; `Shell/NavigationEntry.cs`, `Shell/ShellViewModel.cs` |
| Desktop | the view and the mailbox setup, with Mateo's batch |
| Tests | `tests/Inmobiliaria.Domain.Tests/`, `tests/Inmobiliaria.Infrastructure.Tests/`, `tests/Inmobiliaria.Presentation.Tests/` |
| Specs | `tenant-notices` (new), `desktop-shell`, `access-control` |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| **A tenant gets the same notice twice** | The database uniqueness rule (§4.2), proven by two concurrent sends in a Testcontainers test, watched failing without the rule |
| **A notice states a wrong amount** | Figures read from the cash desk's own code (§4.1); the operator sees every row before sending; loaded contracts excluded until `carga-inicial` (Decision 8) |
| **The app password leaks** | Kept in Credential Manager, never in a file or the repository, never logged. The error log must not print it: the same lesson as the login passwords before #81 |
| **Gmail refuses or limits sending** | 500 messages a day on a personal account, against about a hundred contracts. A failed send removes its row and shows the error; nothing is reported as sent that was not |
| **The new table's GRANTs open more than intended** (high-risk item 2) | Both roles insert and read; nobody updates or deletes. Tested per role, as the existing GRANT tests do |
| **Navigation entries collide with S12 and Mora** | The entry lands after S12's and Mora's, or they are written together |

---

## 8. Rollback Plan

One migration adds a table and touches no existing one. Reverting the PR and running the migration's `Down` removes the
screen and the table; no other stored data changes. The sent rows are lost with it, which only matters if notices were
already sent from production.

---

## 9. Size Forecast

Advisory, about 400 authored lines per slice.

| Slice | Content | Estimate |
|---|---|---|
| T1 | The notice, the sent record with its migration and GRANTs, the query of today's notices | ~350 |
| T2 | The mail port, the MailKit adapter, the Credential Manager store | ~250 |
| T3 | The view-model and its tests | ~250 |
| T4 | The view and the mailbox setup, with Mateo's batch | ~200 |

T1 to T3 now, one PR each; T4 with S12. Moderately held: Decision 8 may need a field that does not exist yet.

Decision needed before apply: No (C is decided; N6 to N11 only widen it later)
Chained PRs recommended: Yes
400-line budget risk: Medium

---

## 10. Questions for Nicolás

N5 to N10 are in the exploration, with what Tomás recalls. One more, for the meeting:

| # | Question | Why it matters |
|---|---|---|
| N11 | **Would it work for you if the notices went out on their own, without anyone pressing a button?** For example on the 1st of the month, or on the due day saying the 2% starts from then. And on days nobody opens the system? | Decides whether a later change adds automatic sending on top of this one, and whether it must run in the cloud (exploration, "Mixing C with automatic sending") |

---

## 11. Success Criteria

- The screen lists one notice per contract with a tenant email, with the figures the cash desk shows for that account.
- One press sends every listed notice; a removed row is not sent.
- A notice already sent for that month is shown as sent and is never sent again, even with two PCs pressing at once.
- A failed email leaves no sent row and shows the failure.
- Contracts with no tenant email are listed apart.
- The app password never appears in a file, the repository or the error log.
- Both roles reach the screen from the menu.
- Full suite green with Docker running, no skipped tests.
