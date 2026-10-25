# Access Control Specification

## Purpose

This capability states, once, what each role MAY do, and how that rule is enforced. The rule is written over **operations**, never over screens — `src/Inmobiliaria.Desktop/` holds only `App.xaml` and `MainWindow.xaml` today, so a screen list would be invented. Enforcement is by PostgreSQL `GRANT`, not by application code, because a rule the UI merely hides is not a rule.

## Requirements

### Requirement: The Operation Rule Is Stated Over Actions, Not Screens

Empleado MUST own every operational action. Empleado MUST be excluded from exactly two **categories**:
system governance (creating, deactivating, or resetting users) and aggregate business reporting
(agency-level income, profitability, and totals). **There is one named, bounded departure: the two go-live
parameters, the starting recibo number and a contract's cutover date, are set by an Admin only**
(`receipt`: Decision 39 of `cobranza-y-recibos`; the cutover date: this change). It is a departure, not a
new category: it is not a reservation of "money-moving" or "financially sensitive" operations, which this
requirement continues to refuse, and it MUST NOT be read as licence to reserve any other operation.

(Previously: "Empleado MUST be excluded from exactly two things ... There is no third exclusion.", which
the starting-number rule already contradicted.)

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

#### Scenario: Empleado cannot set a go-live parameter

- GIVEN "maria" holds the Empleado role
- WHEN she attempts to set the starting recibo number or a contract's cutover date
- THEN each MUST be refused by the database
- AND she MUST still be able to collect rent, void within 7 days and end a contract

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

### Requirement: The Collection Tables Are Granted Deliberately, and the Counter Is a Named Departure
Under "Every New Table Ships With Its GRANTs in the Same Migration", the migration that creates the
collection (payment event), recibo, recibo-line, annulment and numbering-counter tables MUST grant, in
the same migration, to both `inmobiliaria_admin` and `inmobiliaria_empleado` exactly:

- collections, recibos, recibo lines, annulment records: `SELECT` and `INSERT`, and nothing else.
  Neither role MUST hold `UPDATE` or `DELETE` on them (Decision 40).
- the numbering counter: `SELECT`, and `UPDATE` of the column that advances the counter, and not
  `INSERT` or `DELETE`. This is a **deliberate departure** from the insert-only pattern the other
  collection tables follow. The counter is the one row that must change, and both roles take
  payments. Its single row exists from the migration.
- the privilege to set the **starting number** is held by `inmobiliaria_admin` only
  (Decision 39). `inmobiliaria_empleado` MUST NOT be able to set it. This restriction is enforced by
  the database (a separate column-level grant or function), not by the UI.
- the annulment records are insertable by both roles, yet the time and role limits on voiding
  (Decision 45) MUST still be enforced by the database on that insert, because a grant cannot express
  them.
- the existing movements table keeps its insert-only grants; the new movement kinds need no new privilege.

**A named limit.** Under a `GRANT` on the counter's advancing column the database cannot distinguish an
increment made inside an issuance from any other `UPDATE` of that column. The protection against a tampered counter is
therefore not the grant but the unique constraint on the recibo number: a counter set back would
cause the next issuance to be refused, not to issue a duplicate. This is stated so no reader assumes
database enforcement that does not exist.

#### Scenario: Both roles can read and write exactly what they should

- GIVEN the migration has run
- WHEN each application role connects
- THEN each MUST be able to read the new tables, insert collections, recibos, lines and annulments, and advance the counter
- AND neither MUST be able to update or delete a collection, a recibo, a line or an annulment, or to insert into or delete from the counter
- AND only `inmobiliaria_admin` MUST be able to set the counter's starting number

#### Scenario: A counter set back cannot produce a duplicate

- GIVEN issued recibos up to 24270
- WHEN a role sets the counter back to 24200 with a direct `UPDATE` and a payment is then committed
- THEN the commit MUST be refused by the unique constraint
- AND no recibo with a duplicate number MUST exist

#### Scenario: The migration leaves no table unreachable

- GIVEN the migration has run
- WHEN either role queries any new table
- THEN it MUST not fail with a permission error for an operation this requirement grants

### Requirement: The Cutover Date Is Admin-Only, Enforced by the Database

Changing `contracts.cutover_date` MUST be refused by PostgreSQL unless the session's login role is a
member of `inmobiliaria_admin`, in addition to the existing rule that it cannot change once the account
holds a movement (`contracts_cutover_lock`, `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:153-173`).
The refusal MUST hold for raw SQL from any client, because `contracts` is granted `INSERT, UPDATE` to both
roles as a whole table (`20260924202549_AddUsersAndRoles.cs:161`) and a column grant cannot separate the
operation without removing the Empleado's ability to update the rest of the contract.

The refusal applies to a **change**: an `UPDATE` that names `cutover_date` without changing it, and an
`UPDATE` of any other column, MUST still succeed for an Empleado (ending a contract, entering a rate).
The migration that does this MUST NOT change the EF model; if design finds that it would, the model change
and the migration ship in one PR (project rule).

**A named limit** (Decision 23). The database cannot distinguish, in an `INSERT` of a contract, a cutover
date entered by an Empleado from one entered by an Admin, because this change hardens `UPDATE` only. An
Empleado can therefore insert a contract that carries a cutover date; she cannot change it afterwards.
This is stated so no reader assumes database enforcement that does not exist.

#### Scenario: An Admin changes the cutover date

- GIVEN a contract whose account holds no movement
- WHEN a session in `inmobiliaria_admin` updates its cutover date, directly or through the application
- THEN it MUST succeed

#### Scenario: An Empleado is refused by the database, not only hidden

- GIVEN "maria" holds `inmobiliaria_empleado` and connects with pgAdmin
- WHEN she executes an `UPDATE contracts SET cutover_date = ...` that changes the value
- THEN PostgreSQL MUST refuse it and the stored date MUST be unchanged

#### Scenario: An Empleado's other updates still work

- GIVEN a contract with a cutover date and "maria" as Empleado
- WHEN she ends the contract, or updates its honorarios percentage, or updates the contract with
  `cutover_date` named but unchanged
- THEN each MUST succeed

#### Scenario: The lock after a movement still holds for both roles

- GIVEN an account holding a movement
- WHEN an Admin, and separately an Empleado, changes the cutover date
- THEN both MUST be refused

#### Scenario: An Empleado can insert a contract with a date (the named limit)

- GIVEN "maria" as Empleado
- WHEN she inserts a new contract that carries a cutover date
- THEN the insert MUST succeed
- AND she MUST then be refused when she changes that date

#### Scenario: The migration reverses cleanly

- GIVEN the migration applied
- WHEN it is reverted
- THEN the previous trigger function MUST be restored, and no data MUST change

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

30. **An Admin changes the cutover date directly and succeeds; an Empleado changing it directly through a
    raw `UPDATE` is refused by the database. The refusing trigger must be seen failing with the Admin
    check removed before it is trusted.** *(Infrastructure.Tests)*
31. **An Empleado can still end a contract, update the honorarios percentage, and update a contract
    naming `cutover_date` without changing it.** *(Infrastructure.Tests)*
32. **The lock after a movement still refuses both roles.** *(Infrastructure.Tests)*
33. **An Empleado can insert a contract carrying a cutover date and then cannot change it (the named
    limit).** *(Infrastructure.Tests)*
34. **The migration's `Down` restores the previous function body.** *(Infrastructure.Tests)*
35. **`dotnet ef migrations has-pending-model-changes` reports no pending change after the migration.**
    *(Run by the assistant; needs no database or Docker)*
