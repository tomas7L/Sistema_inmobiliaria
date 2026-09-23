# Proposal: Users and Roles

Domain vocabulary (Argentine terms, glossed once): **Empleado** = employee, the restricted role as
the agency's own draft named it; **locador** = lessor/owner; **locatario** = tenant.

Third SDD change. It amends the living specification (44 requirements across `party-registry`,
`unit-registry`, `lease-contract`, `contract-documents`, `economic-index`, `rent-adjustment`) and
does not restate it.

## Intent

The system has no idea who is using it. `ContractDocument.UploadedBy` is a free string that nobody
validates, `RentAdjustment` records `ConfirmedAt` but not who confirmed, and the two employees are
indistinguishable to every table in the database. Change 1 recorded the string uploader explicitly
as a placeholder "because no user entity exists yet". This change owns that conversion.

Separately and more seriously: a database credential sits in a file on the office PC today. Anyone
who can read that file can open any Postgres client and do anything, and every role separation the
application draws is a picture of a rule rather than the rule itself.

**Why now:** two reasons, and the second is the hard one.

1. Every remaining change writes records that someone will later have to explain — a receipt, a
   collection, a liquidación. Adding "who did this" after those tables exist means a migration over
   rows with no truthful answer. `RentAdjustment` is the warning: it is append-only and guarded by
   a database trigger, so a column added to it later cannot be backfilled at all.
2. `credential-exposure` has been open since change 1 and comes due here. Deferring it *is* a
   decision about what roles mean.

**Success:** no database credential exists on disk; each employee logs in as themselves; the records
name them permanently; and the restrictions hold whether the person arrives through this application
or through pgAdmin.

## The decision: `credential-exposure` is RESOLVED, not accepted

### What was chosen, and what was turned down

The team considered accepting the exposure (roles as a convenience, documented as cosmetic) and
considered Windows-identity authentication (cheapest of all — zero credential management). Both were
rejected deliberately. The system is to be genuinely protected, and a username-and-password login
screen is what these users already understand; Windows identity is too unusual to explain at the
moment something goes wrong.

**Supabase Auth is NOT used.** It was the expensive half of the option change 1 priced at "weeks" —
a token exchange, an auth mapping, and a client library the project otherwise has no use for.
Per-user PostgreSQL roles deliver the protection without any of it. This is recorded here explicitly
so that nobody later reintroduces Supabase Auth believing it was an oversight.

### The shape

- **A conventional login screen.** Username and password, like any application.
- **The password typed at login IS that user's PostgreSQL password.** The application composes its
  connection string at login time from the host, port and database name in configuration (none of
  which is a secret) plus the credentials just entered.
- **Therefore no database credential is ever stored on disk.** Not in `appsettings.json`, not in an
  environment variable read by the application, nowhere. This improvement stands on its own and
  would be worth shipping even if roles did not exist.
- **Two PostgreSQL group roles with different GRANTs**, `inmobiliaria_admin` and
  `inmobiliaria_empleado`. Each employee gets a personal LOGIN role that is a member of one of them.
- **Identity comes from the database, never from the client.** The application asks Postgres
  `SELECT current_user` and derives the role from `pg_has_role(...)`. There is no claim the client
  could forge because there is no claim.

### Why this is genuinely different from hiding buttons

A role separation enforced in the UI is a suggestion. Uninstall the WPF client, open any Postgres
client with the same credential, and the separation is gone — because it was never in the database.

A role separation enforced by GRANTs is enforced by the server. Bypassing the application gains
nothing: the person still has to authenticate, still arrives as themselves, and still carries only
their own privileges. An Empleado who opens pgAdmin and types
`INSERT INTO rent_adjustments ...` receives `permission denied for table rent_adjustments`. The
application is no longer the thing that says no; it is merely the first thing that says no.

**Two existing accepted gaps narrow as a side effect.** The append-only trigger on
`rent_adjustments` carried a documented gap — "a superuser can `ALTER TABLE ... DISABLE TRIGGER`.
Named, not closed." Neither application role owns those tables and neither is a superuser, so
neither can disable it. The gap shrinks from "anyone with the app's credential" to "whoever holds
the owner credential used for migrations", which is a developer, not an operator.

**RLS stays off, now for a better reason.** Previously it was off because the connection owned the
tables and would bypass it. That reason dies here. The new reason is that RLS answers *which rows*,
and this business has no row-level rule — both employees work every contract. `GRANT` answers *which
operations*, which is the actual question. `openspec/config.yaml`'s `data-api-disabled` note says
RLS "becomes relevant only if credential-exposure is later resolved via Supabase Auth"; that
sentence is now wrong in two ways and MUST be amended by this change.

**No password hashing anywhere in the application.** PostgreSQL holds the password, in its own
SCRAM verifier, and validates it during the connection handshake. This deletes a whole class of work
the previous proposal assumed: no `IPasswordHasher` port, no `PasswordHash` value object, no hashing
library to choose, and no risk of choosing it badly.

## Scope

### In Scope

- Two PostgreSQL group roles and their GRANTs, **written as SQL in a new migration** so they are
  versioned and reproducible rather than clicked into the Supabase dashboard
- `AppUser`: the FK target — id, username (equal to the Postgres `rolname`), display name, active
  flag. **No password column. No role column** (see Approach §3)
- Authentication by connection: credentials entered at login become the connection
- A session-scoped `DbContext` factory port, and defined behaviour before anyone has logged in
- The permission rule stated over **operations**, not screens (Approach §5)
- Self-service password change via `ALTER ROLE ... PASSWORD`
- A documented provisioning runbook for creating, deactivating and resetting users
- `ContractDocument.UploadedBy`: plain string → real foreign key to `AppUser`
- `RentAdjustment`: gains `ConfirmedBy`, nullable, no backfill
- A login window and the DI/host bootstrap it requires (Approach §6)
- Recording `credential-exposure` as **resolved** in `openspec/config.yaml`, and correcting the
  `data-api-disabled` rationale

### Out of Scope

"Out of scope" means *not in this pull request*, never *not in the product*. Each item names why.

- **Supabase Auth** — not needed, not deferred. Deliberately rejected; see above.
- **Row Level Security** — no row-level rule exists to express.
- **A general audit log** (who viewed what, who edited a party) — no consumer for two people in one
  office. It would add a write to every operation and a table that grows forever to answer a
  question nobody asks. When the agency hires, that is the moment — and the user rows it would
  reference will already exist, which is the cost this change pays down.
- **Receipt and liquidación authorship** — those records do not exist yet. **The collection change
  MUST create receipts with an issuer from the start**, for the same reason `ConfirmedBy` is being
  added now rather than later.
- **In-application user creation** — see Approach §4; it is a runbook, and the justification is
  there.
- **Password policy, lockout, MFA, email reset** — two users, one office, one machine.
- **Navigation shell, menus, screen list** — the login window is the only screen this change adds.

## Capabilities

### New Capabilities

- `user-registry`: user identity as a PostgreSQL login role plus an `AppUser` row, the provisioning
  and deactivation lifecycle, password change, and the rule that identity is read from the database
- `access-control`: the operation rule, its enforcement by GRANT, the column-level cases, and the
  written limit of what GRANTs can and cannot express

### Modified Capabilities

- `contract-documents`: the requirement **"Uploader as Plain Identifier"** is removed — it existed
  only because no user entity existed. **"Document Metadata"** is modified in the same delta so it
  names a user reference rather than an uploader identifier.
- `rent-adjustment`: **"Append-Only Adjustment History"** is modified to include the confirming user
  among the fields a confirmed row records, and to narrow the recorded trigger-disable gap. The
  append-only rule itself is unchanged and MUST keep passing.

## Approach

### 1. Where the connection is built

Today `InmobiliariaDbContext` is constructed from a configured connection string, in two places:
`DesignTimeDbContextFactory` and the (not yet written) desktop host. After this change the
connection comes from the authenticated session.

- A **session-scoped factory port** is the only way the application obtains a `DbContext`. It holds
  the connection string composed at login and yields a context bound to it.
- **Before anyone has logged in there is no factory.** It is not registered in the container until
  authentication succeeds; a context cannot be requested because there is nothing to request it
  from. This is stronger than a factory that throws, and it is checkable.
- **Authentication is the connection attempt itself.** A successful open means the credentials are
  correct — Postgres said so. `28P01` means they are not. No credential is verified by the
  application, because the application has nothing to verify against.
- The composed connection string and the password live **in process memory only**, for the lifetime
  of the session. Residual gap, stated plainly: a memory dump of the running process reveals the
  password. That is materially smaller than a file on disk that survives reboots and backups.
- The port lives in `Inmobiliaria.Infrastructure`. The domain gains only `IUserSession` (username,
  role, display name) and keeps no reference to EF Core, Npgsql or WPF.

### 2. Migrations keep an owner-level credential — and it is a development path

`dotnet ef` needs privileges neither application role has: `CREATE TABLE`, `CREATE ROLE`, `GRANT`.
`DesignTimeDbContextFactory` and its `INMOBILIARIA_DB` environment variable **stay exactly as they
are**, and this change writes down what was previously only implied:

> `INMOBILIARIA_DB` is a development-time path, used by a developer running migrations from a
> workstation or by the Testcontainers fixture. **The application never reads it, never calls
> `Database.Migrate()`, and never holds owner privileges.**

A test asserts the application's startup path performs no migration.

### 3. `AppUser` stores no role, and that is the point

The authoritative role is membership in a Postgres group role, read at login via `pg_has_role`. If
the role were also a column in `app_users`, the two could disagree — and the UI would believe the
column while the database enforced the membership. A "UI says Admin, database says Empleado"
mismatch is exactly the class of bug this change exists to eliminate, so the column does not exist.

`app_users` holds only what the database cannot: display name, and the stable id that
`ContractDocument.UploadedBy` and `RentAdjustment.ConfirmedBy` point at. `username` is unique and
equals `rolname`.

### 4. Creating and removing users is a documented manual runbook

`CREATE ROLE` cannot run as an ordinary application user. Two ways to give it to the Admin:

- **Grant the Admin role `CREATEROLE`.** PostgreSQL 17 constrains this far better than older
  versions, but it is still a privilege-escalation surface, and a mistake made through it is not
  reversible by the application that made it.
- **A runbook.** One documented SQL script, run in the Supabase SQL editor by whoever holds the
  owner credential: `CREATE ROLE ... LOGIN PASSWORD`, `GRANT inmobiliaria_empleado TO ...`,
  `INSERT INTO app_users ...`, in one transaction so a role never exists without its row.

**Recommendation: the runbook.** This happens roughly twice, ever — the agency has two employees.
Building an in-application user-management screen means building screens that do not exist, adding
raw DDL execution to a codebase that otherwise only speaks EF Core, and permanently widening the
Admin role's privileges to serve an operation performed once a year. The cost is entirely one-sided.

**Deactivation is `ALTER ROLE ... NOLOGIN` plus `is_active = false`, never `DROP ROLE`.** Dropping
frees the name for reuse by a different human, and every historical row pointing at that username
would then quietly mean someone else.

### 5. What the two roles may actually do

`src/Inmobiliaria.Desktop/` contains `App.xaml` and `MainWindow.xaml` and nothing else. There is no
navigation and no screen to restrict, so a screen list written today would be invented. The team's
call is the rule, not the list:

> **Admin owns anything that moves money or writes append-only history. Empleado owns the rest.**

Translated into GRANTs over what exists today:

| Table | Empleado | Admin |
|---|---|---|
| All twelve tables | `SELECT` | `SELECT` |
| `parties`, `units`, `contract_parties`, `contract_units`, `contract_documents` | `INSERT`, `UPDATE` | same |
| `economic_indices`, `index_values`, `adjustment_clauses`, `adjustment_clause_indices` | `INSERT`, `UPDATE` | same |
| `contracts` | `INSERT`, `UPDATE` on all columns **except** the termination columns | plus `UPDATE (ended_at, end_reason)` |
| `rent_adjustments`, `rent_adjustment_index_values` | none | `INSERT` |
| `app_users` | `SELECT` only | `SELECT` only (writes are the runbook) |

Neither role gets `DELETE` anywhere, nor ownership, nor schema privileges.

**Where GRANTs cannot reach, say so.** Column-level `UPDATE` separates contract termination
cleanly. It will not separate every future operation — two operations that write the same columns of
the same table are indistinguishable to Postgres. The spec MUST name each such case as
UI-enforced-only rather than let the reader assume the database is holding it.

**Every future migration that creates a table MUST grant on it**, or the application fails at
runtime for both roles. `ALTER DEFAULT PRIVILEGES` covers the `SELECT` baseline; writes stay
explicit per table, because "what may this role write" is the decision this change exists to make
deliberately. This becomes a `conventions` entry in `openspec/config.yaml`.

### 6. Password changes

`ALTER ROLE <self> PASSWORD '<new>'` — an ordinary non-superuser role may change its own password
with no special privilege, on its own session. So **any user can change their own password from
inside the application**, and after it succeeds the session's connection string is recomposed.

**Resetting someone else's** password needs `CREATEROLE` or superuser. This paragraph previously
concluded that it had to be a runbook step; that was reversed — see Settled below. The Admin's role
carries `CREATEROLE`, narrowed by PostgreSQL 16 to the roles it created itself, and the reset
happens from inside the application.

**A password the Admin typed must not stay in use.** Whenever a password is set by somebody other
than its owner — at provisioning or at reset — `AppUser.MustChangePassword` is raised, and that
user's next login requires a new one before anything else. This is credential hygiene, not a
security boundary: PostgreSQL authenticates before the application asks anything, so a direct
client connection never sees the prompt. The spec states that limit explicitly and tests it, so
nobody later mistakes the flag for enforcement.

Two design constraints for `sdd-design`: DDL cannot be parameterised, so the new password MUST be
escaped through `quote_literal` or an equivalent server-side path — string concatenation here is a
SQL-injection hole in the one statement that must not have one. And `log_statement = 'ddl'` would
write the plaintext password to the Postgres log; verify Supabase's setting before shipping.

### 7. The login window enters this change as a consequence

The previous proposal treated the login window as an open question and recommended deferring it.
Choosing a login screen settles it: the window ships here, and with it the project's first DI/host
bootstrap, because a window that composes a connection has to be composed by something. This is
slice 4, it is real work, and it is named rather than smuggled in.

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/Inmobiliaria.Domain/Access` | New | `AppUser`, `UserRole`, `IUserSession`, the operation rule |
| `src/Inmobiliaria.Domain/Leasing/ContractDocument.cs` | Modified | `UploadedBy` string → user reference |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs` | Modified | Gains `ConfirmedBy` |
| `.../Persistence/InmobiliariaDbContext.cs` | Modified | `DbSet<AppUser>`; thirteenth table |
| `.../Persistence/Configurations` | New + Modified | `AppUserConfiguration`; `ContractDocumentConfiguration` and `RentAdjustmentConfiguration` updated |
| `.../Persistence/Migrations` | New | A **new** migration: table, FKs, roles, GRANTs. Both existing migrations are live and untouchable |
| `.../Persistence/DesignTimeDbContextFactory.cs` | Unchanged | Documented as development-time only |
| `.../Persistence/SessionDbContextFactory.cs` | New | The session-scoped factory port and its Npgsql adapter |
| `src/Inmobiliaria.Desktop` | New | Login window, session, DI/host bootstrap |
| `docs/` runbook | New | Create / deactivate / reset a user |
| `openspec/specs/contract-documents` | Modified | Delta: uploader becomes a key; placeholder requirement removed |
| `openspec/specs/rent-adjustment` | Modified | Delta: confirmed row records who confirmed; trigger gap narrowed |
| `openspec/config.yaml` | Modified | `credential-exposure` → `resolved_decisions`; `data-api-disabled` rationale corrected; grant convention added |

## Open Decisions Touched

- **`credential-exposure`** — **resolved by this change**, and resolved rather than accepted. Moves
  to `resolved_decisions` with the full reasoning, including the explicit rejection of Supabase Auth
  and of Windows identity, so neither is revisited as an oversight.
- **`scheduler-mechanism`** — untouched. Nothing here executes unattended.
- **`pdf-library`** — untouched. Nothing here generates a document.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Supabase connection topology breaks per-user login: the direct `5432` endpoint is IPv6-only without the IPv4 add-on, and Supavisor expects a `<role>.<projectref>` username | **High — verify first** | Prove a non-`postgres` role can connect from the office network **before** slice 3. This is a go/no-go on the whole shape; resolve it in `sdd-design`, not in apply |
| Backfilling `rent_adjustments.confirmed_by` is blocked by the append-only trigger | **High if unnoticed** | Add the column **nullable with no backfill**. The trigger is `BEFORE UPDATE OR DELETE FOR EACH ROW`: the `ALTER TABLE` succeeds, any backfill `UPDATE` is rejected by the database |
| `ContractDocument.UploadedBy` holds strings matching no user | Med | Expected zero rows (no real agency data yet). The migration still must be correct: map known strings, route the rest to a designated inactive legacy user so the column stays NOT NULL |
| Rollback loses uploader information | Med | The `Down` migration MUST restore `contract_documents.uploaded_by` as a string and write back each username **before** dropping the FK |
| A future migration adds a table and forgets its GRANTs | **High** | `ALTER DEFAULT PRIVILEGES` for reads; a `conventions` entry for writes; an integration test that connects as each role and fails on any table it cannot read |
| The password is held in process memory | Accepted | Stated in the spec. Strictly better than the file on disk it replaces |
| A locked-out employee cannot work until a developer is reached | Med | Named in the runbook; see question 2 |
| Slice 4 quietly becomes the app-shell change | Med | Login window and bootstrap only. No navigation, no menus, no second screen |
| Change exceeds the 800-line budget | **High** | Four chained PRs; `ask-on-risk` surfaces it at `sdd-tasks` |

## Rollback Plan

1. `dotnet ef database update 20260918233049_AddRentAdjustments`, then remove the new migration.
   Neither live migration is edited; `AddRentAdjustments` is the rollback target.
2. The `Down` migration MUST, in order: restore `contract_documents.uploaded_by` as a string and
   write back each username; drop the FKs and `confirmed_by`; `REVOKE ALL` from both group roles;
   `DROP ROLE` the two **group** roles. It MUST NOT drop individual login roles — the migration did
   not create them and their passwords exist nowhere else.
3. `git revert` the PRs in reverse order.
4. Delete `openspec/changes/users-and-roles/`; revert `credential-exposure` to `open_decisions`.

**Stated honestly: rolling back reinstates the stored credential**, because the application would
again have no way to connect as a person. No contract, adjustment or document row is destroyed. The
append-only history keeps every value it had; it loses only who confirmed each row.

## Dependencies

- `contract-and-parties` and `rent-adjustments`, both archived
- Domain observations: `project/estado-y-orden-de-changes`
- Docker running, so the Testcontainers integration tests against `postgres:17.6` execute
- A verified non-`postgres` Supabase login from the office network (see Risks, row 1)
- No new package for hashing — there is nothing to hash

## Size Forecast

Honest estimate, authored lines (additions + deletions):

| Slice | Est. lines |
|---|---|
| 1. `AppUser`, `UserRole`, `IUserSession`, operation rule + domain tests | 300–400 |
| 2. EF configuration, new migration (table, FKs, roles, GRANTs), integration tests that connect as each role and assert `permission denied` | 480–600 |
| 3. Session `DbContext` factory port, authentication-by-connection, role derivation, password change + tests | 350–450 |
| 4. DI/host bootstrap, login window, session wiring | 400–500 |

**Total 1530–1950 — over the 800-line budget.** Targeting 400–500 authored lines per PR gives
**four chained PRs**. Order is forced: slice 2 needs slice 1's entity, slice 3 needs slice 2's roles
to authenticate against, slice 4 needs slice 3's session. Slice 2 is the one most likely to overrun;
if it does, split the GRANT migration and its permission tests into their own PR. `sdd-tasks` plans
the split. Do not rename the CI `build` job; `core` also blocks merges.

## Note for the spec phase

A single `spec.md` with a table of contents and a Decisions table at the top, plus a numbered Tests
list at the bottom — the format established by `rent-adjustments`. The Decisions table MUST carry,
as its first rows, that `credential-exposure` is resolved by per-user Postgres roles, and that
Supabase Auth and Windows identity were considered and rejected.

## Settled — answers to the question round below

Decided with the user on 2026-09-21. These override the question round's assumptions.

**1 and 2 — the Admin manages users from inside the application**, rather than through a developer
runbook. The recommendation was reversed on its operational cost: a runbook means an employee who
forgets her password cannot work until a developer is reachable, and the agency stops for a reason
that has nothing to do with the agency. PostgreSQL 16 narrowed `CREATEROLE` so it can only manage
roles it created and can never grant superuser, so the privilege is far tighter than the name
suggests. The project runs 17.6.

**Deactivate, never delete.** Removing a user and creating another with the same name would silently
reassign every document upload and every confirmed adjustment the first person made to the second.
The UI calls it "dar de baja"; underneath it is `ALTER ROLE ... NOLOGIN` and the row stays. A
replacement employee is always a new user with her own name, never a reused one.

**3 — the Empleado sees everything except system governance and aggregate business reporting.**
User management is governance, not daily work. Agency-level income, profitability and totals are
Nicolás's business information, not operational data. Everything else — contracts, tenants, owners,
rents, receipts, and the honorarios percentage, which is printed on the receipt she issues herself —
she can see.

**4 — a password set by another person must be changed at first use.** Provisioning and Admin reset
both raise `AppUser.MustChangePassword`; the affected user's next login requires a new password
before they can reach anything else, and it must differ from the one they just used. The point is
narrow and worth stating plainly: the Admin knows the password he typed, and that password should
not be the one in daily use. It is not a security control — the GRANT set is what confines a user,
and it is identical before and after the change.

The reasoning matters more than the list: **a restriction that gets bypassed in practice is worse
than no restriction.** An employee blocked from something she needs will call the owner, and sooner
or later he hands her his password to stop being interrupted. That ends with no separation at all
plus a shared credential, which would defeat this entire change. Restricting too little is safer
than restricting too much, and grants are one migration away from tightening once there is evidence
rather than a guess.

## Proposal question round

Superseded by the section above; kept for the reasoning. Three questions; nothing
already settled is re-asked. The assumptions are what the proposal commits to if no answer arrives.

1. **User provisioning: runbook, or `CREATEROLE` on the Admin role?** The proposal recommends the
   runbook — it happens twice ever, and the alternative permanently widens Admin's privileges to
   serve a once-a-year operation. Choosing `CREATEROLE` adds an in-application user-management
   screen and roughly 300 lines. *Assumed: runbook.*
2. **Is developer-dependent password reset acceptable?** With the runbook, an employee who forgets
   their password cannot work until someone with the owner credential runs one line. On a Sunday
   that is a real cost. The alternative is question 1's `CREATEROLE`. *Assumed: acceptable — two
   people, one office, and a password they chose themselves.*
3. **Is there anything an Empleado must not be able to SEE?** Until now restricting reads was
   pointless because it could be bypassed; GRANTs make it real and enforceable, so it is worth
   asking once. Candidates: owner honorarios percentages, and future liquidación amounts.
   *Assumed: no read restrictions — both employees may `SELECT` everything.*

Answers to 1 and 2 are the same fork and change the size forecast by about one slice. Answer 3
changes the GRANT table but nothing structural.

## Success Criteria

- [ ] No database credential exists in any file the application reads — searchable, and a test asserts it
- [ ] Two employees log in with their own username and password and the system tells them apart
- [ ] An Empleado connecting with **pgAdmin** and running `INSERT INTO rent_adjustments` receives
      `permission denied`, proven by an integration test, not by argument
- [ ] The application's role for a session is derived from `pg_has_role`, never from a stored column
- [ ] `ContractDocument.UploadedBy` is a real reference; the "plain identifier" requirement is gone
      from the living specification, not merely contradicted by the code
- [ ] A confirmed `RentAdjustment` records who confirmed it, and the append-only trigger still
      rejects every update and delete
- [ ] Neither application role can `ALTER TABLE ... DISABLE TRIGGER` on `rent_adjustments`
- [ ] A user changes their own password from inside the application and the session survives it
- [ ] A deactivated user cannot log in, and every record referencing them stays readable
- [ ] The application never calls `Database.Migrate()` and never holds owner privileges
- [ ] The GRANTs are SQL inside a migration, reproducible from an empty database by
      `dotnet ef database update` alone
- [ ] The new migration applies on top of `20260918233049_AddRentAdjustments` without editing either
      live migration
