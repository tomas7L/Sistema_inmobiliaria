# Design: notificaciones (telling tenants what they owe, from the application)

Technical design for the `notificaciones` change, from `spec.md` (Requirements 1 to 8, Decisions P1 to P8 and 9 to 18,
Tests 1 to 18, approved by Tomás on 2026-11-12, merged as PR #113) and `proposal.md` in this folder. Written 2026-11-12.
Nothing here is code; nothing here edits the spec. Every existing type, member and file named was read or mapped and is
cited `path:line`; anything new is marked **(new)**. Paths use `Domain` = `src/Inmobiliaria.Domain/`, `Infra` =
`src/Inmobiliaria.Infrastructure/`, `Pres` = `src/Inmobiliaria.Presentation/`, `Desk` = `src/Inmobiliaria.Desktop/`.

**How complete the audit was.** Read in part: `Domain/Accounts/ContractAccount.cs` (`AmountOwedForPayment`,
`RecargoProjectedForPayment`), `Domain/Collection/CollectionPlanner.cs` (the preview and the charged recargo),
`Infra/Persistence/AccountListings.cs`, `Infra/Persistence/MoraWorklistQuery.cs`, `Infra/Access/SessionServices.cs`, the
migrations `20261015204506_AddCollectionsAndRecibos` and `20261107152256_AddClauseWindowOffset`,
`Pres/Common/FileErrorLog.cs`, `Domain/Access/SecretMask.cs`, `Pres/Shell/ShellViewModel.cs`, `Desk/App.xaml.cs`. External
facts verified against their sources, cited where used. **Not run:** no build and no test.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** rows are where you can change my mind |
| **§6 Findings** | What the code does that the spec did not say. **F2: every production table is granted to Supabase's API roles**, outside this change, verified with your query. Not usable today because the Data API is off |
| **§7 Slices** | Three PRs now, one with Mateo's screens, and what each needs |

**Skip** §1 to §5 unless you want the detail: they are for whoever implements and for the second inspection.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | **Three ports in `Domain/Notices/` (new)**: `ITenantNoticeDesk` reads today's board and sends a batch; `ITenantNoticeSender` sends one email; `IMailboxCredentialStore` reads and saves the Gmail address and app password. Pure interfaces, so the Linux CI tests everything but the Windows store | Strong |
| D2 | **The total is `AmountOwedForPayment`; the recargo is that total minus a new `LedgerForPayment`**, extracted from `AmountOwedForPayment` without changing what it returns (`Domain/Accounts/ContractAccount.cs:318-343`). One formula, two readers | Strong |
| D3 | **Days overdue come from the Mora list itself**: the notice desk calls `IMoraWorklistQuery` once per read and takes each account's `DaysOverdue`. That call also brings every account up to date, so the desk does not do it a second time | Medium |
| D4 | **Table `tenant_notices` (new)**, unique on `(contract_id, kind, notice_month)`, append-only by its own trigger, stamped `recorded_at`/`recorded_by` by the database, `GRANT SELECT, INSERT` to both roles, and revoked from Supabase's API roles | Strong |
| D5 | **Sending connects and logs in to Gmail once, before touching the database.** If the login fails, every notice fails with that reason and no row is ever written. Then one transaction per notice: insert, send, commit; any failure rolls back | Strong |
| D6 | **A second PC's insert waits on the first's open transaction, then fails as a unique violation** (`23505` on `ux_tenant_notices_contract_kind_month`), mapped to "already sent", as `CollectionDesk.cs:444,456` maps its own | Strong |
| D7 | **The MailKit adapter never lets the password out**: every exception is caught, its text has the password removed, and only then is it returned as a failure. `SecretMask` does not cover a bare app password (Finding F4) | Strong |
| D8 | **Gmail's server is fixed in code**: `smtp.gmail.com`, port 587, STARTTLS, 30-second timeout. Nothing in `appsettings.json` | Medium |
| D9 | **The store is `Meziantou.Framework.Win32.CredentialManager`**, in `Desk` only: one generic credential named `SistemaInmobiliaria/Gmail`, user name the address, secret the app password, persisted for that Windows user | Medium |
| D10 | **The email is plain text, built by `NoticeComposer` (new) in `Domain/Notices/`**, with the agency's name from `AgencyDetails`. Its Spanish wording is a placeholder until Tomás reads one real notice | Medium |
| D11 | **"Avisos" is one navigation entry for both roles**; the mailbox setup is a button inside it, shown to an Admin only (spec Decision 18) | Medium |
| D12 | **Loaded contracts are left out by `AccountListing.CutoverDate is not null`** (`Domain/Collection/IAccountDirectory.cs:48`); a contract cancelled as loaded by mistake, by `AccountListingEntry.LoadedByMistake` (`Infra/Persistence/AccountListings.cs:12`) | Strong |

## Table of Contents

1. Technical approach
2. Domain
3. Infrastructure
4. Presentation, the shell and the Windows store
5. Testing strategy
6. Findings
7. Slices, sizes and test mapping
8. Threats and rollback
9. Parameters the spec left to design
10. Questions

---

## 1. Technical approach

Reading is the Mora list plus the desk figure. One call to `IMoraWorklistQuery.ReadAllAsync(today)` brings every account
up to date and gives the days overdue (D3). The desk then reads the accounts with their movements and the listings
(`AccountListings.ReadAllAsync`, `Infra/Persistence/AccountListings.cs:26-27`), computes each account's total and recargo
with the desk's own `ContractAccount` methods (D2), filters (spec Requirement 1, D12), reads the notices already sent for
the month, and returns a board of three lists: to send, no email, already sent.

Sending runs per notice inside its own transaction (D5): the insert takes the uniqueness rule's lock, the email goes out,
and only then does the transaction commit. A second PC sending the same notice blocks on that insert until the first
commits (then fails as a duplicate) or rolls back (then proceeds). That is Postgres's ordinary behaviour for a unique index
with an uncommitted conflicting row, and is what spec Test 9 proves.

The password never enters `Infra` through configuration or DI. The view-model reads it from the store and passes it with
the batch, so `Infra` is testable on Linux and never touches Windows.

## 2. Domain

### 2.1 `Domain/Notices/` **(new)**

- `NoticeKind` — one value, `AmountOwed`. Stored as the text `amount_owed`.
- `TenantNotice` — one notice ready to send: account id, contract id, contract short id, tenant name, units, recipient
  address, figures date, total, recargo, days overdue (zero when not in mora), notice month (the 1st of the sending day's
  month).
- `NoticeBoard` — `ToSend`, `NoEmail` (tenant name, units, short id), `AlreadySent` (short id, tenant, recorded by,
  recorded at).
- `MailboxCredentials` — address and app password. `ToString()` is overridden to print the address only.
- `NoticeSendOutcome` — `Sent`, `AlreadySent`, or `Failed` with a reason text already scrubbed (D7).
- `NoticeSendReport` — one outcome per notice, plus the counts spec Requirement 5 asks the screen to show.
- `ITenantNoticeDesk` — `Task<NoticeBoard> ReadAsync(CancellationToken)`;
  `Task<NoticeSendReport> SendAsync(IReadOnlyList<TenantNotice>, MailboxCredentials, CancellationToken)`.
- `ITenantNoticeSender` — opens one authenticated session for a batch and sends one `OutgoingMail` at a time; a failed
  login is an outcome, not an exception.
- `IMailboxCredentialStore` — `MailboxCredentials? Read()`, `void Save(MailboxCredentials)`.
- `NoticeComposer` — pure: `TenantNotice` plus the agency's name, to subject and body. Covers spec Requirement 2's text:
  the date, "recargo grows after it", the coverage sentence in the Mora screen's words, recargo named apart and never
  added to the total.

### 2.2 `ContractAccount.LedgerForPayment` **(new)**, `Domain/Accounts/ContractAccount.cs`

The first half of `AmountOwedForPayment` (`:322-329`, the ledger without differences for a month not yet begun),
extracted as a public method. `AmountOwedForPayment` then calls it; its result does not change. The notice's recargo is
`AmountOwedForPayment − LedgerForPayment`. That is zero on a frozen account (`:331-334`), whose recargo is already a
movement inside the ledger. The cash desk's existing tests are what prove the extraction changed nothing.

## 3. Infrastructure

### 3.1 `TenantNoticeEntry` and its configuration **(new)**, `Infra/Persistence/`

Table `tenant_notices`: `id` uuid, `contract_id` uuid FK to `contracts`, `kind` text, `notice_month` date, `recipient`
text, `figures_on` date, `total` numeric(14,2), `recargo` numeric(14,2), `recorded_at` timestamptz, `recorded_by` name.
Unique index `ux_tenant_notices_contract_kind_month`, declared with `.IsUnique().HasDatabaseName(...)` as
`Infra/Persistence/Configurations/AccountMovementConfiguration.cs:111-113` does. Snake case comes from the context
(`InmobiliariaDbContext.cs:74`).

### 3.2 Migration `AddTenantNotices` **(new)**

After `20261110150305_AddAdjustmentDifference`. Raw SQL, mirroring `20261015204506_AddCollectionsAndRecibos.cs`:

- `reject_tenant_notice_mutation()` **(new)**: its own function rather than the recibo one, whose name says recibo
  (`:210-229`); same `P0001`, `CONSTRAINT = 'tenant_notices_append_only'`. Trigger `BEFORE UPDATE OR DELETE`.
- `stamp_tenant_notice()` **(new)**: `NEW.recorded_at := now(); NEW.recorded_by := current_user;`, not SECURITY
  DEFINER, for the reason `:233-236` gives.
- `GRANT SELECT, INSERT ON tenant_notices TO inmobiliaria_admin, inmobiliaria_empleado;`
- The Supabase API roles revoked from the table, guarded so the migration runs where those roles do not exist, as
  `20261107152256_AddClauseWindowOffset.cs:125-135` does for a function.
- `Down` drops the table and both functions.

### 3.3 `TenantNoticeDesk` **(new)**, `Infra/Persistence/`

`ReadAsync` as §1. `SendAsync`:

1. Open the sender's session with the credentials. If the login fails, every notice gets `Failed` with that reason; the
   database is never touched.
2. Per notice, in order: begin a transaction; insert the row; on `23505` with the constraint above, roll back and record
   `AlreadySent`; otherwise send; on success commit and record `Sent`; on any failure roll back and record `Failed`.
3. A failure on one notice never stops the next (spec Requirement 5).

### 3.4 `MailKitTenantNoticeSender` **(new)**, `Infra/Mail/`

MailKit's `SmtpClient` to `smtp.gmail.com:587` with `SecureSocketOptions.StartTls`, `Timeout = 30_000` (D8). It wraps
MailKit behind an internal `ISmtpTransport` seam, so spec Test 13 can throw an exception carrying the password without a
real server. Every exception is caught; its `ToString()` has every occurrence of the password replaced by `***`, then is
passed through `SecretMask.Mask` (`Domain/Access/SecretMask.cs:11-27`), and only that text leaves the adapter (D7).
The recipient is checked with `MailboxAddress.TryParse` while the board is read, which is what "shaped like an address"
means (spec Decision 17).

Microsoft documents its own `SmtpClient` as "not recommended for new development" and points to MailKit
([SmtpClient](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient)). MailKit is added to
`Directory.Packages.props:12-25`.

### 3.5 Session registration, `Infra/Access/SessionServices.cs`

`PerCallTenantNoticeDesk` **(new)** in `Infra/Persistence/PerCall/`, as `PerCallMoraWorklistQuery.cs:10-24`. Registered
inside the `recargo.Terms is { } terms` block (`SessionServices.cs:62-69`), so invalid settings leave it unregistered,
exactly as the desk and Mora are. The sender is a singleton outside that block.

## 4. Presentation, the shell and the Windows store

### 4.1 `NoticesViewModel` **(new)**, `Pres/Notices/`

Takes the desk, the store, the role and the error log. Reads on open and on demand, like Mora (`mora` spec Decision 13).
Rows can be removed before sending; a removed row is only left out of this press. Send is disabled with a reason when the
store returns nothing. After a press it shows sent, already sent and failed counts, with each failure's reason, then
re-reads. Errors go through the existing runner, so nothing escapes to the user. `NoticesTexts` **(new)** follows
`Pres/Mora/MoraTexts.cs`.

### 4.2 `MailboxSetupViewModel` **(new)**, `Pres/Notices/`

Address and app password, saved through the store. Built only for an Admin, as `UsersViewModel` is
(`Desk/App.xaml.cs:146-151`). It never shows a stored password back; it says only whether one is stored, and for which
address.

### 4.3 Shell, `Pres/Shell/ShellViewModel.cs`

`NavigationTarget.Notices` **(new)**, title "Avisos", for both roles, disabled with the recargo reason when the settings are
invalid, as Caja de cobranza is (`:43,45`). Placed after Mora's entry, which lands with S12 (mora design D8).

### 4.4 `CredentialManagerMailboxStore` **(new)**, `Desk/`

Created directly in `App.xaml.cs` and handed to the view-models, as `WpfClipboard` is (`Desk/App.xaml.cs:150`), not
registered in the session's services (D9). `Desk` is outside `Inmobiliaria.Core.slnf`, so this class has no CI test: it is
kept to the package's read and write calls, and is checked by hand at handover (§5).

## 5. Testing strategy

- **Domain.Tests**: `NoticeComposer` (spec Test 6); `LedgerForPayment` plus recargo equal `AmountOwedForPayment` for
  every movement kind, including frozen.
- **Infrastructure.Tests**, Testcontainers `postgres:17.6`: the board (Tests 1 to 5, 7); the uniqueness rule, shown
  failing with it removed (Test 8); two concurrent sends through two connections with a fake sender that waits until both
  are in flight (Test 9); the next month (Test 10); one refusal of three (Test 11); the scrubbed password (Test 13); grants
  and the stamp, beside `ReciboGrantsTests` (Test 14); `RolePermissionTests.AllThirteenTables_AreSelectableByBothRoles`
  becomes fourteen (Test 15); composition, beside `SessionServicesTests.cs:103,157` (Test 18). The API-role revoke is
  proven only because `PostgresFixture` creates those roles (Engram #218); without them the guarded revoke is skipped and
  proves nothing.
- **Presentation.Tests**: Tests 12, 16 and 17, with fake desk and store.
- **By hand at handover**: the Windows store saves and reads on this PC; Tomás sends one real notice from a test Gmail
  account to himself and compares it with the cash desk.
- `dotnet ef migrations has-pending-model-changes` clean after the migration.

## 6. Findings

| # | Finding | Consequence |
|---|---|---|
| F1 | **The desk shows one figure and charges a slightly lower one.** `AmountOwedForPayment` adds the recargo untruncated, but each payment charges it truncated to whole pesos (`CollectionPlanner.cs:217` versus `:447`). The difference is under one peso per unpaid month. | The notice states the figure the desk shows (spec Decision 9). Recorded so nobody reads the cents as a bug in the notice. |
| F2 | **Every table in production is fully granted to Supabase's API roles.** No migration revokes tables from `anon`, `authenticated` or `service_role`; the only revokes are for functions (`AddClauseWindowOffset.cs:125-135`, Engram #218). **Verified 2026-11-12** by the §10 query on production: all 23 tables, `__EFMigrationsHistory` and `app_users` included, hold `SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER` for all three roles. Supabase documents this as its default for new tables in `public`, and says a table without Row Level Security "can be accessed by any role with matching grants" ([Securing your API](https://supabase.com/docs/guides/api/securing-your-api)). No table has it (`access-control/spec.md:99`). While the Data API is on, whoever holds the project's public API key can read and change every table through it. **It is off in both projects**: Tomás checked on 2026-11-12, after a reload, that **Enable Data API** is off in production and in the test project, and says he never changed it. `anon` and `authenticated` reach the database only through that API, so today the grants cannot be used from outside. The key is not in the repository or its history either (searched 2026-11-12). | Outside this change. Not exploitable today, but one switch away from it: whoever turns the Data API on reopens every table. Lasting fix, held medium: its own change before go-live, revoking the tables and the default privileges. This change revokes its own table regardless. |
| F3 | **The Mora list already brings every account up to date and counts days overdue** (`MoraWorklistQuery.cs:41-63`). | D3 reuses it instead of a second loop or a second formula. |
| F4 | **`SecretMask` masks only `password=` and SQL `PASSWORD '...'`** (`SecretMask.cs:11-27`); a bare app password inside a MailKit message would reach the error log. | D7. |
| F5 | **Windows-only adapters are built in `App.xaml.cs`, not registered in the session** (`App.xaml.cs:150,252`). | §4.4 follows it. |
| F6 | **Changing the Gmail account's password revokes every app password** ([Google](https://support.google.com/accounts/answer/185833)), and creating one needs 2-Step Verification. | The failed-login message says so, so the operator knows to ask the Admin for a new one. |

## 7. Slices, sizes and test mapping

| Slice | Content | Tests | Estimate | Needs |
|---|---|---|---|---|
| T1 | Domain types, `LedgerForPayment`, table and migration, `ReadAsync`, registration | 1–5, 7, 14, 15, 18 | ~400 | Docker |
| T2 | `SendAsync`, the MailKit adapter, `NoticeComposer` | 6, 8–11, 13 | ~350 | Docker |
| T3 | The two view-models, the shell entry, the Windows store | 12, 16, 17 | ~300 | Docker for the full suite |
| T4 | The views, with Mateo's batch | — | ~200 | Docker for the full suite |

T1 carries the migration and its model change together, in one PR (`CLAUDE.md`: a model change and its migration are
atomic). T1 and T2 touch high-risk items: the table's grants (item 2), stored data (item 1) and a new dependency (item 5);
each gets the independent verifier. Applying the migration to the test database is Tomás's, with Supabase restored.

## 8. Threats and rollback

| Threat | Answer |
|---|---|
| Duplicate email | D4, D6; Test 9 |
| Wrong amount | D2; Domain test against `AmountOwedForPayment`; Test 5 |
| Password in the log | D7; Test 13 |
| An Empleado erases a sent notice | Trigger plus grants; Test 14 |
| A long Gmail stall holds a row lock | 30-second timeout (D8); only the same notice on another PC waits |
| Extraction changes a desk figure | The cash desk's existing suite, unchanged and green |

Rollback: revert the PRs and run `Down`; it drops `tenant_notices` and its two functions and touches no other table. The
sent rows go with it.

## 9. Parameters the spec left to design

| Parameter | Where |
|---|---|
| Table and column names, the kind's stored value, how the month is stored | §3.1 |
| Reading the desk figure for every account in one pass | §1, D2, D3 |
| Adding the tenant's email to what `AccountListings` reads | `AccountListingEntry` gains the tenant's email, read from the `Party` it already loads (`AccountListings.cs:29-40`) |
| What "shaped like an address" checks | `MailboxAddress.TryParse`, §3.4 |
| Where the Gmail address is configured, the credential's name | In the credential itself, D9; nothing in `appsettings.json` |
| Sending off the screen's thread, with progress | §4.1, through the existing runner |
| The view-model's name, its states, its place in the menu | §4 |
| Spanish text and layout | D10; the layout with Mateo's batch |

## 10. Questions

None open for this change. **Answered 2026-11-12:** Tomás ran the query below on production; every table is granted to
all three API roles (Finding F2). The application never uses the Data API: it connects to Postgres directly through the
pooler, and no Supabase API URL or key appears in the repository. The Data API (Integrations > Data API > Overview > **Enable Data
API**) is already off in production and in the test project (Tomás, 2026-11-12, checked after a reload); with it off,
"none of the auto-generated REST endpoints respond, regardless of grants or RLS" (same page). It must stay off. The
revoke itself is a separate change. The query, kept to re-check after that fix:

```sql
SELECT grantee, table_name, string_agg(privilege_type, ',' ORDER BY privilege_type)
FROM information_schema.role_table_grants
WHERE table_schema = 'public' AND grantee IN ('anon', 'authenticated', 'service_role')
GROUP BY grantee, table_name ORDER BY table_name, grantee;
```

No rows for `anon` or `authenticated`: F2 is closed. Rows for them: those tables are readable through the Data API by
whoever has the project's public key, and a separate fix comes before go-live. Rows for `service_role` alone matter less:
its key is secret and bypasses every rule by design, but nothing in this system uses it, so they are revoked in the same
fix.
