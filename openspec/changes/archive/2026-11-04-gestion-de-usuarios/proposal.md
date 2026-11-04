# Proposal: gestion-de-usuarios (the Admin's user screen)

Written 2026-10-25, from the exploration in `openspec/changes/gestion-de-usuarios/exploration.md`
(mirrored in Engram `sdd/gestion-de-usuarios/explore`). Vocabulary: **Admin** and **Empleado** are the
two roles (`inmobiliaria_admin`, `inmobiliaria_empleado`); a **provisional password** is one set by
someone other than the user, which the user must replace at their next login.

Rules come from the archived `user-registry`, `access-control` and `desktop-shell` specs, and from
`openspec/domain/respuestas-del-dueno.md` §15 (Empleado is excluded from user management). This
proposal cites them; it does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope, §5 Decisions and §10 Size Forecast. **Skim** §4
Approach. **Skip** §6 Affected Areas, §7 Risks and §8 Rollback unless you want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Decisions (with how strongly each is held)
6. Affected Areas
7. Risks
8. Rollback Plan
9. Dependencies and Preconditions
10. Size Forecast
11. Success Criteria

---

## 1. Intent

The spec already requires that an Admin create, reset and deactivate Empleado users from inside the
application (`openspec/specs/user-registry/spec.md:78`). The logic exists and is tested
(`IUserProvisioning`, `PostgresUserProvisioning`), but no screen uses it and it is not even wired
after login. Today, adding an employee means running SQL by hand.

After this change, the Admin opens a **Usuarios** screen, sees every user, and can create an
Empleado, reset an Empleado's password, deactivate one who left, and reactivate one who came back —
each Empleado action handing the Admin a provisional password to pass on, when there is one.

**Why now:** it is the last thing required before delivery (`archive-report.md:62` of
`ui-de-cobranza`). Without it, every staff change at the agency needs the development team.

**Success, in one line:** Nicolás can give a new employee access, and take it away, without calling us.

---

## 2. Scope

### In Scope

- **Users screen, Admin only**: lists every user with display name, username, role, active or not,
  and whether a password change is pending. Hidden from an Empleado; the database GRANTs still
  enforce (`access-control/spec.md:53-68`).
- **Create Empleado**: username and display name typed; the system generates the provisional
  password and shows it once.
- **Reset password** of an active Empleado: new generated provisional password, shown once.
- **Deactivate** an Empleado, after a confirmation. Logical, as today: `NOLOGIN` + `IsActive = false`,
  nothing deleted.
- **Reactivate** a deactivated Empleado: login restored together with a new provisional password, in
  one transaction (Decision 3).
- **Admin rows are read-only** on the screen (Decision 2).
- **A minimum password length** on the change-password screen (Decision 6): today it only refuses an
  empty field (`src/Inmobiliaria.Desktop/ChangePasswordViewModel.cs:67-71`), so an Empleado can replace
  the provisional password with `1`.
- **A Domain port** for the screen (Decision 1), implemented in Infrastructure by extending
  `PostgresUserProvisioning`, registered in `SessionServices`, Testcontainers-tested.
- Clear operator messages for every refusal, including PostgreSQL's `42501` when a role was not
  created by the logged-in Admin.
- Correcting the stale spec references in the comments of the files this change touches.

### Out of Scope

| Not here | Why |
|---|---|
| Creating an Admin from the application | Forbidden by the spec: PostgreSQL would create an Admin that cannot provision anyone (`user-registry/spec.md:84-85`). Stays a runbook step |
| Deleting users | The spec requires deactivation, never deletion; history keeps naming the person |
| Reusing a username for a different person | Forbidden by the spec (:80) |
| Editing display names or usernames | Not requested; additive later if needed |
| `R1-clear-must-change-callable-standalone` | A hardening of the password function, not this screen (`ui-de-cobranza/archive-report.md:59`) |

---

## 3. Capabilities

The spec is **one file**, `openspec/changes/gestion-de-usuarios/spec.md`, with a Decisions table, a
table of contents and numbered Tests.

### New Capabilities

None. The screen is the missing half of an existing capability.

### Modified Capabilities

- **`user-registry`** — the provisioning requirement gains: listing users with their role; reactivation
  with a new provisional password; Admin rows not actionable from the application; typed refusals.
  The password-change requirement gains the minimum length of Decision 6.
- **`desktop-shell`** — the Admin-only requirement (:32-56) names a second screen, and its Test 2
  (:149) covers it.

`access-control` is **not** modified: who may do what is already defined and enforced by GRANT.

---

## 4. Approach

### 4.1 The port lives in Domain

View-models live in `Inmobiliaria.Presentation`, which may reference only Domain
(`PresentationArchitectureGuardTests.cs:16`). So the screen needs a Domain port, like go-live's
`IGoLiveSetup` (`src/Inmobiliaria.Domain/GoLive/IGoLiveSetup.cs:9`). A new `IUserAdministration`
offers List, Create, ResetPassword, Deactivate and Reactivate, and returns results with typed
refusals instead of throwing. `PostgresUserProvisioning` implements it; the Infrastructure-only
`IUserProvisioning` is removed, since its only caller is its own test class.

### 4.2 Listing users

`app_users` has no role column, by spec: the role is derived from group membership at login. The list
reads `app_users` and each role's membership (`pg_auth_members`) to label Admin or Empleado. No
migration and no model change.

### 4.3 Provisional passwords

Generated by the system only; the Admin never types one (Decision 4). Easy to dictate over the phone
or WhatsApp: lowercase words and digits separated by hyphens, in the shape of `lago-4821-sol`, without
look-alike characters, with enough length to resist guessing. The exact word list and length are
settled in the design. Shown once on the screen, with a copy button, and never written to the error
log. Every existing rule applies unchanged: `MustChangePassword` is set, the user cannot keep it, and
password SQL is escaped server-side (`user-registry/spec.md:127-185`).

### 4.5 Password rule on change

A new password must be **at least 8 characters** and must not equal the username. No composition
rules (uppercase, digits, symbols): NIST SP 800-63B says verifiers shall not impose them, because they
push people to `Password1!` without adding strength. The rule lives in Domain so it is tested in
`Inmobiliaria.Domain.Tests` (the Desktop project is outside the CI solution filter) and the
change-password view-model calls it. It is enforced in the application, not by PostgreSQL: Supabase
offers no server-side password-strength hook for login roles.

### 4.4 The screen

A list plus actions, following the go-live precedent: `UsersViewModel` in Presentation with an
`OperationRunner` and async commands, `UsersView` in Desktop, and the five shell touch points
(`NavigationTarget`, `ShellViewModel.BuildNavigation`, `App.xaml.cs`, `MainWindow`). Deactivation asks
for confirmation first (convention `confirm-with-a-summary`).

---

## 5. Decisions

| # | Decision | Held | Why |
|---|---|---|---|
| 1 | New Domain port `IUserAdministration` with typed refusals | **Moderate** | Required by the layering guard; typed refusals give Spanish messages instead of raw exceptions. Moving the old interface as-is would work but would grow into this anyway |
| 2 | List every user, act only on Empleados | **Firm** | PostgreSQL lets an Admin change only roles they created, and Admins are made by runbook. It also rules out an Admin deactivating herself |
| 3 | Reactivation = login restored + new provisional password, one transaction | **Firm** | Decided with Tomás 2026-10-25. Otherwise the person returns with the password they had when they left |
| 4 | The system generates provisional passwords, easy to dictate; the Admin cannot type one | **Firm** | Decided with Tomás 2026-11-01. A hand-typed one invites `1234`, and it adds nothing: the password is used once and must be changed at the next login |
| 5 | No migration | **Firm** | `app_set_role_login(name, true)` already reactivates; listing reads catalogs already readable |
| 6 | New passwords: minimum 8 characters, not equal to the username, no composition rules | **Firm** on no composition rules; **Moderate** on 8 | Decided with Tomás 2026-11-01. NIST SP 800-63B forbids composition rules. It asks for 15 when the password is the only factor; 8 is a deliberate concession to a small office's daily use |

---

## 6. Affected Areas

| Area | Files |
|---|---|
| Domain | `src/Inmobiliaria.Domain/Access/` — new port, records, refusals; `AppUser.Activate()`; the password rule |
| Infrastructure | `src/Inmobiliaria.Infrastructure/Access/PostgresUserProvisioning.cs`, `IUserProvisioning.cs` (removed), `SessionServices.cs` |
| Presentation | `src/Inmobiliaria.Presentation/Users/` (new), `Shell/NavigationEntry.cs`, `Shell/ShellViewModel.cs` |
| Desktop | `src/Inmobiliaria.Desktop/Views/UsersView.xaml(.cs)` (new), `App.xaml.cs`, `MainWindow.xaml(.cs)`, `ChangePasswordViewModel.cs` |
| Tests | `tests/Inmobiliaria.Infrastructure.Tests/UserProvisioningTests.cs`, `tests/Inmobiliaria.Presentation.Tests/` (new tests and a fake), `tests/Inmobiliaria.Domain.Tests/` (password rule) |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| **Security surface**: the change exercises role creation and password changes (high-risk item 2) | Database GRANTs already enforce. Tests prove an Empleado session is refused by the database, not just hidden. Independent verification and the gentle-ai review on the S1 slice |
| The provisional password leaks into a log or exception text | A test asserts the error-log path never receives it |
| An Admin made by runbook cannot reset an Empleado another Admin created | Explicit refusal message; documented in the spec. Invisible with one Admin, which is the agency today |
| The live smoke uses the wrong Admin | On the test database, `tomas` creates the Empleado it then resets |

---

## 8. Rollback Plan

No migration and no data format change, so rollback is reverting the PRs. Users created through the
screen are ordinary users, the same as ones created by SQL, and stay valid after a revert.

---

## 9. Dependencies and Preconditions

- `user-registry`, `access-control` and `desktop-shell` exist and are archived.
- Open decisions in `openspec/config.yaml`: **none touched** (`scheduler-mechanism`, `pdf-library`).
- Domain observations: none apply beyond `respuestas-del-dueno.md` §15.

| Phase / slice | Docker | Supabase |
|---|---|---|
| spec, design, tasks | No | No |
| S1 port + Infrastructure | **Yes** | No |
| S2 screen | **Yes** for the full suite | No |
| verify | **Yes** | Optional live smoke, Tomás only, test database restored |

The assistant never handles the Supabase password.

---

## 10. Size Forecast

Advisory, about 400 authored lines per slice (config `review_budget_lines` 800).

| Slice | Content | Estimate |
|---|---|---|
| S1 | Domain port, records, refusals, `AppUser.Activate()`; password rule; list, reactivate, `42501` mapping; registration; Testcontainers tests | ~450 |
| S2 | `UsersViewModel` + tests; view and shell wiring; change-password rule wired; spec deltas | ~520 |

**Total about 970 authored lines**, two chained PRs.

Decision needed before apply: No
Chained PRs recommended: Yes
400-line budget risk: Medium (S2)

---

## 11. Success Criteria

- An Admin logged in to the application can create an Empleado, and that Empleado can log in with the
  provisional password and is forced to change it.
- An Admin can reset, deactivate and reactivate that Empleado; a deactivated one cannot log in, and a
  reactivated one can only log in with the new provisional password.
- An Empleado sees no Usuarios screen, and calling the same operations with an Empleado session is
  refused by the database.
- A new password shorter than 8 characters, or equal to the username, is refused with a clear message.
- Full suite green with Docker running, no skipped tests; `has-pending-model-changes` reports no
  changes.
