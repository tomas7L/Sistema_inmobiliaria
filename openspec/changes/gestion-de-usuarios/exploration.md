# Exploration: gestion-de-usuarios (2026-10-25)

> Source of truth mirrored in Engram `sdd/gestion-de-usuarios/explore`.

## Current state (evidence)

- The requirement already exists and is unmet: "Admin Provisions and Deactivates Users From Inside the Application" (`openspec/specs/user-registry/spec.md:78-107`). An Admin creates **Empleado** users only (login role + `AppUser` row as one unit), resets the password of users they created, and deactivates with `NOLOGIN` + `IsActive = false`, never deleting. A username is never reused (:80). Two PostgreSQL limits are recorded there (:84-86): creating an Admin is a runbook step (`docs/runbooks/bootstrap-first-admin.md`), and an Admin can only `ALTER ROLE` roles they created.
- Related rules: an Admin reset takes effect on the next login and leaves an open session alone (:109-125); create and reset both set `MustChangePassword`, and the provisional password cannot be kept (:127-167); password SQL is escaped server-side (:169-185). Empleado is excluded from these actions (`access-control/spec.md:12,32-36`), and the database GRANT is the enforcement (:53-68). Hiding an Admin screen is not enforcement (`desktop-shell/spec.md:32-56`); that requirement names only the go-live screen.
- The logic exists but nothing calls it: `IUserProvisioning` (`src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs:38,52,60`) offers `CreateUserAsync(username, password, displayName)`, `ResetPasswordAsync(username, newPassword)` and `DeactivateAsync(username)`. `PostgresUserProvisioning` validates input before sending a password (:39-51), refuses an existing username active or not (:63-69), creates role + row in one transaction (:78,83), refuses reset of a deactivated user (:122), and deactivates idempotently (:166). It is used only by `tests/Inmobiliaria.Infrastructure.Tests/UserProvisioningTests.cs` and is **not registered** in `SessionServices.cs:26-53`.
- Missing from the port: **listing users** (the screen cannot exist without it) and **reactivation**. `AppUser` (`src/Inmobiliaria.Domain/Access/AppUser.cs`) has `Id`, `Username`, `DisplayName`, `IsActive`, `MustChangePassword`, `Deactivate()` (:81), `RequirePasswordChange()` (:95); no `Activate()` and no role column (role comes from `pg_has_role` at login, by spec). SQL `app_set_role_login(name, boolean)` (`Migrations/20260924202549_AddUsersAndRoles.cs:188-193`) already accepts `true`, so reactivation needs no migration.
- Grants (`AddUsersAndRoles.cs`): `INSERT, UPDATE ON app_users` to Admin only (:163); `EXECUTE` on create and set-login to Admin only (:213-216); `app_set_role_password` to both roles (:209). None of the three are SECURITY DEFINER (:167-169).
- Layering: ViewModels live in `src/Inmobiliaria.Presentation`, which may reference only Domain — `PresentationArchitectureGuardTests.cs:16` forbids `Inmobiliaria.Infrastructure`. So a ViewModel **cannot** use `IUserProvisioning` where it is today. Precedent: the go-live port is declared in Domain (`src/Inmobiliaria.Domain/GoLive/IGoLiveSetup.cs:9`) and implemented in Infrastructure (`SessionServices.cs:40`, `PerCallGoLiveSetup`).
- Admin-only screen precedent (go-live), five touch points: `NavigationTarget` + title (`Presentation/Shell/NavigationEntry.cs:4-19`); `ShellViewModel.BuildNavigation` adds it only for `UserRole.Admin` (`ShellViewModel.cs:48-51`); `App.xaml.cs:144-146` builds the ViewModel only for Admin and passes it nullable to `MainWindow` (:179); `MainWindow.xaml.cs:17-32` sets the DataContext; `MainWindow.xaml:52-98` switches screens with DataTriggers. ViewModel shape: `ObservableObject` + `OperationRunner Runner` + `IAsyncRelayCommand`s (`GoLiveViewModel.cs:17-40`); tests use fakes in `tests/Inmobiliaria.Presentation.Tests/Fakes/` and gating is pinned in `ShellViewModelTests.cs`.
- Unmapped error: resetting or deactivating a role the caller did not create (any runbook Admin) fails with a raw `PostgresException 42501`, which would surface as the generic error.
- Stale references: comments cite a requirement "Deactivate, Never Delete" and spec tests 11/31 (`IUserProvisioning.cs:56-58`, `PostgresUserProvisioning.cs:58,68,163`); no such requirement exists and user-registry has 21 tests.

## Approaches (structure)

| | Approach | Pros | Cons |
|---|---|---|---|
| A | **Move** `IUserProvisioning` to Domain as-is, add `ListAsync`, register it | Smallest diff; no consumer besides one test class | The port's name and shape stay provisioning-oriented; list results need a Domain record anyway |
| B | **New Domain port** `IUserAdministration` (List, Create, Reset, Deactivate, Reactivate) implemented by the existing `PostgresUserProvisioning`, old interface deleted | Mirrors `IGoLiveSetup`; one port the screen owns; returns refusals the ViewModel maps to Spanish text instead of raw exceptions | Slightly more code: a result/refusal type and its mapping |

**Recommendation: B, held moderately.** Both are correct; B wins because the screen needs typed refusals (username taken, user deactivated, "not created by you" for `42501`) and a list record, and putting those in Domain is the existing go-live pattern. A would end up growing into B.

## Other structural recommendations

- **List shows every user, actions only on Empleados.** The list reads `app_users` plus each role's group membership (`pg_auth_members`) to label Admin/Empleado. Admin rows are read-only in the screen, which also removes the self-deactivation case and the `42501` path in normal use. Held firmly: it follows from the two PostgreSQL limits in the spec.
- **The system generates the provisional password** (readable, e.g. 10 chars without ambiguous letters), shown once for the Admin to pass on. Typing one by hand invites `1234`. Held moderately; standard practice, and `MustChangePassword` forces replacing it either way.
- **Reactivation always issues a new provisional password, in one step.** Reactivating restores `LOGIN` (`app_set_role_login(name, true)`), sets `IsActive = true` (new `AppUser.Activate()`), sets a generated password and `MustChangePassword`, all in one transaction. Without the reset, the person would come back with the password they had when they left. Today `ResetPasswordAsync` refuses a deactivated user (`PostgresUserProvisioning.cs:120-124`), so reactivation is its own operation, not "activate, then reset". The same "only roles you created" limit applies. Held firmly; decided with Tomás 2026-10-25 (the agency may rehire someone who left).
- **Fix the stale spec references** in the comments while touching those files.
- Keep out: `R1-clear-must-change-callable-standalone` (`archive-report.md:59`) is a hardening item on the password function, not this screen.

## Scope

In: Domain port + list record + refusals; `AppUser.Activate()`; Infrastructure implementation (list, reactivate with new provisional password, `42501` mapping) and registration; `UsersViewModel` + tests; `UsersView` + the five shell touch points; spec deltas to `user-registry` (list, reactivation with new provisional password, Admin rows read-only) and `desktop-shell` (second Admin-only screen, its test 2 at :149).
Out: creating Admins in the app (forbidden by the spec), editing display names, deleting users, R1 hardening.

## Questions that need an answer

1. ~~Reactivation~~ — **answered 2026-10-25 (Tomás): in scope.** Someone who left can come back; deactivation stays logical, and reactivation issues a new provisional password (see Other structural recommendations).
2. Generated vs typed provisional password — decided here (generated) unless Tomás objects.

## Risks

- **Security surface (high-risk item 2):** this screen exercises role creation and password changes. The database GRANTs already enforce Admin-only; tests must prove an Empleado session is refused by the database, not just hidden.
- Live smoke needs the test Supabase restored, and a reset only works on roles the logged-in Admin created — the test Admin `tomas` must create the Empleado it then resets.
- The provisional password appears on screen once; it must not reach the error log (`%LOCALAPPDATA%\SistemaInmobiliaria\logs`).

## Size forecast (authored lines incl. tests) and slicing

| Slice | Content | Lines | Docker |
|---|---|---|---|
| S1 | Domain port, records, refusals, `AppUser.Activate()`; Infrastructure list/reactivate/mapping; registration; Testcontainers tests | ~400 | Yes |
| S2 | `UsersViewModel` + tests; view + shell wiring; spec deltas | ~500 | No (Presentation tests), yes for the full suite |

About 900 total, two PRs.

## Ready for proposal

Yes. No open questions for Nicolás.
