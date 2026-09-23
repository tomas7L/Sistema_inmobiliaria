# Spec: Users and Roles

Single-file specification for the `users-and-roles` change. Four capabilities are covered below:
`user-registry` (**NEW**), `access-control` (**NEW**), `contract-documents` (**MODIFIED** — a delta
against `openspec/specs/contract-documents/spec.md`), and `rent-adjustment` (**MODIFIED** — a delta
against `openspec/specs/rent-adjustment/spec.md`).

Domain vocabulary (Argentine terms, glossed once): **Empleado** = employee, the restricted role, as
the agency's own draft named it; **Admin** = the agency owner's own role; **locador** = lessor/owner;
**locatario** = tenant.

## Decisions

Every decision this spec rests on, and who made it. **CONFIRMED BY USER** means the answer came
from the 2026-09-21 question round with the project owner. **TEAM DECISION** means Volkode decided
without a user round because the shape had to be settled to write this spec at all. **VERIFIED
FACT** means it was proven, not decided.

| # | Decision | Status |
|---|----------|--------|
| 1 | The password typed at login IS the user's PostgreSQL password. The application verifies nothing itself; PostgreSQL rejecting the connection IS a failed login. | TEAM DECISION |
| 2 | **Supabase Auth is NOT used.** Considered and rejected — it would add a token exchange and an auth mapping the project has no other use for. | TEAM DECISION |
| 3 | Windows-identity authentication was considered and rejected — too unusual to explain to two users the moment something goes wrong. | TEAM DECISION |
| 4 | Two PostgreSQL group roles (`inmobiliaria_admin`, `inmobiliaria_empleado`) with distinct GRANTs, written as SQL inside a versioned migration. | TEAM DECISION |
| 5 | The application role is read at login via `pg_has_role`; `AppUser` stores no role column, so nothing can drift from what the database enforces. | TEAM DECISION |
| 6 | **The Admin manages users from inside the application** — creating them and resetting passwords — rather than through a developer-run runbook. | CONFIRMED BY USER |
| 7 | **Deactivate, never delete.** `ALTER ROLE ... NOLOGIN`; the row stays; a username is never reused for a different person. | CONFIRMED BY USER |
| 8 | **The Empleado does everything operational and is excluded from exactly two things:** system governance (user management) and aggregate business reporting (agency-level income, profitability, totals). Collecting rent, issuing receipts, terminating contracts and confirming adjustments are hers as much as the Admin's. | CONFIRMED BY USER |
| 9 | A non-`postgres` role was proven to connect through Supavisor from the office network: the attempt returned `42501 permission denied`, meaning the pooler routed it and Postgres authenticated it before the grants refused the operation. Username format required by the pooler: `<role>.<projectref>`. | VERIFIED FACT |
| 10 | `ContractDocument.UploadedBy` becomes a real foreign key to `AppUser`, replacing the placeholder string. | TEAM DECISION |
| 11 | `RentAdjustment` gains `ConfirmedBy`, nullable, with no backfill — its append-only trigger rejects any `UPDATE`, so this is the last cheap moment to add the column. | TEAM DECISION |
| 12 | Password-setting DDL (`CREATE ROLE`, `ALTER ROLE ... PASSWORD`) MUST escape the password server-side; it cannot be parameterised like ordinary application SQL. | TEAM DECISION |
| 13 | A general audit log is out of scope. The collection change (not this one) MUST create receipts with an issuer from the start. | TEAM DECISION |
| 14 | **A password set by someone other than its owner must be changed at that owner's next login.** Tracked by `AppUser.MustChangePassword`. It is an application convention for credential hygiene, NOT a security boundary — PostgreSQL authenticates before the application asks anything, so a direct client connection is unaffected by it. | CONFIRMED BY USER |
| 15 | **The clause reserving to Admin "every operation that moves money or writes append-only history" is REMOVED.** It was never decided by the agency, it contradicted Decision 8 standing beside it, and the two people who use this system do the same operational work. Its removal is why `rent_adjustments` is now writable by both roles. | CONFIRMED BY USER |

## Table of Contents

1. [Capability: user-registry (NEW)](#capability-user-registry-new)
   1. [Requirement: No Database Credential Stored on Disk, and No Supabase Auth](#requirement-no-database-credential-stored-on-disk-and-no-supabase-auth)
   2. [Requirement: Login Is the Connection Attempt](#requirement-login-is-the-connection-attempt)
   3. [Requirement: No Database Access Before Authentication](#requirement-no-database-access-before-authentication)
   4. [Requirement: Application Role Read From PostgreSQL, Never Mirrored](#requirement-application-role-read-from-postgresql-never-mirrored)
   5. [Requirement: Admin Provisions and Deactivates Users From Inside the Application](#requirement-admin-provisions-and-deactivates-users-from-inside-the-application)
   6. [Requirement: Self-Service Password Change Is Immediate; Admin Reset Takes Effect Next Login](#requirement-self-service-password-change-is-immediate-admin-reset-takes-effect-next-login)
   7. [Requirement: A Password Set by Another Person Must Be Changed Before Anything Else](#requirement-a-password-set-by-another-person-must-be-changed-before-anything-else)
   8. [Requirement: Password DDL Is Never Built by String Concatenation](#requirement-password-ddl-is-never-built-by-string-concatenation)
2. [Capability: access-control (NEW)](#capability-access-control-new)
   1. [Requirement: The Operation Rule Is Stated Over Actions, Not Screens](#requirement-the-operation-rule-is-stated-over-actions-not-screens)
   2. [Requirement: Enforcement by Database GRANT — Bypassing the Application Gains Nothing](#requirement-enforcement-by-database-grant--bypassing-the-application-gains-nothing)
   3. [Requirement: Neither Application Role Can Disable the Append-Only Trigger](#requirement-neither-application-role-can-disable-the-append-only-trigger)
   4. [Requirement: Column-Level Limits Are Named Explicitly Where GRANTs Cannot Separate Operations](#requirement-column-level-limits-are-named-explicitly-where-grants-cannot-separate-operations)
   5. [Requirement: No Row-Level Restriction](#requirement-no-row-level-restriction)
   6. [Requirement: Every New Table Ships With Its GRANTs in the Same Migration](#requirement-every-new-table-ships-with-its-grants-in-the-same-migration)
3. [Capability: contract-documents (MODIFIED)](#capability-contract-documents-modified)
   1. [MODIFIED Requirement: Document Metadata](#modified-requirement-document-metadata)
   2. [REMOVED Requirement: Uploader as Plain Identifier](#removed-requirement-uploader-as-plain-identifier)
4. [Capability: rent-adjustment (MODIFIED)](#capability-rent-adjustment-modified)
   1. [MODIFIED Requirement: Append-Only Adjustment History](#modified-requirement-append-only-adjustment-history)
5. [Out of Scope](#out-of-scope)
6. [Tests](#tests)

---

## Capability: user-registry (NEW)

### Purpose

`AppUser` is the FK target every other table points at when it needs to name a person: id,
username (equal to the PostgreSQL `rolname`), display name, active flag. Identity is a personal
PostgreSQL login role; the account's password IS the login password there is no second credential
this capability manages. This capability covers login, provisioning, deactivation, and password
change. It does not cover what a role may then do — that is `access-control`.

### Requirement: No Database Credential Stored on Disk, and No Supabase Auth

The system MUST NOT persist any database credential in a configuration file, an environment
variable read by the running application, or any other on-disk location. The password entered at
login MUST exist only in the memory of that session, for its lifetime. **Supabase Auth MUST NOT be
used** for authentication or session identity — decided and rejected, not merely unimplemented, so
it is not reintroduced later believing it was overlooked.

#### Scenario: No credential recoverable from configuration

- GIVEN the application's configuration files and the environment it reads at startup
- WHEN they are inspected for a database credential
- THEN none MUST be found

#### Scenario: Supabase Auth is never invoked

- GIVEN a user submits a login form
- WHEN the login attempt is processed
- THEN no Supabase Auth API call MUST occur
- AND the only interaction with the database MUST be the PostgreSQL connection attempt itself

### Requirement: Login Is the Connection Attempt

A login attempt MUST succeed if and only if PostgreSQL accepts a connection composed from the
entered username and password. The application MUST NOT independently verify the password against
any stored value, because none exists to check it against.

#### Scenario: Correct credentials succeed

- GIVEN "maria" is an active user with a working PostgreSQL login role
- WHEN she enters her username and her correct password
- THEN the connection MUST succeed and the session MUST proceed as "maria"

#### Scenario: Wrong password is rejected without revealing whether the username exists

- GIVEN a user enters the valid username "maria" with an incorrect password
- WHEN the login attempt is made
- THEN PostgreSQL MUST reject the connection
- AND the application MUST display one generic failure message

#### Scenario: An unknown username produces the identical failure message

- GIVEN a username with no corresponding PostgreSQL login role at all
- WHEN a login attempt is made with that username and any password
- THEN the connection MUST fail
- AND the message shown MUST be worded identically to the wrong-password case, so a caller cannot
  distinguish "no such user" from "wrong password"

### Requirement: No Database Access Before Authentication

The system MUST NOT expose any means of opening a database connection or issuing any query before
a login attempt for the current session has already succeeded.

#### Scenario: The application at rest holds no connection

- GIVEN the application has just started and nobody has logged in
- WHEN any part of the application attempts to reach the database
- THEN there MUST be nothing available to reach it with — not a shared connection, not a cached
  credential, not a default role

### Requirement: Application Role Read From PostgreSQL, Never Mirrored

The role that governs what a session MAY do MUST be determined at login by asking PostgreSQL which
group role the connecting login role belongs to (`pg_has_role` or equivalent). `AppUser` MUST NOT
store a role column that duplicates this membership.

#### Scenario: Role is derived, not stored

- GIVEN "maria" is a member of `inmobiliaria_empleado`
- WHEN her session starts
- THEN the application MUST derive her role from the database membership check
- AND the `app_users` row for "maria" MUST have no role column to have disagreed with it

#### Scenario: A role change in the database takes effect on the next login

- GIVEN "maria" is a member of `inmobiliaria_empleado`
- WHEN the Admin changes her membership to `inmobiliaria_admin` directly in the database
- THEN her *next* login MUST derive the new role
- AND no `app_users` column requires updating for this to be correct

### Requirement: Admin Provisions and Deactivates Users From Inside the Application

An Admin MUST be able to create a new user — a personal PostgreSQL login role together with its
`AppUser` row, created as one unit — and to reset another user's password, from inside the
application. Deactivating a user MUST use `ALTER ROLE ... NOLOGIN` (or equivalent) together with
`AppUser.IsActive = false`, and MUST NOT delete the role or the row. A username, once assigned,
MUST NOT be reused for a different person.

#### Scenario: Admin creates a new employee

- GIVEN the Admin is logged in
- WHEN she provisions a new user "sofia" with role Empleado
- THEN a PostgreSQL login role "sofia" MUST exist, granted membership in `inmobiliaria_empleado`
- AND an `app_users` row for "sofia" MUST exist, active, with no orphaned role or row on either side

#### Scenario: A deactivated user cannot log in, and their historical rows still name them

- GIVEN "maria" uploaded a `ContractDocument` and confirmed a `RentAdjustment` while active
- WHEN an Admin deactivates "maria"
- THEN "maria" MUST be unable to log in afterward
- AND the `ContractDocument` and `RentAdjustment` rows referencing "maria" MUST remain unchanged and
  readable, still naming her by that same reference

#### Scenario: A deactivated username is never reassigned

- GIVEN "maria" has been deactivated
- WHEN a new employee is provisioned to replace her
- THEN the Admin MUST create a distinct new user with a new username
- AND the system MUST NOT permit "maria"'s login role or username to be given to the new person

### Requirement: Self-Service Password Change Is Immediate; Admin Reset Takes Effect Next Login

Any authenticated user MUST be able to change their own PostgreSQL password from inside the
application, and their already-open session MUST continue to work afterward. When an Admin resets
a **different** user's password, that change MUST take effect only the next time the affected user
authenticates — it MUST NOT disturb a session that user already has open.

#### Scenario: Self password change survives in the same session

- GIVEN "maria" is logged in
- WHEN she changes her own password from inside the application
- THEN the change MUST succeed
- AND her current session MUST continue to function without re-authenticating

#### Scenario: Admin reset does not affect a session already open

- GIVEN "maria" is logged in with an open session
- WHEN an Admin resets "maria"'s password
- THEN "maria"'s already-open session MUST keep working until she disconnects
- AND the new password MUST only be required the next time she attempts to log in

### Requirement: A Password Set by Another Person Must Be Changed Before Anything Else

When a user's password was set by somebody other than that user — at provisioning, or by an Admin
reset — that user's next successful authentication MUST require them to set a new password before
they can reach any other part of the application. The new password MUST be different from the one
they just authenticated with. Setting it MUST clear the requirement. A user changing their own
password while no such requirement is pending MUST NOT have one raised against them.

The pending state MUST be recorded on `AppUser.MustChangePassword`. Provisioning a user and
resetting another user's password MUST both set it; a self-service change MUST clear it.

**This is credential hygiene, not a security control, and MUST NOT be presented as one anywhere in
this project.** PostgreSQL has already authenticated the session before the application asks
anything, so a user who connects with pgAdmin instead of the application never sees the prompt and
works normally. What actually confines that user is the GRANT set, which is identical before and
after the change. The requirement exists because the password the Admin typed — and therefore
knows — should not stay in daily use; that intent is real and worth building, and it is all this
delivers.

#### Scenario: A newly provisioned user must change the password before reaching anything else

- GIVEN an Admin has provisioned "sofia" with a provisional password
- WHEN "sofia" authenticates for the first time
- THEN the application MUST require her to set a new password
- AND it MUST NOT let her reach any other part of the application until she has

#### Scenario: An Admin reset raises the requirement again

- GIVEN "maria" has been using the system with a password she set herself
- WHEN an Admin resets "maria"'s password
- THEN "maria"'s next authentication MUST again require her to set a new password before anything
  else

#### Scenario: Re-entering the provisional password is rejected

- GIVEN "sofia" is being required to change a password an Admin set
- WHEN she submits that same password as her new one
- THEN the change MUST be rejected
- AND the requirement MUST remain pending

#### Scenario: A voluntary change raises no requirement

- GIVEN "maria" has nothing pending
- WHEN she changes her own password because she felt like it
- THEN the change MUST succeed
- AND her next login MUST NOT require another change

#### Scenario: The requirement does not confine a direct database client

- GIVEN "sofia" has a pending password change
- WHEN she connects to the database directly with her own credentials, outside the application
- THEN the connection MUST succeed and behave exactly as it would with nothing pending
- AND her permissions MUST be identical to what her role grants at any other time — this is the
  honest limit of the requirement, not a defect to be fixed by adding database-side enforcement

### Requirement: Password DDL Is Never Built by String Concatenation

Every statement that sets or changes a PostgreSQL password (`CREATE ROLE ... PASSWORD`,
`ALTER ROLE ... PASSWORD`) MUST escape the password value through `quote_literal` or an equivalent
server-side mechanism, because this DDL cannot be parameterised the way ordinary application SQL
can. It MUST NOT be built by directly concatenating the raw password value into a SQL string.

#### Scenario: A password containing a quote character is set correctly

- GIVEN a new password value containing a single quote, e.g. `o'brien55`
- WHEN the Admin creates a user or resets a password with that exact value
- THEN the resulting DDL MUST set that exact password
- AND MUST NOT execute any statement smuggled in through the quote character

#### Scenario: An injection payload in the password field is neutralized

- GIVEN a submitted password value of `x'; DROP TABLE app_users; --`
- WHEN the password-setting DDL is executed
- THEN that literal string MUST become the password
- AND no other table or statement MUST be affected — `app_users` MUST still exist afterward

---

## Capability: access-control (NEW)

### Purpose

This capability states, once, what each role MAY do, and how that rule is enforced. The rule is
written over **operations**, never over screens — `src/Inmobiliaria.Desktop/` holds only
`App.xaml` and `MainWindow.xaml` today, so a screen list would be invented. Enforcement is by
PostgreSQL `GRANT`, not by application code, because a rule the UI merely hides is not a rule.

### Requirement: The Operation Rule Is Stated Over Actions, Not Screens

Empleado MUST own every operational action. Empleado MUST be excluded from exactly two things:
system governance (creating, deactivating, or resetting users) and aggregate business reporting
(agency-level income, profitability, and totals). There is no third exclusion.

No operation is reserved to Admin for being financially sensitive. Collecting rent, issuing a
receipt, and confirming a rent adjustment are ordinary operational work that **both** roles
perform. Empleado MUST retain the ability to see and act on contracts, tenants, owners, rents,
receipts, and the honorarios percentage printed on a receipt she issues herself.

An earlier draft of this requirement also reserved to Admin "every operation that moves money or
writes append-only history". That clause is **REMOVED, not merely unimplemented.** It was never
decided by the agency, it contradicted the exclusion list standing beside it, and the two people who
use this system do the same operational work. It MUST NOT be reintroduced by a reader who finds its
absence surprising.

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

### Requirement: Enforcement by Database GRANT — Bypassing the Application Gains Nothing

Every restriction stated by the operation rule MUST be enforced by a PostgreSQL `GRANT` bound to
the connecting role, not solely by application logic. An operation forbidden to Empleado MUST be
refused by PostgreSQL itself even when attempted through a client other than this application.

#### Scenario: An Empleado attempting an Admin-only operation is refused by the database, not merely hidden in the UI

- GIVEN "maria" holds `inmobiliaria_empleado` and connects with **pgAdmin**, not this application,
  using her own credentials
- WHEN she executes `INSERT INTO app_users (...) VALUES (...)` directly, or invokes the function
  that provisions a login role
- THEN PostgreSQL MUST reject it with a permission error
- AND no row MUST be written and no role MUST be created — bypassing the application gained her
  nothing: she still had to authenticate as herself, and she still carried only her own privileges

#### Scenario: An Admin performs the same operation directly and succeeds

- GIVEN a user holding `inmobiliaria_admin` connects with any Postgres client
- WHEN they execute the equivalent user-provisioning operation
- THEN it MUST succeed, because the grant — not the application — is what allows it

### Requirement: Neither Application Role Can Disable the Append-Only Trigger

Neither `inmobiliaria_admin` nor `inmobiliaria_empleado` MUST hold privileges sufficient to
`ALTER TABLE ... DISABLE TRIGGER` on `rent_adjustments`, or to otherwise bypass its append-only
enforcement. This narrows an existing accepted gap from "anyone holding the application's
credential" to "whoever holds the migration-owner credential" — a developer path, not an operator
one.

#### Scenario: Admin role cannot disable the trigger

- GIVEN a connection authenticated as a login role that is only a member of `inmobiliaria_admin`
- WHEN it attempts `ALTER TABLE rent_adjustments DISABLE TRIGGER ...`
- THEN PostgreSQL MUST refuse it — neither application role owns the table

### Requirement: Column-Level Limits Are Named Explicitly Where GRANTs Cannot Separate Operations

Where two distinct operations would write the same columns of the same table, and PostgreSQL's
`GRANT` system cannot distinguish between them, this specification MUST name that case explicitly
as enforced by the application UI only, rather than letting a reader assume database enforcement
that does not exist.

#### Scenario: Aggregate business reporting is separated by the application only

- GIVEN "maria" holds the Empleado role and is legitimately granted `SELECT` on `contracts` and on
  the collection tables, because she needs those rows to do her daily work
- WHEN she connects with pgAdmin and runs `SELECT sum(monthly_rent) FROM contracts`
- THEN PostgreSQL MUST allow it, because the total is derived from rows she may already read and no
  `GRANT` can forbid an aggregate over permitted rows
- AND this specification MUST state plainly that the exclusion from aggregate reporting is enforced
  by the application only, and MUST NOT claim database enforcement for it

#### Scenario: An operation the database cannot distinguish is named, not implied

- GIVEN two hypothetical future operations that would both write the same already-granted columns
  of the same table
- WHEN access control for that case is documented
- THEN the specification MUST state plainly that the separation is UI-enforced only, and MUST NOT
  claim the database enforces a distinction it structurally cannot make

### Requirement: No Row-Level Restriction

Neither role MUST be restricted from any particular *row* of a table it has been granted access
to. Both employees work every contract; there is no row-level rule this business needs, so no Row
Level Security policy MUST filter what either role can see among the rows it is granted to read.

#### Scenario: Both roles see every row of a table they may read

- GIVEN "maria" (Empleado) and the Admin both hold `SELECT` on `contracts`
- WHEN each queries `contracts`
- THEN each MUST see every row in the table, with no row hidden from either by a row-level policy

### Requirement: Every New Table Ships With Its GRANTs in the Same Migration

Any migration that creates a new table MUST grant, in that same migration, the baseline `SELECT`
and the deliberately chosen write privileges to both application roles. A table with no grants MUST
NOT be shippable in a state where the application fails at runtime for both roles.

#### Scenario: A new table is usable by both roles immediately after migration

- GIVEN a migration creates a new table
- WHEN that migration completes and either role connects
- THEN each role MUST be able to perform exactly the operations this specification says it may —
  no table MUST be silently unreachable to a role that needs it

---

## Capability: contract-documents (MODIFIED)

This is a delta against `openspec/specs/contract-documents/spec.md`. All other requirements in
that living spec are unaffected and MUST continue to hold exactly as written.

### MODIFIED Requirement: Document Metadata

Each `ContractDocument` MUST store a storage pointer (Supabase Storage path/key), original
filename, content type, uploaded-at timestamp, a reference to the `AppUser` who uploaded it, and a
`DocumentKind` (Original, Addendum, or TerminationNotice).

(Previously: recorded an "uploader identifier" as a plain string, not a relational reference — see
the removed requirement below.)

#### Scenario: Upload records full metadata with a real uploader reference

- GIVEN an original signed lease file is uploaded for a contract by an authenticated user "maria"
- WHEN the `ContractDocument` is created
- THEN it MUST record the storage pointer, filename, content type, uploaded-at, a foreign-key
  reference to "maria"'s `AppUser` row, and `DocumentKind = Original`

#### Scenario: The uploader reference survives the uploader's later deactivation

- GIVEN a `ContractDocument` was uploaded by "maria"
- WHEN "maria" is later deactivated
- THEN the `ContractDocument` MUST still resolve its uploader reference to "maria"'s `AppUser` row
  and MUST still display her name

### REMOVED Requirement: Uploader as Plain Identifier

(Reason: `AppUser` now exists as a real entity, so `UploadedBy` becomes a foreign key rather than
the placeholder string this requirement existed only to justify.)
(Migration: existing string values, if any, map to a matching `AppUser` by username where one
exists; any unmatched string routes to a designated inactive legacy `AppUser` so the column stays
`NOT NULL`. No real agency data exists yet, so this path is expected to touch zero rows in
practice, but the migration MUST still handle it correctly.)

---

## Capability: rent-adjustment (MODIFIED)

This is a delta against `openspec/specs/rent-adjustment/spec.md`. All other requirements in that
living spec are unaffected and MUST continue to hold exactly as written.

### MODIFIED Requirement: Append-Only Adjustment History

Each confirmed `RentAdjustment` MUST be recorded as a new row holding the effective date, previous
canon, new canon, coefficient, the index values used, and the confirming user (`ConfirmedBy`,
nullable, to accommodate any row that predates this column with no truthful value to backfill).
Confirmed adjustments MUST NOT be edited or deleted; the history MUST remain reproducible so that
any past canon can be reconstructed without recomputation. Neither `inmobiliaria_admin` nor
`inmobiliaria_empleado` MUST hold privileges sufficient to disable or bypass the append-only
trigger; the only remaining path to alter or delete a confirmed row is a table-owner credential
used for migrations — a developer path, not an operator path.

(Previously: recorded effective date, previous canon, new canon, coefficient, and index values used,
with no confirming user, and named the trigger-disable gap as reachable by "anyone with the
application's credential".)

#### Scenario: Confirmed adjustment records who confirmed it

- GIVEN an adjustment is confirmed by "maria" with previous canon $450,000, coefficient 15%, new
  canon $517,500
- WHEN it is recorded
- THEN the `RentAdjustment` row MUST store the effective date, $450,000, $517,500, 15%, the index
  values used, and `ConfirmedBy` equal to "maria"'s `AppUser` reference

#### Scenario: History still answers the canon for a past month

- GIVEN a contract has three confirmed adjustments over its lifetime, each naming who confirmed it
- WHEN the canon in force for a specific past month is requested
- THEN it MUST be answerable from the stored adjustment history alone

#### Scenario: The column is added nullable, with no backfill attempted

- GIVEN an existing `rent_adjustments` table already has confirmed rows with no `ConfirmedBy` value
- WHEN the migration adding `ConfirmedBy` runs
- THEN the column MUST be added as nullable
- AND no `UPDATE` MUST be attempted against any existing row — the append-only trigger would reject
  it, and the migration MUST NOT attempt what it cannot do

#### Scenario: Neither application role can disable the append-only trigger to force a backfill

- GIVEN a connection authenticated as either application role
- WHEN it attempts to disable the `rent_adjustments` trigger in order to backfill `ConfirmedBy` on
  an old row
- THEN PostgreSQL MUST refuse the attempt to disable the trigger

---

## Out of Scope

Each item below is *not in this specification*, not *excluded from the product*. It names the
change that owns it:

- **The login window's visual design** — this spec defines observable authentication behavior
  (connection-as-authentication, generic failure message, role derivation); how the window looks is
  a design/implementation concern the design phase owns
- **The application host bootstrap** (DI wiring) — a design/implementation concern, not a behavior
- **Collection and receipts, and their issuer** — the collection change, which MUST create receipts
  with an issuer from the start, building on `AppUser` as this change delivers it
- **Current account** — the current-account change
- **Notifications** — the notifications change
- **A general audit log** (who viewed what, who edited a party) — explicitly out of scope; no
  consumer exists for two people in one office

---

## Tests

Derived from the requirements above, not generic CRUD coverage. Each names the requirement it
proves. Integration tests require a real Postgres (`postgres:17.6` via Testcontainers) connecting
as each application role in turn; permission and trigger behavior cannot be validated against a
fake.

1. **No credential on disk.** Configuration files and the runtime environment are searched; no
   database password is found in either.
2. **Supabase Auth is never called.** A login attempt is traced and asserted to make no Supabase
   Auth API call.
3. **Correct credentials authenticate.** An active user's own username and password open a session
   as that user.
4. **Wrong password is rejected with a generic message.** An incorrect password for a real username
   fails, and the message shown does not say "wrong password".
5. **Unknown username fails identically.** A username with no matching role fails with the exact
   same wording as test 4, proving the two cases are indistinguishable to the caller.
6. **No database access exists before login.** Before any session authenticates, no code path can
   open a connection or issue a query.
7. **Role is derived from `pg_has_role`, never a stored column.** A session's effective role is
   asserted to come from the live membership check; `app_users` has no role column to assert
   against instead.
8. **A database-side role change takes effect on next login.** Changing group membership directly
   in Postgres changes the role a subsequent login derives, with no application-side update.
9. **Admin creates a user as one unit.** Provisioning succeeds only when both the login role and
   the `app_users` row exist; neither is left orphaned.
10. **A deactivated user cannot log in.** `ALTER ROLE ... NOLOGIN` on an active user causes their
    next login attempt to fail.
11. **A deactivated user's historical rows still name them.** `ContractDocument` and
    `RentAdjustment` rows referencing a deactivated user remain readable and unchanged.
12. **A deactivated username is never reused.** Attempting to provision a new user with a name
    already assigned to a deactivated user is rejected or otherwise never produces a shared
    identity.
13. **Self password change preserves the current session.** Changing one's own password succeeds
    and the session that changed it keeps working without re-authenticating.
14. **An Admin's password reset does not disturb an open session.** Resetting a different user's
    password takes effect only on that user's next login, proven by keeping their prior session
    alive across the reset.
15. **A provisioned user must change the password before reaching anything else.** A user created by
    an Admin, authenticating for the first time, is required to set a new password and can reach no
    other part of the application until they do.
16. **An Admin reset raises the requirement again.** After an Admin resets an active user's
    password, `MustChangePassword` is set and that user's next authentication requires a change
    before anything else.
17. **Re-entering the provisional password is rejected.** Submitting the same password that was just
    authenticated with fails, and the pending requirement remains set.
18. **A voluntary change raises no requirement.** A user with nothing pending changes their own
    password; `MustChangePassword` is not set afterward and their next login requires nothing.
19. **A pending change does not confine a direct database client.** A user with
    `MustChangePassword` set connects outside the application and performs exactly the operations
    their role grants — asserting the stated limit, so nobody later mistakes the flag for
    enforcement.
20. **Password DDL survives a quote character in the password.** A password containing `'` is set
    correctly with no SQL error and no altered statement.
21. **Password DDL neutralizes an injection payload.** A password field containing
    `x'; DROP TABLE app_users; --` results in exactly that literal password, with `app_users` still
    present and unaffected afterward.
22. **An Empleado performing an Admin-only write via pgAdmin is refused by the database.** Connecting
    directly with an Empleado's own credentials and attempting to write `app_users` or to provision
    a login role returns a permission error and changes nothing — proving enforcement does not
    depend on the application. User governance is the only category of write she lacks.
23. **An Admin performing the same operation via any client succeeds.** The equivalent
    user-provisioning operation, executed as `inmobiliaria_admin`, succeeds.
24. **Neither role can disable the append-only trigger.** `ALTER TABLE rent_adjustments DISABLE
    TRIGGER ...` is refused for both `inmobiliaria_admin` and `inmobiliaria_empleado`.
25. **An Empleado can terminate a contract.** Recording notice and ending a contract succeed as
    Empleado — this is ordinary operational work, not an Admin-only operation, and no column-level
    restriction stands in its way.
26. **An Empleado can confirm a rent adjustment.** Confirming an adjustment succeeds as Empleado and
    the new row records her in `ConfirmedBy` — a positive assertion, placed here so the removed
    money clause cannot creep back in as an `INSERT` grant quietly withheld from her.
27. **Aggregate reporting is not database-enforced, and the spec says so.** Connecting as Empleado
    and running an aggregate over rows she may legitimately read succeeds at the database. The
    exclusion is application-level, asserted here so the living specification never claims a
    `GRANT` that cannot exist.
28. **No row-level restriction exists.** Both roles, granted `SELECT` on the same table, retrieve
    every row in it.
29. **A new table's GRANTs ship in the same migration.** After any migration that creates a table,
    an integration test connects as each role and confirms it can perform exactly its intended
    operations on that table — none unreachable.
30. **`UploadedBy` is a real foreign key.** `ContractDocument.UploadedBy` resolves to an `AppUser`
    row; the "plain identifier" requirement is absent from the living specification.
31. **A `ContractDocument`'s uploader reference survives the uploader's deactivation.** The FK still
    resolves and displays the uploader's name after they are deactivated.
32. **`ConfirmedBy` is added nullable with no backfill.** The migration adding the column to
    `rent_adjustments` succeeds without issuing any `UPDATE` against existing rows.
33. **A newly confirmed `RentAdjustment` records `ConfirmedBy`.** Confirming an adjustment as a
    given user stores that user's reference on the new row.
34. **The append-only trigger still rejects every update and delete after this change.** Re-run
    against the modified schema: a confirmed row cannot be altered or removed by either application
    role.
