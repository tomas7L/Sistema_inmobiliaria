# Desktop Shell Specification

## Purpose

What the operator sees after logging in: a main window that knows who is logged in, offers the screens
that role may use, wires everything from the session, and survives failures that are not refusals. It
holds no business rule.

## Requirements

### Requirement: The Main Window Receives the Session and Shows Who Is Working

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

### Requirement: Admin-Only Screens Are Hidden From an Empleado, and the Database Still Decides

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

### Requirement: Post-Login Services Are Wired From the Session, One Database Context per Operation

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

### Requirement: A Failure That Is Not a Refusal Never Crashes the Window

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

### Requirement: Recargo Terms and the Agency's Printed Details Come From Configuration

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

## Tests

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
