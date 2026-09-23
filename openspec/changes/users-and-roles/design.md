# Design: Users and Roles

Domain vocabulary (Argentine terms, glossed once): **Empleado** = employee, the restricted role;
**Admin** = the agency owner's role; **locador** = lessor/owner; **locatario** = tenant. Identifiers
and code are English.

## Technical Approach

The authentication boundary is a **connection**, not a token. Nothing in this system verifies a
password: the user types one, the application composes an Npgsql connection string around it, and
PostgreSQL either accepts the handshake or does not. A successful handshake produces the session's
`NpgsqlDataSource`, and that data source is the **only** object in the process capable of reaching
the database. Before login it does not exist, so "no access before authentication" is a property of
the object graph rather than a check anybody can forget to write.

Authorization is `GRANT`. Two group roles, created and granted by SQL inside one new versioned EF
migration, applied on top of `20260918233049_AddRentAdjustments`. Neither existing migration is
edited.

Scope guard for `sdd-tasks`: the surface is one new domain namespace (`Domain/Access`), one new
infrastructure namespace (`Infrastructure/Access`), one new migration, **two** modified EF
configurations, two modified domain entities, and two WPF windows. **No navigation shell, no menus,
no repository layer, no use-case project.** The migration issues no `ALTER TABLE contracts` at all
(Decision 5), so the archived `contract-and-parties` mapping is left alone.

### Verified against the code before designing

| Claim | Verified where | Result |
|---|---|---|
| `contract_documents.uploaded_by` is a plain `text NOT NULL` column | `ContractDocumentConfiguration.cs:34-36`, `20260913215911_InitialSchema.cs:84` | Confirmed. Constructor also runs `ThrowIfNullOrWhiteSpace` (`ContractDocument.cs:39`) |
| The append-only trigger is `BEFORE UPDATE OR DELETE FOR EACH ROW` | `20260918233049_AddRentAdjustments.cs:197-199` | Confirmed. `ALTER TABLE ... ADD COLUMN ... NULL` is DDL and fires no row trigger |
| Every `RentAdjustment` property already throws on post-save modification | `RentAdjustmentConfiguration.cs:71-74` iterates `GetProperties()` | Confirmed — `ConfirmedBy` inherits this automatically, no new code |
| `contracts` has an `ended_at` column | `ContractConfiguration.cs:35`, `InitialSchema.cs:23` | **FALSE.** The column is `actual_end_date`. Spec and proposal have since been corrected; no statement in this design depends on either name (Decision 5) |
| Only `Contract.End` can reach `ContractStatus.Ended`, and it demands a reason and a non-nullable date | `Contract.cs:254-269` | Confirmed — this is why Decision 5 leaves the integrity CHECK to a follow-up |
| `Inmobiliaria.Desktop` can host testable bootstrap code | `Inmobiliaria.Desktop.csproj` references Domain only; `Inmobiliaria.Core.slnf` excludes Desktop | It cannot be covered by the Linux `core` job. See Decision 7 |
| All primary keys are `uuid`, no sequences | Both migrations | Confirmed — no `GRANT USAGE ON SEQUENCES` is needed |

## Architecture Decisions

### Decision 1 — The session is an `NpgsqlDataSource` produced *by* authentication, never resolved from a container

**Choice**: `IAuthenticator.AuthenticateAsync(username, password, ct)` returns
`AuthenticationResult`. On success the result **carries** an `ISessionDbContextFactory`; there is no
other constructor, factory method, or DI registration anywhere that yields one.

```csharp
// Inmobiliaria.Domain/Access — no EF, no Npgsql, no WPF
public interface IUserSession
{
    Guid UserId { get; }
    string Username { get; }          // == PostgreSQL rolname, lowercase
    string DisplayName { get; }
    UserRole Role { get; }            // Admin | Empleado
    bool MustChangePassword { get; }
    void ClearMustChangePassword();
}

```

```csharp
// Inmobiliaria.Infrastructure/Access — everything that speaks EF or Npgsql
public interface ISessionDbContextFactory : IAsyncDisposable { InmobiliariaDbContext Create(); }

public abstract record AuthenticationResult
{
    public sealed record Success(IUserSession Session, ISessionDbContextFactory Factory) : AuthenticationResult;
    public sealed record Rejected : AuthenticationResult;          // wrong password OR unknown user
    public sealed record NotProvisioned(string Reason) : AuthenticationResult;
}

public interface IAuthenticator
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}
```

**Where the line falls, and why it is not arbitrary**: `AuthenticationResult.Success` carries an EF
type, so `IAuthenticator` and its result live in Infrastructure, not Domain. `Domain/Access` keeps
only `AppUser`, `UserRole`, `IUserSession` and `UserSession` — no port that speaks EF or Npgsql. This
is not a preference: `ArchitectureGuardTests` fails the build if `Inmobiliaria.Domain` ever references
`Microsoft.EntityFrameworkCore` or `Npgsql`, so putting the authenticator in Domain would not
compile past CI. The ViewModels consume `IAuthenticator` directly, which is why
`Inmobiliaria.Desktop.csproj` gains its first Infrastructure project reference.

| Option | Tradeoff | Decision |
|---|---|---|
| Factory returned by authentication *(chosen)* | Nothing to resolve before login because nothing is registered. Provable by inspecting the pre-login `IServiceCollection` | **Chosen** |
| Factory registered at startup, throws until a session exists | Every call site must remember the throw is possible; the guarantee is a runtime exception, not a shape | Rejected |
| Ambient `AsyncLocal` session | Invisible coupling; a background thread silently gets no session | Rejected |

**Password lifetime**: the plaintext is handed to `NpgsqlDataSourceBuilder` and the local reference
is dropped. The data source retains the composed connection string internally for the session's
life — unavoidable, since Npgsql re-authenticates on every pooled physical connection. Residual gap,
stated plainly: a memory dump of the running process reveals the password. That is strictly smaller
than the file on disk it replaces. `SecureString` was considered and **rejected**: Microsoft
documents it as providing no protection on .NET Core, and Npgsql's connection string takes a plain
`string` regardless, so it would be ceremony with no effect.

### Decision 2 — Supavisor username composition is a single named function, and identity is never read back from it

**VERIFIED FACT** (spec Decision 9): the pooler requires the username `<role>.<projectref>`.

```csharp
// Inmobiliaria.Infrastructure/Access/SupavisorUsername.cs
public static string For(string role, string projectRef) => $"{role}.{projectRef}";
```

The composed value is used **only** to open the connection. The session's identity comes from a
single round trip immediately after the handshake:

```sql
SELECT current_user::text AS role_name,
       pg_has_role(current_user, 'inmobiliaria_admin',    'MEMBER') AS is_admin,
       pg_has_role(current_user, 'inmobiliaria_empleado', 'MEMBER') AS is_empleado;
```

Postgres returns `maria`, not `maria.abcdefghijkl` — the pooler strips the suffix. Deriving identity
from the typed string instead would produce a username that matches no `app_users` row and no
`rolname`, which is precisely the drift spec Decision 5 exists to forbid.

`Host`, `Port`, `Database`, `ProjectRef` and `SslMode` come from `appsettings.json`. **None is a
secret**; the file today contains only logging configuration and gains no credential. Precedence when
both memberships return true: **Admin wins**, recorded here so it is not discovered later.

### Decision 3 — Password DDL is executed through non-`SECURITY DEFINER` plpgsql functions that `format()` their arguments

`CREATE ROLE` and `ALTER ROLE ... PASSWORD` cannot be parameterised. The escaping happens
server-side, inside functions created by the migration:

```sql
CREATE FUNCTION app_set_role_password(target_role name, new_password text) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    EXECUTE format('ALTER ROLE %I PASSWORD %L', target_role, new_password);
END $$;
```

The application calls `SELECT app_set_role_password(@role, @password)` with **real Npgsql
parameters**. The value therefore never enters a SQL string on the client at all; `%L` is
`quote_literal` semantics and `%I` is `quote_ident`. A password of `x'; DROP TABLE app_users; --`
arrives as an opaque `text` argument, gets wrapped in escaped quotes by `format`, and becomes exactly
that literal password. There is no parse boundary for it to cross.

**The functions are deliberately NOT `SECURITY DEFINER`.** They execute as the caller, so
PostgreSQL's own privilege check still runs: a self password change works for anyone (a role may
always change its own password), and an Empleado calling `app_set_role_password('nicolas', ...)`
gets `permission denied` from the server. The function adds escaping, never privilege.

Companions created the same way: `app_create_login_role(name, text, name)` and
`app_set_role_login(name, boolean)`. `EXECUTE` is granted on the provisioning pair to
`inmobiliaria_admin` only; on `app_set_role_password` to both roles.

| Option | Tradeoff | Decision |
|---|---|---|
| Parameterised call into a `format()`-ing function *(chosen)* | No client-side SQL assembly at all; server-side escaping; caller privileges preserved | **Chosen** |
| `quote_literal` via a round trip, then concatenate client-side | Two round trips, and the concatenation still happens on the client — the hole survives | Rejected |
| `DO $$ ... $$` block | `DO` blocks cannot take parameters, so the value must be concatenated in | Rejected |
| Client-side SCRAM-SHA-256 verifier (RFC 5803), send only the hash | Removes plaintext from the wire and from every log. But it is hand-rolled crypto in a 5-person team for a 2-user system | Rejected — **kept as the documented escape hatch** if Decision 4 finds `log_statement = 'all'` |

### Decision 4 — `log_statement` is checked before slice 4 ships, and the function form already answers the common case

**How to check**, from any authenticated session:

```sql
SELECT name, setting FROM pg_settings
 WHERE name IN ('log_statement', 'log_min_duration_statement', 'log_parameter_max_length');
```

If `setting` comes back `NULL`, the GUC is masked for a non-superuser and the value must be read from
the Supabase dashboard (Project Settings → Database → Logs) instead. Three outcomes, three actions:

| Observed | Meaning | Action |
|---|---|---|
| `log_statement = 'none'` or `'mod'` | No DDL is logged | Ship as designed |
| `log_statement = 'ddl'` | DDL statement text is logged | **Already neutralised by Decision 3.** The client issues `SELECT app_set_role_password(...)`, whose top-level command tag is `SELECT`, not DDL. The `ALTER ROLE` runs inside plpgsql `EXECUTE` and is not a logged top-level statement |
| `log_statement = 'all'`, or `log_min_duration_statement = 0` | Every statement **and its bind parameters** are logged | **STOP.** The password reaches the log. Do not ship in-app password management until it is off, or adopt the rejected SCRAM client-side hashing from Decision 3 |

This is a go/no-go for slice 4 and belongs in `tasks.md` as an explicit gate, not as a note.

### Decision 5 — `contracts` gets a plain table-level `UPDATE` for both roles, and the migration touches it not at all

Termination is ordinary operational work, so the Empleado owns it. That is the entire argument, and
it is deliberately shorter than it used to be: an earlier version of this paragraph reasoned that
termination "moves no money and writes no append-only history", which reached the right answer
through the wrong test. **That test — the money-and-append-only clause — has itself been removed
from the spec** (spec Decision 15). The rule is now a single positive statement with exactly two
exclusions, system governance and aggregate business reporting, and termination is neither.

An earlier draft of this design defended a column-level exclusion on the termination columns; that
exclusion has been removed from the spec and **must not reappear here**.

```sql
GRANT INSERT, UPDATE ON contracts TO inmobiliaria_admin, inmobiliaria_empleado;
```

That is the whole of it. The consequences are all simplifications:

- **No column enumeration.** The earlier shape had to list every `contracts` column by name, because
  `GRANT UPDATE (a, b, c)` has no "all except" syntax. That list was also a standing trap: a column
  added to `contracts` by a later migration would silently not be updatable by Empleado until
  somebody remembered to extend it. Both the list and the trap are gone.
- **The migration now issues no `ALTER TABLE contracts` whatsoever.** Its only live-table changes are
  to `contract_documents` and `rent_adjustments`. `contracts` is read by the GRANT and otherwise
  untouched — the strongest available guarantee that the archived `contract-and-parties` capability
  is not disturbed by this change.
- **`ended_at` never mattered.** The verification that the column is actually `actual_end_date`
  (`ContractConfiguration.cs:35`, `InitialSchema.cs:23`) stands and was worth finding — the spec and
  proposal have since been corrected — but no statement in this design now depends on either name.

**The CHECK constraint is dropped from this change, deliberately.** The earlier draft added
`CHECK ((status = 'Ended') = (actual_end_date IS NOT NULL AND end_reason IS NOT NULL))`. Its
justification was access control: it was what stopped an Empleado who could write `status` but not
the other two from producing an `Ended` contract with no date and no reason. With the restriction
gone, that justification evaporates entirely, and three pieces of evidence say the remaining
data-integrity argument does not carry it on its own:

1. **The domain already enforces it.** `Contract.End(reason, actualEndDate)` throws when `reason` is
   null and takes a non-nullable `DateOnly`, and it is the only path to `ContractStatus.Ended`
   (`Contract.cs:254-269`). No violating row is reachable through the application.
2. **No spec test asks for database-level proof.** The append-only trigger earned its place because
   spec test 33 demands a raw-SQL attempt against the engine. Nothing comparable exists here, so the
   CHECK would ship with no requirement behind it — which this project's verify rules would correctly
   report as a finding in the opposite direction.
3. **It costs a scope expansion.** The constraint requires editing `ContractConfiguration.cs`, a
   mapping owned by the already-archived `contract-and-parties` change, plus an `ALTER TABLE` on a
   live table — reintroducing exactly the coupling the first bullet above just removed.

I agree with the coordinator's judgement and record the constraint as a **follow-up for the
collection change**, which is the first change that writes money against a contract's lifecycle
state and therefore the first that has a real reason to want it at the database. Recorded in Open
Questions so it is carried, not forgotten.

**The residual UI-only case is still named, not hidden**, and the spec's own example is now the
honest one: Empleado legitimately holds `SELECT` on `contracts`, so `SELECT sum(monthly_rent) FROM
contracts` succeeds in pgAdmin and **no `GRANT` can prevent it** — an aggregate over rows a role may
already read is not a privilege PostgreSQL models. The exclusion from aggregate business reporting is
application-enforced only. Spec test 26 asserts precisely that, so the living specification can never
quietly claim a `GRANT` that cannot exist. The same limit applies to any two future operations writing
the same already-granted columns of the same table.

### Decision 6 — `must_change_password` is cleared through one `SECURITY DEFINER` function, not by granting an `UPDATE`

An Empleado must clear her own flag, and only her own. Granting
`UPDATE (must_change_password) ON app_users` would let her clear anybody's. RLS is forbidden by the
spec ("No Row-Level Restriction") and would be the wrong tool anyway — the rule is about a write, not
about which rows are visible.

```sql
CREATE FUNCTION app_clear_must_change_password() RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    UPDATE app_users SET must_change_password = false WHERE username = current_user;
END $$;

REVOKE ALL ON FUNCTION app_clear_must_change_password() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app_clear_must_change_password() TO inmobiliaria_admin, inmobiliaria_empleado;
```

This is the **only** `SECURITY DEFINER` object in the system, its body is four lines, its
`search_path` is pinned (the standard hardening for definer functions), and it can affect exactly one
boolean column of exactly one row — the caller's own. Setting the flag is an Admin action and uses the
Admin's plain `UPDATE` grant on `app_users`.

**"The new password must differ from the current one" — the honest mechanism.** The application never
sees a stored verifier and cannot compute one. In the forced-change flow the user typed the current
password seconds earlier, in the same interaction, so the comparison is an ordinal string comparison
against that in-memory value, which is discarded the moment the change succeeds. In a voluntary
change the dialog asks for the current password and proves it by opening a throwaway connection with
it. **Limit, stated so nobody overstates it**: this rejects re-entering *the password just used*. It
cannot detect reuse of an older password, and the flag confines nobody at the database — a user with
the flag set who opens pgAdmin has exactly the privileges her role grants. Decision 14 of the spec is
credential hygiene; this design does not make it more than that and must not be described as if it
does.

### Decision 7 — The pre-login composition root lives in `Infrastructure`, so the Linux `core` job can prove it is empty

`Inmobiliaria.Desktop` is excluded from `Inmobiliaria.Core.slnf` and only builds on Windows, so any
test placed there cannot gate a merge through the `core` job. The registration itself is therefore a
plain cross-platform static method, and `App.xaml.cs` only calls it:

```csharp
// Inmobiliaria.Infrastructure/Access/ApplicationServices.cs
public static IServiceCollection AddPreLoginServices(this IServiceCollection services, ConnectionEndpoint endpoint);
```

Two guards, both in `Inmobiliaria.Infrastructure.Tests` and therefore inside `core`:

1. **Container guard** — build the pre-login `ServiceCollection` and assert no `ServiceDescriptor`
   whose service or implementation type is assignable to `DbContext`, `NpgsqlDataSource`,
   `NpgsqlConnection`, or `ISessionDbContextFactory`. Same shape as the existing
   `ArchitectureGuardTests`.
2. **Source guard** — walk `src/` from the repo root and assert that `MigrateAsync(`, `Migrate(` and
   `INMOBILIARIA_DB` appear **only** in `DesignTimeDbContextFactory.cs` and the test fixture. Stated
   limitation: this is a lexical guard, not a semantic one. A Roslyn analyzer was considered and
   rejected as disproportionate for one rule.

`DesignTimeDbContextFactory.cs` stays **byte-identical**. Its XML doc gains one sentence recording
that `INMOBILIARIA_DB` is a development-time path the application never reads.

New package references this forces: `Microsoft.Extensions.DependencyInjection.Abstractions` on
Infrastructure, and `CommunityToolkit.Mvvm` + `Microsoft.Extensions.DependencyInjection` on Desktop.
All three need `PackageVersion` entries in `Directory.Packages.props`.

### Decision 8 — `AppUser`, and the two schema deltas

| Column | Type | Note |
|---|---|---|
| `id` | `uuid` PK | `Guid.CreateVersion7()` client-side, unchanged from the archived convention |
| `username` | `text NOT NULL UNIQUE` | Equals `rolname`. `CHECK (username = lower(username))` — "Maria" and "maria" would otherwise be two rows behind one role |
| `display_name` | `text NOT NULL` | The only thing Postgres does not already know |
| `is_active` | `boolean NOT NULL DEFAULT true` | Paired with `ALTER ROLE ... NOLOGIN`, never `DROP ROLE` |
| `must_change_password` | `boolean NOT NULL DEFAULT false` | Decision 6 |

**No role column.** Spec Decision 5.

**`contract_documents`** — in the `Up`, in order: add `uploaded_by_user_id uuid NULL`; seed a fixed,
hard-coded legacy UUID row (`username = 'legacy'`, `is_active = false`); `UPDATE ... SET
uploaded_by_user_id = COALESCE((SELECT u.id FROM app_users u WHERE u.username = d.uploaded_by),
'<legacy uuid>')`; `SET NOT NULL`; `DROP COLUMN uploaded_by`; add the FK with
`ON DELETE RESTRICT` (deleting a user must never cascade into documents, and users are never deleted
anyway). The `Down` reverses it: re-add `uploaded_by text`, write usernames back by join, **then**
drop the FK and the uuid column. The legacy UUID is hard-coded rather than generated so `Down` can
find it. Expected to touch zero rows today; the path still has to be right.

**`rent_adjustments`** — `ADD COLUMN confirmed_by uuid NULL` plus the FK, and **no `UPDATE`
whatsoever**. Adding a nullable column with no default is a catalog-only change in PG 11+ and fires
no row trigger; validating the new FK reads rows rather than writing them, and every existing value
is `NULL`. `RentAdjustmentConfiguration`'s existing `SetAfterSaveBehavior(Throw)` loop picks the new
property up with no new code.

`RentAdjustment.Confirm` and `Contract.ConfirmAdjustment` take **`Guid confirmedBy` as a required,
non-nullable parameter** even though the column is nullable. Nullable exists only for rows that
predate the column; a *new* row that forgets to name its confirmer is the exact defect this change
exists to prevent, so the compiler refuses it. Rejected: a `Guid? confirmedBy = null` optional
parameter — it would compile every existing call site untouched and silently write nulls forever.
Cost, measured: **26 call sites across 5 test files** must be updated.

**Removing the money clause makes this column load-bearing rather than decorative, which is worth
stating plainly.** Under the earlier draft only the Admin could insert into `rent_adjustments`, and
there is exactly one Admin, so the *role* already answered "who confirmed this?" and `ConfirmedBy`
was close to a formality — a second copy of a fact the GRANT set implied. Now both roles write the
table, so two different people can confirm adjustments and **the column is the only record of which
one did**. Nothing else in the schema distinguishes them: `confirmed_at` gives a timestamp, not an
identity, and the row is append-only, so the answer cannot be reconstructed later from anything. The
required non-nullable parameter therefore stops being defensive tidiness and becomes the mechanism
that keeps the append-only history answerable. This strengthens spec Decision 11's "last cheap
moment" argument rather than weakening it: the moment is not merely cheap now, it is the only one —
the trigger rejects every `UPDATE`, so a `ConfirmedBy` left null today is null permanently.

### Decision 9 — The GRANT set, and how the first Admin comes into existence

Roles are created idempotently (`IF NOT EXISTS (SELECT FROM pg_roles ...)`, since
`CREATE ROLE IF NOT EXISTS` does not exist) as `NOLOGIN` group roles. Baseline:

```sql
GRANT USAGE ON SCHEMA public TO inmobiliaria_admin, inmobiliaria_empleado;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO inmobiliaria_admin, inmobiliaria_empleado;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO inmobiliaria_admin, inmobiliaria_empleado;
```

`ALTER DEFAULT PRIVILEGES` is written **without `FOR ROLE`** so it binds to the current user — the
migration owner, whoever that is. That is `postgres` on Supabase and `inmobiliaria_test` under
Testcontainers, and the statement is identical in both.

| Table | Empleado | Admin |
|---|---|---|
| all thirteen | `SELECT` | `SELECT` |
| `parties`, `units`, `contract_parties`, `contract_units`, `contract_documents` | `INSERT`, `UPDATE` | same |
| `economic_indices`, `index_values`, `adjustment_clauses`, `adjustment_clause_indices` | `INSERT`, `UPDATE` | same |
| `contracts` | `INSERT`, `UPDATE` on all columns, termination included | same |
| `rent_adjustments`, `rent_adjustment_index_values` | `INSERT` | same |
| `app_users` | `SELECT` + `EXECUTE app_clear_must_change_password()` | `SELECT`, `INSERT`, `UPDATE`, plus `EXECUTE` on the provisioning functions |

**Only one row differs between the two roles, and it is user governance.** That is the whole of the
asymmetry, and it now matches the spec's exclusion list exactly: everything operational is granted
identically to both, and the single divergence is `app_users` plus the privileged functions that
create and disable login roles. Aggregate business reporting — the other exclusion — appears in no
row because it cannot: no `GRANT` forbids an aggregate over rows a role may already read
(Decision 5).

**`rent_adjustment_index_values` must be granted alongside `rent_adjustments`, not after it.**
`Contract.ConfirmAdjustment` appends the adjustment and its snapshotted index-value rows in one
`SaveChanges`, so granting only the parent table would fail the transaction halfway with a
permission error on the child.

No `DELETE` anywhere, no ownership, no schema-level privilege beyond `USAGE`. **Neither role owns any
table**, which is what makes `ALTER TABLE rent_adjustments DISABLE TRIGGER` fail for both —
PostgreSQL requires table ownership for it, so this needs no extra revoke, only the absence of
ownership. Granting Empleado `INSERT` does not weaken that by a step: `INSERT` and table ownership
are unrelated privileges, and the append-only trigger fires on `UPDATE`/`DELETE`, neither of which
either role holds on any table.

**Bootstrapping the first Admin is a runbook, and cannot be anything else** — there is no Admin yet to
create one. `docs/runbooks/bootstrap-first-admin.md` holds one transaction, run once with the owner
credential: `CREATE ROLE ... LOGIN PASSWORD`, `ALTER ROLE ... CREATEROLE`,
`GRANT inmobiliaria_admin, inmobiliaria_empleado TO <admin> WITH ADMIN OPTION`, and the matching
`INSERT INTO app_users` with `must_change_password = true`. Every subsequent user is created from
inside the application (spec Decision 6). `ADMIN OPTION` on **both** group roles is required because
granting an existing group role needs it; whether PostgreSQL 17 lets a member exercise an inherited
admin option is a behaviour slice 4's integration test must **prove**, not assume — see Open
Questions.

### Decision 10 — The forced password change is a construction order, not a guard clause

Two windows, two ViewModels, both `ObservableObject` from `CommunityToolkit.Mvvm`:

| Component | Depends on | Boundary |
|---|---|---|
| `LoginViewModel` | `IAuthenticator` | Holds `Username`, `Password`, `IsBusy`, `ErrorMessage`, and one `LoginCommand`. Knows nothing about Npgsql, connection strings, or `pg_has_role` |
| `ChangePasswordViewModel` | `IPasswordService`, `IUserSession` | `IsForced` toggles whether cancel exists. Nothing else changes between the two uses |
| `App.xaml.cs` | `AddPreLoginServices` | The composition root and the sequence below |

```
App.OnStartup
   └─ AddPreLoginServices()            container holds NO data source, NO DbContext
   └─ show LoginWindow (modal)
        └─ IAuthenticator.AuthenticateAsync ──► Npgsql handshake ──► 28P01 ⇒ Rejected
                                                        │
                                              SELECT current_user, pg_has_role(...)
                                                        │
                                              load app_users row by username
                                                        ▼
                                        Success(IUserSession, ISessionDbContextFactory)
   └─ if session.MustChangePassword
        └─ show ChangePasswordWindow (modal, IsForced) ──► SELECT app_set_role_password(@role,@pw)
                                                      └─► SELECT app_clear_must_change_password()
                                                      └─► rebuild the session data source
            (closed without success ⇒ dispose the session and return to LoginWindow)
   └─ construct MainWindow  ← reached only past both gates
```

`MainWindow` is never constructed while the flag is pending, so there is no `if (MustChangePassword)`
inside any screen for a future maintainer to forget. Visual design is out of scope for this document
— the user is approving the mockup separately.

**One generic failure message.** `Rejected` covers wrong password *and* unknown username and renders
identical wording; `28P01` and "role does not exist" are never distinguished to the caller.
`NotProvisioned` (authenticated, but no `app_users` row, or `is_active = false`, or member of neither
group) is deliberately a **different** message — it is not a credential problem, and a generic message
would send the user hunting for a typo in a password that was correct.

## Data Flow

```
  appsettings.json (host, port, database, projectref — no secret)
        │
        ├──► SupavisorUsername.For("maria", ref) ──┐
  typed password ─────────────────────────────────┤
                                                   ▼
                                        NpgsqlDataSourceBuilder ──► handshake
                                                   │                    │
                                       28P01 / no role                 ok
                                                   ▼                    ▼
                                              Rejected     current_user + pg_has_role
                                           (generic msg)               │
                                                                        ▼
                                                        IUserSession + ISessionDbContextFactory
                                                                        │
                            ┌───────────────────────────────────────────┤
                            ▼                                           ▼
             every later DbContext in the session            SELECT app_set_role_password(@r,@p)
             (GRANTs of maria's role apply per statement)     format('%I','%L') escapes server-side
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Inmobiliaria.Domain/Access/AppUser.cs`, `UserRole.cs`, `IUserSession.cs`, `UserSession.cs` | Create | The whole domain surface; no EF, Npgsql or WPF reference (Decision 1) |
| `src/Inmobiliaria.Domain/Leasing/ContractDocument.cs` | Modify | `string UploadedBy` → `Guid UploadedByUserId`; drop `ThrowIfNullOrWhiteSpace`, reject `Guid.Empty` |
| `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs` | Modify | `Guid ConfirmedBy` (required parameter, nullable column) through `Confirm` |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modify | `ConfirmAdjustment` gains `Guid confirmedBy` |
| `src/Inmobiliaria.Infrastructure/Access/SupavisorUsername.cs`, `ConnectionEndpoint.cs`, `IAuthenticator.cs`, `AuthenticationResult.cs`, `NpgsqlAuthenticator.cs`, `ISessionDbContextFactory.cs`, `SessionDbContextFactory.cs`, `IPasswordService.cs`, `PostgresPasswordService.cs`, `IUserProvisioning.cs`, `PostgresUserProvisioning.cs`, `ApplicationServices.cs` | Create | Every port that speaks EF or Npgsql, plus their adapters and the composition root |
| `.../Persistence/Configurations/AppUserConfiguration.cs` | Create | Table, unique + lowercase username |
| `.../Persistence/Configurations/ContractDocumentConfiguration.cs` | Modify | FK replaces the text column; doc comment corrected |
| `.../Persistence/Configurations/RentAdjustmentConfiguration.cs` | Modify | `confirmed_by` + FK |
| `.../Persistence/InmobiliariaDbContext.cs` | Modify | `DbSet<AppUser>`; thirteenth table; class comment updated |
| `.../Persistence/DesignTimeDbContextFactory.cs` | Modify | One doc sentence. **No behavioural change** |
| `.../Persistence/Migrations/*_AddUsersAndRoles.cs` | Create | `app_users`, both schema deltas, two group roles, the full GRANT set, four SQL functions, and a matching `Down`. **No `ALTER TABLE contracts`** |
| `src/Inmobiliaria.Desktop/App.xaml.cs`, `LoginWindow.xaml{,.cs}`, `LoginViewModel.cs`, `ChangePasswordWindow.xaml{,.cs}`, `ChangePasswordViewModel.cs` | Create/Modify | Bootstrap and the two windows |
| `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj`, `Directory.Packages.props` | Modify | Infrastructure project reference; three new package versions |
| `docs/runbooks/bootstrap-first-admin.md` | Create | The one transaction that cannot be run from inside the application |
| `tests/Inmobiliaria.Domain.Tests/*` (5 files) | Modify | 26 call sites updated for the two signature changes |
| `tests/Inmobiliaria.Infrastructure.Tests/RolePermissionTests.cs`, `AuthenticationTests.cs`, `PasswordDdlTests.cs`, `CompositionGuardTests.cs` | Create | Everything that needs a real engine or a real container |
| `openspec/config.yaml` | Modify | `credential-exposure` → `resolved_decisions`; `data-api-disabled` rationale corrected; the grant convention added |

## Testing Strategy

The 34 numbered spec tests mapped onto layers. `Database.Migrate()`, never `EnsureCreated()` — this
migration's roles, GRANTs and functions are hand-written SQL that `EnsureCreated` silently omits,
which would produce green permission tests against a database enforcing nothing.

| Spec tests | Layer | Why there |
|---|---|---|
| 1, 6 | Infrastructure (no container) | Configuration scan + the pre-login `ServiceCollection` and source guards of Decision 7. Runs in `core` on every PR |
| 2 | Infrastructure (no container) | Assert the project graph has no Supabase Auth package and the login path opens exactly one Npgsql connection |
| 3, 4, 5, 10 | Integration, container | Real roles created in the fixture; a failed handshake is a real `PostgresException` |
| 7, 8 | Integration, container | `pg_has_role` is the thing under test; change membership between two logins |
| 9, 12, 16 | Integration, container | Provisioning is a transaction; the orphan case must be proven by rollback |
| 11, 31 | Integration, container | FK survival across `is_active = false` |
| 13, 14 | Integration, container | Two live sessions; the second must survive a password change made by the first |
| 15, 17, 18 | **Split** | The database half (set/clear via `app_clear_must_change_password`) is integration; the "cannot reach anything else" half is the construction order of Decision 10, asserted over the bootstrap sequence, not over a WPF window |
| 19 | Integration, container | The honest-limit test: a flagged user's privileges are byte-identical outside the app |
| 20, 21 | Integration, container | `o'brien55` and `x'; DROP TABLE app_users; --` are set as literal passwords; `app_users` still exists; the payload password then authenticates |
| 22, 23 | Integration, container | The **only** asymmetry left, so this pair now tests user governance rather than adjustments: as Empleado, a direct `INSERT INTO app_users` and a call to the provisioning function are both refused by the engine; the Admin's equivalents succeed. A better example than the old one, because governance is the sole category of write she lacks |
| 24, 28 | Integration, container | Connect as each role in turn; ownership-based and row-level refusals come from the engine, not from the app |
| 25 | Integration, container | Positive case: as Empleado, `GiveNotice` **and** `End` both succeed, writing `status`, `actual_end_date` and `end_reason`. The standing guard against the removed column-level restriction |
| 26 | Integration, container | Positive case, deliberately mirroring 25: as Empleado, confirming an adjustment succeeds and the new row records **her** in `confirmed_by`. The standing guard against the removed money clause returning as an `INSERT` grant quietly withheld. Must insert into `rent_adjustments` **and** `rent_adjustment_index_values` in one transaction, which is what proves the paired grant of Decision 9 |
| 27 | Integration, container | As Empleado, `SELECT sum(monthly_rent) FROM contracts` **succeeds**. An assertion that the database does *not* enforce something, written so the living specification can never claim a `GRANT` that cannot exist |
| 29 | Integration, container | Enumerate `information_schema.tables` and assert every one is `SELECT`-able by both roles — the regression net for the "forgot the GRANT" risk |
| 30, 32 | Integration, container | `information_schema.columns` + `pg_constraint`: `uploaded_by` is gone, `confirmed_by` is nullable, and the migration issued no `UPDATE` |
| 33, 34 | Integration, container | Confirm as a user, then re-run the archived append-only assertions against the modified schema |

One test falls out of this design rather than out of the spec, and is named here so it is not
mistaken for scope creep: Decision 9's `ADMIN OPTION` question — an Admin login role created by the
runbook must actually be able to `GRANT inmobiliaria_empleado` to a role it creates. (The earlier
draft named a second one, for Decision 5's CHECK constraint; that constraint moved to a follow-up,
and its test went with it.)

## Threat Matrix

N/A for the listed boundaries — no routing, shell command, subprocess, VCS/PR automation,
executable-file classification, or process-integration surface. One injection surface does exist and
is treated as a design requirement rather than left implicit:

| Surface | Safe behaviour | Failure behaviour | Test |
|---|---|---|---|
| Password value reaching `ALTER ROLE` / `CREATE ROLE` | Passed as an Npgsql parameter into a `format('%L')` function; never concatenated client-side (Decision 3) | Any statement other than the intended one executes | Spec tests 20, 21 |
| Role name reaching the same DDL | `format('%I')`, and the value is always `current_user` or a validated `[a-z][a-z0-9_]*` username | An identifier escapes its quoting | Provisioning test with a hostile username |
| Password reaching the Postgres log | `log_statement` verified before slice 4; the `SELECT`-wrapped form is not DDL (Decision 4) | `log_statement = 'all'` writes bind parameters | Manual gate, recorded in `tasks.md` |
| Connection string composed from user input | `NpgsqlDataSourceBuilder`, never string interpolation — a `;` in a password would otherwise start a new keyword | Keyword injection into the connection string | Password containing `;Pooling=false` authenticates unchanged |

## Migration / Rollout

One migration, `AddUsersAndRoles`, generated with
`dotnet ef migrations add AddUsersAndRoles -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.
The roles, GRANTs and four functions are appended to `Up` via `migrationBuilder.Sql(...)` with exact
inverses first in `Down`. Rollback target is
`dotnet ef database update 20260918233049_AddRentAdjustments`; neither live migration is edited. The
`Down` order is fixed and must not be reordered: restore `uploaded_by` as text and write usernames
back **first**, then drop FKs and `confirmed_by`, then `DROP FUNCTION`, then `REVOKE ALL` and
`DROP ROLE` the two **group** roles only — never an individual login role, whose password exists
nowhere else. Rolling back reinstates the stored credential; that is stated in the proposal and is
not softened here. A developer applies the migration manually; the application never calls
`Database.Migrate()`.

**Size forecast — 1,910–2,410 authored lines, five chained PRs.**

Two revisions have now moved this number, and they moved it in opposite directions. Removing the
column-level termination restriction took roughly **110–120 lines off** the original 2,000–2,500.
Removing the money clause puts about **20–30 lines back on**, which is the opposite of what one
would expect from a restriction being deleted, so the arithmetic is itemised rather than asserted:

| Item | Lines |
|---|---|
| **Revision 1 — termination restriction removed** | |
| Enumerated per-column `GRANT UPDATE` on `contracts` and its comment | −20 |
| The CHECK constraint in the migration's `Up` and `Down` | −10 |
| `ContractConfiguration.cs`'s mirroring `HasCheckConstraint` and doc comment | −12 |
| The design-derived CHECK test and the old two-assertion test 25 | −45 |
| The `conventions` entry about amending grants for a new `contracts` column | −15 |
| Two new tests gained (25 positive termination, 27 aggregate-not-enforced) | +55 |
| **Revision 2 — money clause removed** | |
| The split `rent_adjustments` grant collapses into one statement covering both roles | −5 |
| Tests 22 and 23 rewritten from adjustments to user governance — same shape, same length | ±0 |
| **New test 26**: a positive integration test that confirms an adjustment as Empleado. It needs a seeded contract, clause and index values before it can assert anything, even reusing `SchemaConstraintTests`' existing `SeedContractWithClause` helper | **+30** |
| **Net across both revisions** | **≈ −90** |

**Why the second revision grows the change slightly**: a *restriction* is cheap to delete — it was
one `GRANT` naming one role instead of two. The *guard against its return* is not, because a
positive assertion has to build a valid adjustment before it can confirm one, while the negative
assertion it replaces only had to attempt an `INSERT` and catch the error. That is a good trade and
worth paying for: tests 25 and 26 are what stop this change's most repeated mistake from being made
a third time. But it is a trade, not a saving, and reporting it as a saving would be wrong.

**The slice plan does not change** — five PRs, same seams, same forced ordering. Revision 1's saving
lands almost entirely in slice 2b, the slice the proposal flagged as most likely to overrun; revision
2's addition lands in slice 3 with the other role-permission tests. Still well over the 800-line
budget; `Decision needed before apply` and the formal guard lines belong to `sdd-tasks`.

| PR | Contents | Est. lines | Seam rationale |
|---|---|---|---|
| 1 | `Domain/Access` (4 files) + domain tests | 240–320 | Depends on nothing. The FK target must exist before anything points at it |
| 2a | `AppUserConfiguration`, the two entity changes, **two** configuration edits, the 26 test call sites | 360–440 | Compiles and stays green with no migration yet |
| 2b | The migration: table, both deltas, roles, GRANTs, four functions, `Down` + schema tests (29, 30, 32) | 365–455 | The proposal named slice 2 as most likely to overrun; this is the pre-planned split, now with more headroom |
| 3 | `Infrastructure/Access` ports and adapters + tests 1–8, 13, 19–28, 34 | 570–670 | Authentication cannot be tested before the roles exist. Absorbs all three positive/negative guard tests (25, 26, 27) |
| 4 | In-app provisioning/reset/deactivation + tests 9–12, 14, 16–18 | 380–470 | The slice the reversal added; it needs slice 3's session |
| 5 | Desktop bootstrap, both windows and ViewModels, packages | 400–500 | Named, not smuggled. No navigation, no menus, no second screen |

## Open Questions

- [ ] **`ADMIN OPTION` inheritance in PostgreSQL 17.** Decision 9 requires the Admin login role to
      grant `inmobiliaria_empleado` to roles it creates. Whether that works through inherited
      membership or needs the grant written directly on each login role must be **proven by test**
      before slice 4 is planned. If it does not work, the bootstrap runbook grows one line per user
      and in-app provisioning partially reverts to a runbook.
- [ ] **`log_statement` on Supabase** (Decision 4). `ddl` is already handled; `all` is a hard stop
      for slice 4. Must be read before slice 4 starts.
- [x] **`ended_at` does not exist** (Decision 5). Found during this design; the spec and proposal
      have since been corrected to `actual_end_date`. Closed — no statement in this design now
      depends on either name.
- [x] **Does the Empleado own contract termination?** Answered: **yes**, the whole flow, notice and
      ending alike. She owns every operational action; the project owner confirmed it. The
      column-level restriction an earlier draft defended is removed and must not reappear. Spec test
      25 is the standing guard.
- [x] **Does the Empleado own rent adjustments?** Answered: **yes**. The clause reserving to Admin
      "every operation that moves money or writes append-only history" is REMOVED, not unimplemented
      (spec Decision 15), so `rent_adjustments` and `rent_adjustment_index_values` are `INSERT` for
      both roles. Spec test 26 is the standing guard. **There is no third exclusion**: only system
      governance and aggregate business reporting, and any future design statement implying a
      financial restriction on the Empleado is a defect.
- [ ] **FOLLOW-UP, carried to the collection change** — the integrity constraint
      `CHECK ((status = 'Ended') = (actual_end_date IS NOT NULL AND end_reason IS NOT NULL))`.
      Deliberately **not** in this change (Decision 5): its access-control justification is gone,
      `Contract.End` already enforces it in the domain, no spec requirement asks for database-level
      proof, and adding it would mean editing an archived capability's mapping. The collection change
      is the first that writes money against a contract's lifecycle state and so the first with a
      real reason to want it at the database.
- [ ] `scheduler-mechanism` and `pdf-library` — untouched by this change.
- [ ] The **residual memory-dump gap** (Decision 1) is accepted, not closed. Revisit only if the
      agency ever runs this on a shared or untrusted machine.

## Key Learnings

1. The `contracts` table has no `ended_at` column; the real termination columns are `actual_end_date`
   and `end_reason`, and both the proposal and the spec have since been corrected.
2. The Empleado is excluded from exactly two things, system governance and aggregate business
   reporting, so the GRANT set differs between the two roles on the `app_users` row alone and every
   operational table is granted identically to both.
3. Password DDL becomes injection-safe by calling a plain plpgsql function with real Npgsql
   parameters and letting `format('%I', '%L')` escape server-side, which also hides the `ALTER ROLE`
   from `log_statement = 'ddl'` because the top-level command tag is then `SELECT`.
4. `Inmobiliaria.Desktop` is excluded from `Inmobiliaria.Core.slnf`, so any architecture guard placed
   there cannot gate a merge through the Linux `core` job and must live in Infrastructure instead.
5. Once both roles can write `rent_adjustments`, `ConfirmedBy` becomes the only record of who
   confirmed an adjustment, because `confirmed_at` gives a timestamp rather than an identity and the
   append-only trigger makes a null permanent.
