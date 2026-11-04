# User Registry Specification

## Purpose

`AppUser` is the FK target every other table points at when it needs to name a person: id, username (equal to the PostgreSQL `rolname`), display name, active flag. Identity is a personal PostgreSQL login role; the account's password IS the login password there is no second credential this capability manages. This capability covers login, provisioning, deactivation, and password change. It does not cover what a role may then do — that is `access-control`.

## Requirements

### Requirement: No Database Credential Stored on Disk, and No Supabase Auth

The system MUST NOT persist any database credential in a configuration file, an environment variable read by the running application, or any other on-disk location. The password entered at login MUST exist only in the memory of that session, for its lifetime. **Supabase Auth MUST NOT be used** for authentication or session identity — decided and rejected, not merely unimplemented, so it is not reintroduced later believing it was overlooked.

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

A login attempt MUST succeed if and only if PostgreSQL accepts a connection composed from the entered username and password. The application MUST NOT independently verify the password against any stored value, because none exists to check it against.

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
- AND the message shown MUST be worded identically to the wrong-password case, so a caller cannot distinguish "no such user" from "wrong password"

### Requirement: No Database Access Before Authentication

The system MUST NOT expose any means of opening a database connection or issuing any query before a login attempt for the current session has already succeeded.

#### Scenario: The application at rest holds no connection

- GIVEN the application has just started and nobody has logged in
- WHEN any part of the application attempts to reach the database
- THEN there MUST be nothing available to reach it with — not a shared connection, not a cached credential, not a default role

### Requirement: Application Role Read From PostgreSQL, Never Mirrored

The role that governs what a session MAY do MUST be determined at login by asking PostgreSQL which group role the connecting login role belongs to (`pg_has_role` or equivalent). `AppUser` MUST NOT store a role column that duplicates this membership.

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

An Admin MUST be able to create a new **Empleado** user — a personal PostgreSQL login role together with its `AppUser` row, created as one unit — and to reset the password of a user they created, from inside the application. Deactivating a user MUST use `ALTER ROLE ... NOLOGIN` (or equivalent) together with `AppUser.IsActive = false`, and MUST NOT delete the role or the row. A username, once assigned, MUST NOT be reused for a different person.

> **Two limits PostgreSQL imposes, discovered by experiment and recorded here rather than left for a reader to hit.** Both were proven against real PostgreSQL 17 during implementation; neither is a design preference.
>
> 1. **Creating an Admin is a human runbook step, not an in-application action.**
>    `app_create_login_role` issues `GRANT <group> TO <new_role>` with no `WITH ADMIN OPTION`, and `ADMIN OPTION` is never inherited through group membership — it is scoped per grant edge. An Admin created from inside the application would therefore be structurally unable to provision anyone, which is the one thing that distinguishes the role. In-application creation is limited to Empleado accounts; `docs/runbooks/bootstrap-first-admin.md` carries the procedure for every additional Admin.
> 2. **An Admin can reset the password only of a role they themselves created.** PostgreSQL restricts `ALTER ROLE` on an existing role to that role's creator or a superuser, independent of any `CREATEROLE` or `ADMIN OPTION` the caller holds. With the single Admin this agency actually has, this is invisible. It becomes real the day a second Admin exists, and it is stated here so that day is not a surprise.

#### Scenario: Admin creates a new employee

- GIVEN the Admin is logged in
- WHEN she provisions a new user "sofia" with role Empleado
- THEN a PostgreSQL login role "sofia" MUST exist, granted membership in `inmobiliaria_empleado`
- AND an `app_users` row for "sofia" MUST exist, active, with no orphaned role or row on either side

#### Scenario: A deactivated user cannot log in, and their historical rows still name them

- GIVEN "maria" uploaded a `ContractDocument` and confirmed a `RentAdjustment` while active
- WHEN an Admin deactivates "maria"
- THEN "maria" MUST be unable to log in afterward
- AND the `ContractDocument` and `RentAdjustment` rows referencing "maria" MUST remain unchanged and readable, still naming her by that same reference

#### Scenario: A deactivated username is never reassigned

- GIVEN "maria" has been deactivated
- WHEN a new employee is provisioned to replace her
- THEN the Admin MUST create a distinct new user with a new username
- AND the system MUST NOT permit "maria"'s login role or username to be given to the new person

### Requirement: Self-Service Password Change Is Immediate; Admin Reset Takes Effect Next Login

Any authenticated user MUST be able to change their own PostgreSQL password from inside the application, and their already-open session MUST continue to work afterward. When an Admin resets a **different** user's password, that change MUST take effect only the next time the affected user authenticates — it MUST NOT disturb a session that user already has open.

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

When a user's password was set by somebody other than that user — at provisioning, or by an Admin reset — that user's next successful authentication MUST require them to set a new password before they can reach any other part of the application. The new password MUST be different from the one they just authenticated with. Setting it MUST clear the requirement. A user changing their own password while no such requirement is pending MUST NOT have one raised against them.

The pending state MUST be recorded on `AppUser.MustChangePassword`. Provisioning a user and resetting another user's password MUST both set it; a self-service change MUST clear it.

**This is credential hygiene, not a security control, and MUST NOT be presented as one anywhere in this project.** PostgreSQL has already authenticated the session before the application asks anything, so a user who connects with pgAdmin instead of the application never sees the prompt and works normally. What actually confines that user is the GRANT set, which is identical before and after the change. The requirement exists because the password the Admin typed — and therefore knows — should not stay in daily use; that intent is real and worth building, and it is all this delivers.

#### Scenario: A newly provisioned user must change the password before reaching anything else

- GIVEN an Admin has provisioned "sofia" with a provisional password
- WHEN "sofia" authenticates for the first time
- THEN the application MUST require her to set a new password
- AND it MUST NOT let her reach any other part of the application until she has

#### Scenario: An Admin reset raises the requirement again

- GIVEN "maria" has been using the system with a password she set herself
- WHEN an Admin resets "maria"'s password
- THEN "maria"'s next authentication MUST again require her to set a new password before anything else

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
- AND her permissions MUST be identical to what her role grants at any other time — this is the honest limit of the requirement, not a defect to be fixed by adding database-side enforcement

### Requirement: Password DDL Is Never Built by String Concatenation

Every statement that sets or changes a PostgreSQL password (`CREATE ROLE ... PASSWORD`, `ALTER ROLE ... PASSWORD`) MUST escape the password value through `quote_literal` or an equivalent server-side mechanism, because this DDL cannot be parameterised the way ordinary application SQL can. It MUST NOT be built by directly concatenating the raw password value into a SQL string.

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

> **The ten requirements below come from `gestion-de-usuarios`**, in the order of its Requirements 1 to 10. Those numbers, its Decisions and its Findings are in `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md`.

### Requirement: The Admin Sees Every User

The Usuarios screen MUST list every `app_users` row, active or not, with the columns and order of
Decision 7. The role MUST be derived from PostgreSQL group membership, as at login, and MUST NOT be read
from any stored column (`user-registry/spec.md:60-62`). A user who belongs to neither group MUST still be
listed, with no role shown, and MUST NOT be actionable.

#### Scenario: Admins and Empleados are listed with their role

- GIVEN an Admin "nicolas", an active Empleado "sofia" and a deactivated Empleado "maria"
- WHEN the Admin opens the Usuarios screen
- THEN all three MUST be listed
- AND "nicolas" MUST show Admin, "sofia" Empleado and active, "maria" Empleado and deactivated

#### Scenario: A pending password change is visible

- GIVEN "sofia" was just created and has not logged in yet
- WHEN the list is shown
- THEN "sofia" MUST show a pending password change

### Requirement: The Admin Creates an Empleado With a Generated Password

The Admin MUST be able to create an Empleado by typing a username (Decision 8) and a display name
(Decision 9). The system MUST generate the provisional password (Decisions P4 and 13); the form MUST NOT
offer a password field. The login role and the `app_users` row MUST be created as one unit, with
`MustChangePassword` set (`user-registry/spec.md:80,131`). A username already assigned to anyone, active or
deactivated, MUST be refused (`user-registry/spec.md:80`), and the refusal MUST NOT send any password to
the database.

#### Scenario: Creating an Empleado shows the password once

- GIVEN the Admin types username "sofia" and display name "Sofía Pérez"
- WHEN the Admin creates the user
- THEN "sofia" MUST exist as an active Empleado with a pending password change
- AND the screen MUST show the generated password (Requirement 6)
- AND "sofia" MUST be able to log in with that password and be forced to change it

#### Scenario: A taken username is refused

- GIVEN "maria" exists and is deactivated
- WHEN the Admin tries to create "maria"
- THEN the creation MUST be refused with the taken-username message
- AND nothing MUST be created

#### Scenario: An invalid username is refused before the database

- GIVEN the Admin types "josé pérez"
- WHEN the Admin tries to create the user
- THEN the creation MUST be refused with a message naming the allowed characters
- AND no database operation MUST be attempted

#### Scenario: An `ñ` is accepted

- GIVEN the Admin types "nuñez"
- WHEN the Admin creates the user
- THEN the user MUST be created as "nuñez" and MUST be able to log in as "nuñez"

#### Scenario: Mixed case is lowercased

- GIVEN the Admin types "Sofia"
- WHEN the Admin creates the user
- THEN the user MUST be created as "sofia"

### Requirement: The Admin Resets an Active Empleado's Password

The Admin MUST be able to reset the password of an active Empleado, after confirmation (Decision 10).
The system MUST generate the new provisional password and set `MustChangePassword`. A session the
Empleado already has open MUST keep working (`user-registry/spec.md:120-125`). Resetting a deactivated
Empleado MUST be refused: the Admin reactivates instead (Requirement 5).

#### Scenario: A reset forces a change at the next login

- GIVEN "sofia" is active and has set her own password
- WHEN the Admin resets it and confirms
- THEN the screen MUST show a new generated password
- AND "sofia"'s next login MUST require that password and then a change

#### Scenario: Cancelling the confirmation changes nothing

- GIVEN the Admin starts a reset of "sofia"
- WHEN the Admin cancels the confirmation
- THEN "sofia"'s password and pending state MUST be unchanged

#### Scenario: A deactivated Empleado is not reset

- GIVEN "maria" is deactivated
- WHEN a reset of "maria" is attempted
- THEN it MUST be refused, and the screen MUST NOT offer reset on her row

### Requirement: The Admin Deactivates an Empleado

The Admin MUST be able to deactivate an active Empleado, after a confirmation that names the person and
says an open session keeps working until it is closed (Finding F1). Deactivation MUST remain logical:
`NOLOGIN` and `IsActive = false`, nothing deleted, history still naming the person
(`user-registry/spec.md:80,95-100`). Deactivating an already deactivated user MUST change nothing and MUST
NOT be an error.

#### Scenario: A deactivated Empleado cannot log in

- GIVEN "sofia" is active
- WHEN the Admin deactivates her and confirms
- THEN "sofia" MUST be listed as deactivated
- AND a new login as "sofia" MUST fail with the ordinary login failure message

#### Scenario: The confirmation warns about an open session

- GIVEN the Admin starts deactivating "sofia"
- WHEN the confirmation is shown
- THEN it MUST name "sofia" and say an open session keeps working until it is closed

### Requirement: The Admin Reactivates an Empleado With a New Password

The Admin MUST be able to reactivate a deactivated Empleado, after confirmation. Reactivation MUST, in one
transaction, restore `LOGIN`, set `IsActive = true`, set a new generated provisional password and set
`MustChangePassword` (Decision P3). If any step fails, none MUST take effect. The username and the
`app_users` row MUST be the same ones: reactivation is the same person returning, never a reuse
(`user-registry/spec.md:102-107`). Reactivating an active user MUST be refused.

#### Scenario: A returning employee gets a new password

- GIVEN "maria" was deactivated while her password was "old-one-123"
- WHEN the Admin reactivates her and confirms
- THEN "maria" MUST be listed as active with a pending password change
- AND the screen MUST show a new generated password
- AND a login as "maria" with "old-one-123" MUST fail
- AND a login with the new password MUST succeed and force a change

#### Scenario: Reactivation is all or nothing

- GIVEN a reactivation whose password step fails
- WHEN the transaction ends
- THEN "maria" MUST still be deactivated, with `NOLOGIN` and `IsActive = false`

### Requirement: A Provisional Password Is Shown Once and Never Logged

Every operation that generates a password (Requirements 2, 3 and 5) MUST show it to the Admin on the
screen, together with the username it belongs to and a Copy action, until the Admin dismisses it
(Decision 11). It MUST NOT be shown again after that. The clipboard MUST be written only on Copy
(Decision 12). The password MUST be easy to dictate: lowercase words and digits separated by hyphens,
without look-alike characters, in the shape of `lago-4821-sol` (Decision P4; the word list is design's).
It MUST NOT appear in the error log, in any exception message, or in any refusal text.

#### Scenario: The password is gone after dismissing it

- GIVEN a password was generated for "sofia" and is on screen
- WHEN the Admin dismisses it
- THEN no part of the screen MUST show that password again

#### Scenario: Copy puts exactly the password on the clipboard

- GIVEN a generated password is on screen
- WHEN the Admin presses Copy
- THEN the clipboard MUST hold exactly that password, with no username and no spaces around it

#### Scenario: A failure after generating never logs the password

- GIVEN an operation generated a password and then failed with an unexpected error
- WHEN the error is recorded in `%LOCALAPPDATA%\SistemaInmobiliaria\logs`
- THEN the log entry MUST NOT contain the password

#### Scenario: Generated passwords are dictatable and differ

- GIVEN many passwords generated in a row
- WHEN they are inspected
- THEN each MUST match the dictatable shape, contain no look-alike characters, be at least 8 characters
- AND no two MUST be equal

### Requirement: Admin Rows Are Read-Only, and Foreign Roles Are Refused by Name

The screen MUST NOT offer reset, deactivate or reactivate on an Admin's row, including the logged-in
Admin's own (Decision P2). If the port is asked to act on an Admin anyway, it MUST refuse without touching
the database. If PostgreSQL refuses an action because the target role was not created by the logged-in
Admin (`42501`, `user-registry/spec.md:86`), the screen MUST show that refusal's own message, which says
the user was created by another Admin, and MUST NOT show the generic error (Decision 16).

#### Scenario: The Admin cannot act on their own row

- GIVEN the Admin "nicolas" opens the Usuarios screen
- WHEN the list is shown
- THEN no reset, deactivate or reactivate action MUST be offered

#### Scenario: A role created by another Admin is refused by name

- GIVEN "sofia" was created by Admin "nicolas", and Admin "tomas" is logged in
- WHEN "tomas" resets "sofia"
- THEN the screen MUST show the created-by-another-Admin message
- AND "sofia"'s password and pending state MUST be unchanged

### Requirement: Every Refusal Has Its Own Message

Each of these MUST reach the screen as its own operator message, not as the generic error (Decision 17):
username taken; username invalid; display name missing; user not found; user is an Admin; user already
deactivated (for reset); user already active (for reactivate); created by another Admin. Anything else
follows `desktop-shell`'s "A Failure That Is Not a Refusal Never Crashes the Window": a generic message,
the window stays open, the typed form stays filled. After any action, refused or not, the list MUST be
read again from the database (Decision 18).

#### Scenario: A refusal keeps the form

- GIVEN the Admin typed "maria" and "María Gómez" and "maria" is taken
- WHEN the creation is refused
- THEN the taken-username message MUST be shown
- AND both typed fields MUST still hold what the Admin typed

#### Scenario: The list reflects the database, not the screen

- GIVEN another Admin deactivated "sofia" a minute ago
- WHEN this Admin's next action ends, whatever it was
- THEN the list MUST show "sofia" as deactivated

### Requirement: A Chosen Password Has a Minimum Length

Every password a user chooses for themselves (the forced change and a voluntary change,
`user-registry/spec.md:109-167`) MUST be at least 8 characters and MUST NOT equal the user's username,
compared ignoring case (Decision P6). It MUST NOT require uppercase letters, digits or symbols. It MUST
accept spaces and MUST accept at least 64 characters, and MUST NOT trim what was typed (Decision 14). A
refusal MUST say what the rule is and MUST leave a pending change pending. The rule MUST NOT be applied to
passwords users already have (Decision 15). The rule MUST be testable without the desktop project (it is
outside the CI solution filter).

#### Scenario: A short password is refused

- GIVEN "sofia" must change her provisional password
- WHEN she submits "1234567"
- THEN the change MUST be refused with a message saying the minimum is 8 characters
- AND her change MUST remain pending

#### Scenario: Eight characters with no symbols is accepted

- GIVEN "sofia" must change her provisional password
- WHEN she submits "girasoles"
- THEN the change MUST succeed

#### Scenario: The username is refused as a password

- GIVEN the user "sofia1234"
- WHEN she submits "Sofia1234" as her new password
- THEN the change MUST be refused with a message saying it cannot be the username

#### Scenario: A passphrase with spaces is accepted whole

- GIVEN a user submits "la casa del lago " (with a trailing space)
- WHEN the change succeeds
- THEN the next login MUST require exactly that value, trailing space included

#### Scenario: The rule also applies to a voluntary change

- GIVEN "maria" has nothing pending and opens the change-password dialog
- WHEN she submits "abc"
- THEN the change MUST be refused

### Requirement: The Login Username Is Read the Way It Was Created

Before the connection attempt (`user-registry/spec.md:26-28`), the login MUST trim the spaces around the
typed username, lowercase it and normalize it to Unicode NFC, the same treatment the username got when it
was created (Decision 8). The password MUST be sent exactly as typed. A failed login MUST keep showing the
one generic message, worded identically for a wrong password and an unknown username
(`user-registry/spec.md:36-48`).

#### Scenario: Capitals at login still log in

- GIVEN the Empleado "nuñez" exists
- WHEN she types "Nuñez " (capital and trailing space) with her correct password
- THEN the login MUST succeed as "nuñez"

#### Scenario: The password is not touched

- GIVEN "sofia" has the password "Girasoles 1"
- WHEN she logs in typing "girasoles 1"
- THEN the login MUST fail with the generic message

## Tests

Derived from the requirements above, not generic CRUD coverage. Each names the requirement it proves. Integration tests require a real Postgres (`postgres:17.6` via Testcontainers) connecting as each application role in turn; permission and trigger behavior cannot be validated against a fake.

1. **No credential on disk.** Configuration files and the runtime environment are searched; no database password is found in either.
2. **Supabase Auth is never called.** A login attempt is traced and asserted to make no Supabase Auth API call.
3. **Correct credentials authenticate.** An active user's own username and password open a session as that user.
4. **Wrong password is rejected with a generic message.** An incorrect password for a real username fails, and the message shown does not say "wrong password".
5. **Unknown username fails identically.** A username with no matching role fails with the exact same wording as test 4, proving the two cases are indistinguishable to the caller.
6. **No database access exists before login.** Before any session authenticates, no code path can open a connection or issue a query.
7. **Role is derived from `pg_has_role`, never a stored column.** A session's effective role is asserted to come from the live membership check; `app_users` has no role column to assert against instead.
8. **A database-side role change takes effect on next login.** Changing group membership directly in Postgres changes the role a subsequent login derives, with no application-side update.
9. **Admin creates a user as one unit.** Provisioning succeeds only when both the login role and the `app_users` row exist; neither is left orphaned.
10. **A deactivated user cannot log in.** `ALTER ROLE ... NOLOGIN` on an active user causes their next login attempt to fail.
11. **A deactivated user's historical rows still name them.** `ContractDocument` and `RentAdjustment` rows referencing a deactivated user remain readable and unchanged.
12. **A deactivated username is never reused.** Attempting to provision a new user with a name already assigned to a deactivated user is rejected or otherwise never produces a shared identity.
13. **Self password change preserves the current session.** Changing one's own password succeeds and the session that changed it keeps working without re-authenticating.
14. **An Admin's password reset does not disturb an open session.** Resetting a different user's password takes effect only on that user's next login, proven by keeping their prior session alive across the reset.
15. **A provisioned user must change the password before reaching anything else.** A user created by an Admin, authenticating for the first time, is required to set a new password and can reach no other part of the application until they do.
16. **An Admin reset raises the requirement again.** After an Admin resets an active user's password, `MustChangePassword` is set and that user's next authentication requires a change before anything else.
17. **Re-entering the provisional password is rejected.** Submitting the same password that was just authenticated with fails, and the pending requirement remains set.
18. **A voluntary change raises no requirement.** A user with nothing pending changes their own password; `MustChangePassword` is not set afterward and their next login requires nothing.
19. **A pending change does not confine a direct database client.** A user with `MustChangePassword` set connects outside the application and performs exactly the operations their role grants — asserting the stated limit, so nobody later mistakes the flag for enforcement.
20. **Password DDL survives a quote character in the password.** A password containing `'` is set correctly with no SQL error and no altered statement.
21. **Password DDL neutralizes an injection payload.** A password field containing `x'; DROP TABLE app_users; --` results in exactly that literal password, with `app_users` still present and unaffected afterward.

Tests 22 to 44 come from `gestion-de-usuarios`; `R` and Decision numbers in them refer to its archived spec.

22. **The list returns every user with role, active state and pending change; the role comes from
    membership, not a column; a user in no group is listed with no role.** R1 *(Infrastructure.Tests)*
23. **Creating an Empleado makes the role and the row together, with `MustChangePassword` set, and the
    generated password logs in.** R2 *(Infrastructure.Tests)*
24. **A username taken by an active or a deactivated user is refused with no password sent and nothing
    created.** R2 *(Infrastructure.Tests)*
25. **Username validation: valid examples pass, `ñ` included; uppercase (`Ñ` included) is lowercased; a
    decomposed `ñ` is normalized to the composed one; spaces, accented vowels, a leading digit, fewer than 3
    or more than 30 characters are refused before any database call.** R2, Decision 8 *(Domain.Tests)*
26. **A reset sets a new password and `MustChangePassword`; the old password stops working; an open session
    of that user keeps working.** R3 *(Infrastructure.Tests)*
27. **Resetting a deactivated user is refused and changes nothing.** R3 *(Infrastructure.Tests)*
28. **Deactivation sets `NOLOGIN` and `IsActive = false`, deletes nothing, and a second deactivation is a
    no-op.** R4 *(Infrastructure.Tests)*
29. **Reactivation restores login, sets active, sets a new password and the pending change; the old
    password fails; the row id and username are unchanged.** R5 *(Infrastructure.Tests)*
30. **A reactivation whose password step is forced to fail leaves the user deactivated.** R5
    *(Infrastructure.Tests)*
31. **Reactivating an active user is refused.** R5 *(Infrastructure.Tests)*
32. **Generated passwords match the dictatable shape, contain no look-alike characters, are at least 8
    characters, and 1,000 in a row are all different.** R6, Decision 13 *(Domain.Tests or
    Infrastructure.Tests, per design)*
33. **The view-model shows a generated password until dismissed, then never again; Copy writes exactly the
    password and nothing writes the clipboard otherwise.** R6 *(Presentation.Tests)*
34. **An unexpected failure after a password was generated produces an error-log entry and a generic
    message, neither containing the password.** R6 *(Presentation.Tests and Infrastructure.Tests)*
35. **The view-model offers no action on Admin rows, including the logged-in Admin's; the port refuses an
    Admin target without a database call.** R7 *(Presentation.Tests, Infrastructure.Tests)*
36. **A reset or deactivation of a role created by a different Admin returns the created-by-another-Admin
    refusal, not an exception, and changes nothing.** R7 *(Infrastructure.Tests)*
37. **Every refusal in Requirement 8 maps to its own operator message; a refusal keeps the typed form; the
    list is reloaded after every action.** R8 *(Presentation.Tests)*
38. **Deactivate, reset and reactivate do nothing until confirmed, and cancelling changes nothing; the
    deactivation confirmation mentions the open session.** R3, R4, R5, Decision 10 *(Presentation.Tests)*
39. **Password rule: 7 characters refused, 8 accepted, no composition demanded, the username refused in any
    case, spaces kept, 64 characters accepted.** R9 *(Domain.Tests)*
40. **The change-password dialog refuses a password that breaks the rule, with the rule's message, in both
    the forced and the voluntary case, and a forced change stays pending.** R9 *(Infrastructure.Tests or
    Presentation.Tests, per design; never only in Desktop)*
41. **A user whose username has `ñ` is created and logs in against real PostgreSQL.** R2, Decision 8
    *(Infrastructure.Tests)*. The pooler half of this is the live smoke (Finding F4), not an automated test.
42. **The login lowercases, trims and NFC-normalizes the username and sends the password untouched: `Nuñez `
    logs in as `nuñez`; a password differing only in case fails.** R10 *(Infrastructure.Tests; never only in
    Desktop)*
43. **A failed login after normalization still shows the one generic message, identical for a wrong
    password and an unknown username.** R10, extends `user-registry` Tests 4 and 5 *(Infrastructure.Tests)*
44. **The pooler's refusal of an unknown or deactivated role is an ordinary failed login; any other `XX000` stays visible.** Found by the live smoke, where Testcontainers cannot see it because it has no pooler *(Infrastructure.Tests, `PoolerLoginRefusalTests`, a loopback socket replaying the error)*
