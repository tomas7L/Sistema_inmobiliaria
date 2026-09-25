# Runbook: Bootstrap the First Admin

This runbook exists because there is no Admin yet to create one (design.md Decision 9). It is run
**once**, manually, by a developer holding the database owner credential (`postgres` on Supabase,
the migration owner under Testcontainers). It is not, and must never become, part of the
application — the application only ever consumes users that already exist.

## Preconditions

- The `AddUsersAndRoles` migration has already been applied (`app_users` table, the two group
  roles `inmobiliaria_admin`/`inmobiliaria_empleado`, and the four SQL functions all exist).
- You are connected with the database owner credential, not through Supavisor with an application
  role.
- You have chosen the first Admin's username (lowercase, matches the PostgreSQL role name
  convention — `CHECK (username = lower(username))` on `app_users`) and a provisional password.

## The one transaction

Replace `<admin_username>` and `<provisional_password>` below. Run every statement inside a single
transaction so a failure partway leaves nothing half-created.

```sql
BEGIN;

-- 1. The personal PostgreSQL login role for the first Admin.
CREATE ROLE <admin_username> LOGIN PASSWORD '<provisional_password>';

-- 2. CREATEROLE lets this role later call app_create_login_role(...) from inside the
--    application to provision every subsequent user (spec Decision 6) — the functions
--    created by the migration are deliberately NOT SECURITY DEFINER, so they run as the
--    caller and need the caller to already hold this privilege (design.md Decision 3).
ALTER ROLE <admin_username> CREATEROLE;

-- 3. ADMIN OPTION on BOTH group roles: granting an existing group role to somebody else
--    requires ADMIN OPTION on that group role. Whether PostgreSQL 17 lets a member
--    exercise this option through INHERITED membership (rather than needing it written
--    directly on every login role) is proven — not assumed — by task 3.12's integration
--    test. If that test found inheritance does NOT work, this statement must instead be
--    repeated directly for every future Admin login role created by this runbook (see the
--    "If task 3.12 failed" section below), and the in-app provisioning function
--    (PR 4) must grant ADMIN OPTION explicitly per Admin, not rely on inheriting it from
--    this bootstrap grant.
GRANT inmobiliaria_admin, inmobiliaria_empleado TO <admin_username> WITH ADMIN OPTION;

-- 4. The matching app_users row. must_change_password = true: this password was chosen by
--    the developer running this runbook, not by the Admin who will use it (spec
--    "A Password Set by Another Person Must Be Changed Before Anything Else").
INSERT INTO app_users (id, username, display_name, is_active, must_change_password)
VALUES (gen_random_uuid(), '<admin_username>', '<Admin Display Name>', true, true);

COMMIT;
```

## If task 3.12's ADMIN OPTION proof failed

Task 3.12 (`RolePermissionTests`, Testcontainers) is the authority on whether step 3 above is
sufficient by itself. If that test recorded that PostgreSQL 17 does **not** honor inherited
`ADMIN OPTION` for a member of `inmobiliaria_admin`:

- This runbook must be re-run (or the bootstrap script extended) to grant
  `inmobiliaria_admin, inmobiliaria_empleado ... WITH ADMIN OPTION` **directly** to every
  individual Admin login role — not only the first one — because inheritance cannot be relied on.
- PR 4's in-app provisioning function must not assume an Admin caller can `GRANT
  inmobiliaria_empleado` to a newly created role purely by virtue of being a member of
  `inmobiliaria_admin`; it must be re-scoped accordingly before that PR starts (tasks.md H.3).

See task 3.12's recorded outcome in `apply-progress.md` for the actual, tested answer — this
runbook states the branch, not the resolution.

## Verifying the bootstrap succeeded

```sql
SELECT rolname, rolcanlogin, rolcreaterole FROM pg_roles WHERE rolname = '<admin_username>';
SELECT * FROM pg_auth_members
 WHERE roleid IN ('inmobiliaria_admin'::regrole, 'inmobiliaria_empleado'::regrole)
   AND member = '<admin_username>'::regrole;
SELECT id, username, display_name, is_active, must_change_password FROM app_users
 WHERE username = '<admin_username>';
```

The first Admin can now log in through the application with the provisional password and will be
required to change it before reaching anything else (spec "A Password Set by Another Person Must
Be Changed Before Anything Else").
