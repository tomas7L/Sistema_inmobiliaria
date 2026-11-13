# Exploration: revocar-permisos-api (closing Supabase's API roles out of the database)

Done 2026-11-13, read-only. Code facts verified by reading the repository; platform facts verified against Supabase's and
PostgreSQL's documentation, cited by URL. Anything not verified is marked **[inferred]**. No question here is for Nicolás:
this is the team's own security debt.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Finding** | What is open today, and why it matters even with the Data API off |
| **The three ways to close it**, and the recommendation under it | The decision this exploration exists for, with how strongly it is held |
| **What Tomás has to run** | The read-only queries on production before design, and who applies the migration |

**Skip** "What exists" and "What breaks" unless you want the detail.

## Finding

**Every table in production is fully granted to Supabase's three API roles, `anon`, `authenticated` and `service_role`,
and nothing in this repository ever takes those grants away, except on `tenant_notices`.** Verified on production
2026-11-12 (`notificaciones/design.md`, Finding F2): all 23 tables, `app_users` and `__EFMigrationsHistory` included, hold
`SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER` for all three. No table has Row Level Security, on purpose
(`openspec/config.yaml`, decision `data-api-disabled`: enforcement is by table GRANT).

These grants come from Supabase's default privileges: on existing projects, every new table in `public` is granted to the
three roles, and every new function gets `EXECUTE` ([Securing your API](https://supabase.com/docs/guides/api/securing-your-api)).

**Today they cannot be used.** `anon` and `authenticated` reach the database only through the Data API, and it is off in
production and in the test project (Tomás, 2026-11-12). With it off, "none of the auto-generated REST endpoints respond,
regardless of grants or RLS" (same page). **They are one switch away**: whoever turns the Data API on, by mistake or to try
something, opens every table, the users table included, to anyone holding the project's public key.

**Supabase announced a change of default, only for new tables, and it has not reached production.** Its changelog
schedules existing projects to stop granting *future* tables in `public` to the API roles on 2026-10-30, adding that
"Existing tables are not affected in your project, they keep their current grants and stay reachable"
([changelog 45329](https://supabase.com/changelog/45329-breaking-change-tables-not-exposed-to-data-and-graphql-api-automatically)).
**Production still has the old defaults** (Tomás's query, 2026-11-13, below): `postgres`'s defaults in `public` grant the
three API roles everything on tables (`arwdDxtm`), `EXECUTE` on functions and `rwU` on sequences. So the 23 tables stay
open, and every table a migration creates is opened too, until this change.

## What exists

- **Migrations run as `postgres`** through the pooler (`docs/ESTADO.md:111-112`). Tables and functions they create are owned by
  `postgres` and get `postgres`'s default privileges, not `supabase_admin`'s, which "apply only to objects that
  supabase_admin creates" and which `postgres` cannot change
  ([KB](https://supabase.com/kb/troubleshooting/cant-change-the-default-privileges-of-supabase_admin-in-the-public-schema)).
  So everything this system creates is governed by defaults that a migration can change.
- **The pattern already exists three times**: a `DO` loop over `anon`, `authenticated`, `service_role` that revokes only
  when the role exists, because those roles exist only on Supabase (`AddTenantNotices.cs:88-101`,
  `AddClauseWindowOffset.cs:125-140`, `HardenClearMustChangePassword.cs:23-36`). The new migration reuses it.
- **The application's own access is separate and explicit**: two `NOLOGIN` group roles, `inmobiliaria_admin` and
  `inmobiliaria_empleado`, with per-table grants in each migration and a default `SELECT` on new tables
  (`AddUsersAndRoles.cs:135-163`). Operators are login roles that are members of one of them. Revoking the API roles
  touches none of this.
- **No sequences exist** (no identity, serial or `CREATE SEQUENCE`). `recibo_numbering` is a counter row.
- **Functions**: two `SECURITY DEFINER` functions are already closed to `PUBLIC` and to the API roles
  (`app_clear_must_change_password`, `app_replace_clause_indices`). Three caller-rights functions are not:
  `app_set_role_password`, `app_create_login_role`, `app_set_role_login` (`AddUsersAndRoles.cs:173-188`), each granted
  explicitly to the group roles that call them. About 17 trigger functions also keep `PUBLIC`'s `EXECUTE`.
- **Nothing in `src/` uses the Data API, a Supabase key, Storage or PostgREST.** The only Supabase reference is the pooler
  host in `appsettings.json`. `contract-documents` mentions Supabase Storage only in a comment
  (`ContractDocument.cs:7`); Storage lives in its own schema, not in `public` **[inferred from Supabase's architecture; to
  confirm in design]**.
- **The test fixture imitates Supabase**: `PostgresFixture.cs:76-85` creates the three roles and grants them `ALL` on new
  tables and `EXECUTE` on new functions, before migrating. That is what makes a revoke testable here.

## The three ways to close it

### A. Revoke the tables and the defaults, in one migration (recommended)

One migration, run as `postgres`:

1. Revoke everything on every existing table in `public` from the three API roles, with the existing guarded loop.
2. Change `postgres`'s default privileges in `public` so future tables and functions are not granted to them, as Supabase
   documents:
   `ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated, service_role;`
   and the same for `FUNCTIONS` (and `SEQUENCES`, harmless with none today). Supabase's pages write `FOR ROLE postgres`;
   without `FOR ROLE` it binds to the role running the migration, which is `postgres` on Supabase and the container's
   superuser in the tests, where no `postgres` role exists **[inferred from the image's `POSTGRES_USER`; design confirms]**.
   `AddUsersAndRoles.cs:148` already relies on that binding.
3. Revoke the API roles' `EXECUTE` on the three caller-rights functions and their `PUBLIC` `EXECUTE`, keeping the explicit
   grants to the group roles, as the two hardened functions already are.

- **For:** closes F2 for good, in the same tool every other permission in this system uses (a migration), testable with
  the fixture that already exists, and runs identically on the test database and production.
- **Against:** an EF migration whose `Down` must not restore the exposure (as `HardenClearMustChangePassword` already
  decided, line 42), and one fixture sanity check has to be rewritten (see **What breaks**).

### B. Revoke `USAGE` on schema `public` from `anon` and `authenticated`

Without `USAGE` on the schema, no object in it is reachable, whatever its grants.

- **For:** one statement, and it also covers objects nobody remembers.
- **Against:** it leaves 23 tables whose grants still say "open", so the next person reading `information_schema` sees
  exposure that is not there, and the F2 query (`notificaciones/design.md:261-264`) never comes back clean. Supabase's own
  guidance is table and default privileges, not the schema **[inferred: its pages never suggest revoking schema usage;
  some platform features may expect it]**. Good as an extra layer, weak as the fix.

### C. Do nothing in the database; keep the Data API off

- **For:** zero work; it is off today.
- **Against:** it rests on a dashboard switch nobody can see from the repository, on two projects, forever. It is the
  situation F2 was written about.

### Recommendation

**A, held strongly.** It is the fix Supabase documents, it follows a pattern this repository already uses three times, and
it is the only one of the three after which the F2 query returns no rows. **B is a reasonable addition** (held weak): worth
discussing in design only if it costs nothing in Supabase's dashboard. **C is rejected.**

**The `PUBLIC` default on functions** is the one place to go carefully. Supabase also suggests
`ALTER DEFAULT PRIVILEGES FOR ROLE postgres REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`, warning that it reaches functions in
other schemas and that callers then need explicit grants. Every function this system's code calls already has one. Trigger
functions cannot be called directly ("trigger functions can only be called as triggers"); whether a trigger still fires
for a role without `EXECUTE` on its function is **[inferred: PostgreSQL checks it when the trigger is created]**
([CREATE TRIGGER](https://www.postgresql.org/docs/17/sql-createtrigger.html) does not say when the check runs). So design
either leaves trigger functions alone, or proves it with a test that inserts as an Empleado after the revoke. Held medium.

## What breaks

- **`TenantNoticeRecordTests.SupabasesApiRoles_HoldNothing_OnTheSentRecord`** (`TenantNoticeRecordTests.cs:162-173`) proves
  the fixture works by asserting `anon` holds every privilege on `contract_events`. After A, it holds none. The sanity check
  has to move: assert it before migrating, or on a table created after the fixture's grants but outside the migrations.
- **The fixture imitates the defaults production really has** (checked 2026-11-13), for tables and functions; it sets none
  for sequences, and none exist. Supabase may still apply its announced change to production before or after this
  migration, so the migration must reach the same end state either way: revoking what is not held is not an error.
- Nothing in `src/` changes. No model change, so `has-pending-model-changes` must stay clean; the migration is SQL only.

## What Tomás has to run

**Before design, read-only, in the SQL editor of production** (no password; Supabase's dashboard runs it as `postgres`).
They say where production starts from, so the migration is written against facts:

```sql
-- 1. Current default privileges, by owner role and object type
SELECT pg_get_userbyid(defaclrole) AS owner, defaclnamespace::regnamespace AS schema, defaclobjtype AS type, defaclacl
FROM pg_default_acl ORDER BY 1, 2, 3;

-- 2. Which functions in public the API roles and PUBLIC can execute
SELECT p.proname,
       has_function_privilege('anon', p.oid, 'EXECUTE') AS anon,
       has_function_privilege('authenticated', p.oid, 'EXECUTE') AS authenticated,
       has_function_privilege('service_role', p.oid, 'EXECUTE') AS service_role
FROM pg_proc p WHERE p.pronamespace = 'public'::regnamespace ORDER BY 1;

-- 3. Whether the API roles hold USAGE on schema public
SELECT r, has_schema_privilege(r, 'public', 'USAGE') FROM unnest(ARRAY['anon','authenticated','service_role']) r;
```

The same three on the test project are useful but not required. Paste the results in the chat; they contain no secret.

**After the change is merged:** apply the migration to the test database, then production, as every other migration
(`docs/ESTADO.md`, "Aplicar una migración"), and rerun the F2 query (`notificaciones/design.md:261-264`). F2 is closed
when it returns no rows.

## What production returned (Tomás, 2026-11-13)

- **Default privileges:** `postgres` in `public` grants `anon`, `authenticated` and `service_role` everything on new tables,
  `EXECUTE` on new functions and `rwU` on new sequences, and `SELECT` on new tables to the two group roles. The same three
  rows exist for `postgres` in `storage`, and `supabase_admin` holds the same grants in `public`, `graphql` and
  `graphql_public`. Only `postgres` in `public` is this change's: the rest are Supabase's schemas, or objects only
  `supabase_admin` creates.
- **Functions:** the three API roles can execute 18 of the 20 functions in `public`: the three operator functions and 15
  trigger functions. Only the two hardened functions are closed. `reject_tenant_notice_mutation` and
  `stamp_tenant_notice` are missing, because `AddTenantNotices` is not yet applied to production.
- **Schema:** the three API roles hold `USAGE` on `public`.

All three confirm the Finding; none changes the recommendation.

## Size and risk

About 150 to 250 lines: one SQL-only migration, its tests, the fixture change, and the rewritten sanity check. **High risk,
item 2 (permissions)**: it gets the independent verifier and the gentle-ai review. It changes no stored data and is
reversible by granting again, but `Down` deliberately will not do that.
