# Access Control Specification

## Purpose

This capability states, once, what each role MAY do, and how that rule is enforced. The rule is written over **operations**, never over screens — `src/Inmobiliaria.Desktop/` holds only `App.xaml` and `MainWindow.xaml` today, so a screen list would be invented. Enforcement is by PostgreSQL `GRANT`, not by application code, because a rule the UI merely hides is not a rule.

## Requirements

### Requirement: The Operation Rule Is Stated Over Actions, Not Screens

Empleado MUST own every operational action. Empleado MUST be excluded from exactly two things: system governance (creating, deactivating, or resetting users) and aggregate business reporting (agency-level income, profitability, and totals). There is no third exclusion.

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

### Requirement: Enforcement by Database GRANT — Bypassing the Application Gains Nothing

Every restriction stated by the operation rule MUST be enforced by a PostgreSQL `GRANT` bound to the connecting role, not solely by application logic. An operation forbidden to Empleado MUST be refused by PostgreSQL itself even when attempted through a client other than this application.

#### Scenario: An Empleado attempting an Admin-only operation is refused by the database, not merely hidden in the UI

- GIVEN "maria" holds `inmobiliaria_empleado` and connects with **pgAdmin**, not this application, using her own credentials
- WHEN she executes `INSERT INTO app_users (...) VALUES (...)` directly, or invokes the function that provisions a login role
- THEN PostgreSQL MUST reject it with a permission error
- AND no row MUST be written and no role MUST be created — bypassing the application gained her nothing: she still had to authenticate as herself, and she still carried only her own privileges

#### Scenario: An Admin performs the same operation directly and succeeds

- GIVEN a user holding `inmobiliaria_admin` connects with any Postgres client
- WHEN they execute the equivalent user-provisioning operation
- THEN it MUST succeed, because the grant — not the application — is what allows it

### Requirement: Neither Application Role Can Disable the Append-Only Trigger

Neither `inmobiliaria_admin` nor `inmobiliaria_empleado` MUST hold privileges sufficient to `ALTER TABLE ... DISABLE TRIGGER` on `rent_adjustments`, or to otherwise bypass its append-only enforcement. This narrows an existing accepted gap from "anyone holding the application's credential" to "whoever holds the migration-owner credential" — a developer path, not an operator one.

#### Scenario: Admin role cannot disable the trigger

- GIVEN a connection authenticated as a login role that is only a member of `inmobiliaria_admin`
- WHEN it attempts `ALTER TABLE rent_adjustments DISABLE TRIGGER ...`
- THEN PostgreSQL MUST refuse it — neither application role owns the table

### Requirement: Column-Level Limits Are Named Explicitly Where GRANTs Cannot Separate Operations

Where two distinct operations would write the same columns of the same table, and PostgreSQL's `GRANT` system cannot distinguish between them, this specification MUST name that case explicitly as enforced by the application UI only, rather than letting a reader assume database enforcement that does not exist.

#### Scenario: Aggregate business reporting is separated by the application only

- GIVEN "maria" holds the Empleado role and is legitimately granted `SELECT` on `contracts` and on the collection tables, because she needs those rows to do her daily work
- WHEN she connects with pgAdmin and runs `SELECT sum(monthly_rent) FROM contracts`
- THEN PostgreSQL MUST allow it, because the total is derived from rows she may already read and no `GRANT` can forbid an aggregate over permitted rows
- AND this specification MUST state plainly that the exclusion from aggregate reporting is enforced by the application only, and MUST NOT claim database enforcement for it

#### Scenario: An operation the database cannot distinguish is named, not implied

- GIVEN two hypothetical future operations that would both write the same already-granted columns of the same table
- WHEN access control for that case is documented
- THEN the specification MUST state plainly that the separation is UI-enforced only, and MUST NOT claim the database enforces a distinction it structurally cannot make

### Requirement: No Row-Level Restriction

Neither role MUST be restricted from any particular *row* of a table it has been granted access to. Both employees work every contract; there is no row-level rule this business needs, so no Row Level Security policy MUST filter what either role can see among the rows it is granted to read.

#### Scenario: Both roles see every row of a table they may read

- GIVEN "maria" (Empleado) and the Admin both hold `SELECT` on `contracts`
- WHEN each queries `contracts`
- THEN each MUST see every row in the table, with no row hidden from either by a row-level policy

### Requirement: Every New Table Ships With Its GRANTs in the Same Migration

Any migration that creates a new table MUST grant, in that same migration, the baseline `SELECT` and the deliberately chosen write privileges to both application roles. A table with no grants MUST NOT be shippable in a state where the application fails at runtime for both roles.

#### Scenario: A new table is usable by both roles immediately after migration

- GIVEN a migration creates a new table
- WHEN that migration completes and either role connects
- THEN each role MUST be able to perform exactly the operations this specification says it may — no table MUST be silently unreachable to a role that needs it

## Tests

Derived from the requirements above, not generic CRUD coverage. Each names the requirement it proves. Integration tests require a real Postgres (`postgres:17.6` via Testcontainers) connecting as each application role in turn; permission and trigger behavior cannot be validated against a fake.

22. **An Empleado performing an Admin-only write via pgAdmin is refused by the database.** Connecting directly with an Empleado's own credentials and attempting to write `app_users` or to provision a login role returns a permission error and changes nothing — proving enforcement does not depend on the application. User governance is the only category of write she lacks.
23. **An Admin performing the same operation via any client succeeds.** The equivalent user-provisioning operation, executed as `inmobiliaria_admin`, succeeds.
24. **Neither role can disable the append-only trigger.** `ALTER TABLE rent_adjustments DISABLE TRIGGER ...` is refused for both `inmobiliaria_admin` and `inmobiliaria_empleado`.
25. **An Empleado can terminate a contract.** Recording notice and ending a contract succeed as Empleado — this is ordinary operational work, not an Admin-only operation, and no column-level restriction stands in its way.
26. **An Empleado can confirm a rent adjustment.** Confirming an adjustment succeeds as Empleado and the new row records her in `ConfirmedBy` — a positive assertion, placed here so the removed money clause cannot creep back in as an `INSERT` grant quietly withheld from her.
27. **Aggregate reporting is not database-enforced, and the spec says so.** Connecting as Empleado and running an aggregate over rows she may legitimately read succeeds at the database. The exclusion is application-level, asserted here so the living specification never claims a `GRANT` that cannot exist.
28. **No row-level restriction exists.** Both roles, granted `SELECT` on the same table, retrieve every row in it.
29. **A new table's GRANTs ship in the same migration.** After any migration that creates a table, an integration test connects as each role and confirms it can perform exactly its intended operations on that table — none unreachable.
