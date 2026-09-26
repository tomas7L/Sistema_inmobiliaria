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
