# Design: gestion-de-usuarios (the Admin's user screen)

Technical design for the `gestion-de-usuarios` change, from `spec.md` (merged in PR #80, Requirements 1 to
11, Decisions P1 to P6 and 7 to 19, Tests 1 to 25) and `proposal.md` in this folder. Written 2026-11-01.
Nothing here is code; nothing here edits the spec. Every type, member and file named as existing was read
in the code and is cited `path:line`; anything new is marked **(new)**.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** rows are where you can change my mind |
| **§9 Findings** | Two things found while designing. **F5** is a real gap in today's error log, and this change closes it |
| **§10 Slices** | Three PRs instead of two |
| Decision D6 | The generated password is a little longer than the spec's example: `lago-sol-pan-48017` |

**Skip** §2 to §8 unless you want the detail: they are written for whoever implements and for the second
inspection at the start of each session.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | One Domain port **`IUserAdministration`** (new) with List, Create, ResetPassword, Deactivate, Reactivate; results carry a refusal or a value, never an exception for anything the Admin can cause | Strong |
| D2 | `PostgresUserProvisioning` becomes **`PostgresUserAdministration`** and implements the port. `IUserProvisioning` is deleted. It already opens one context per call, so it needs no per-call wrapper | Strong |
| D3 | The adapter **generates** the provisional password and returns it in the result. The view-model never makes one | Strong |
| D4 | Username, password rule, generator and secret masking are **pure Domain code**, so `Inmobiliaria.Domain.Tests` gates them on the Linux CI job | Strong |
| D5 | The rule on a chosen password is checked in **`PostgresPasswordService`**, before any database call, and reported as a typed refusal; the desktop dialog only shows its text | Strong |
| D6 | Generated password: **three words from a fixed list of 256 + five digits**, hyphen-separated, e.g. `lago-sol-pan-48017`. 24 + 16.6 = **40.6 bits** (spec Decision 13) | **Medium** on the shape |
| D7 | The login normalizes the username **inside `NpgsqlAuthenticator`**, so the session, the Supavisor username and every later connection use the normalized name | Strong |
| D8 | **42501** maps to "created by another Admin" for an Admin session and to "Admin only" for any other session | Strong |
| D9 | The list reads **direct** membership through `pg_auth_members`, not `pg_has_role`, because `pg_has_role` throws for a name with no role | Medium |
| D10 | The error log masks `PASSWORD '...'`, not only `password=...` (Finding F5) | Strong |
| D11 | Confirmation is a **pending action** on the view-model (shown, then Confirm or Cancel), like the voiding preview, not a modal dialog | Strong |
| D12 | Copy goes through a Presentation interface **`IClipboard`** (new); Desktop implements it with WPF's clipboard | Strong |
| D13 | **Three slices**: S1 rules and login, S2 port and adapter, S3 screen | Medium |

## Table of Contents

1. Technical approach
2. Domain
3. Infrastructure
4. The login and the password dialog
5. Presentation
6. Desktop and the shell
7. Testing strategy
8. Spec claims checked against the code
9. Findings
10. Slices, sizes and test mapping
11. Threats and rollback
12. Questions for Tomás

---

## 1. Technical approach

```
UsersView (Desktop) ──> UsersViewModel (Presentation) ──> IUserAdministration (Domain)
                                     │                                  ▲
                                     └─> IClipboard (Presentation)      │ implemented by
                                                                        │
                              PostgresUserAdministration (Infrastructure, one context per call)
                                     ├─ Username, ProvisionalPassword (Domain)
                                     └─ app_create_login_role / app_set_role_password /
                                        app_set_role_login + app_users, one transaction per action

LoginWindow ──> NpgsqlAuthenticator ──> Username.Normalize (Domain)
ChangePasswordViewModel (Desktop) ──> PostgresPasswordService ──> PasswordRule (Domain)
```

No migration and no model change (spec Decision P5). Every SQL function used already exists
(`20260924202549_AddUsersAndRoles.cs:176,183,191`).

## 2. Domain

All in `src/Inmobiliaria.Domain/Access/`.

### 2.1 `IUserAdministration` (new) — `IUserAdministration.cs`

```
Task<IReadOnlyList<UserListing>> ListAsync(ct)
Task<UserAdminResult> CreateEmpleadoAsync(string typedUsername, string displayName, ct)
Task<UserAdminResult> ResetPasswordAsync(Guid userId, ct)
Task<UserAdminResult> DeactivateAsync(Guid userId, ct)
Task<UserAdminResult> ReactivateAsync(Guid userId, ct)
```

Actions take the row's `Guid`, not the username, so a stale list can never act on a different person.

- `UserListing(Guid Id, string Username, string DisplayName, UserRole? Role, bool IsActive, bool MustChangePassword)`.
  `Role` is null for a user in no group (spec R1). Uses the existing `UserRole`
  (`src/Inmobiliaria.Domain/Access/UserRole.cs`).
- `UserAdminResult(UserAdminRefusal? Refusal, ProvisionalCredential? Credential)`, same shape as
  `SetupResult` (`IGoLiveSetup.cs:71-78`).
- `ProvisionalCredential(string Username, string Password)`, with **`ToString()` overridden to omit the
  password**, so a record printed into any message or log carries no secret.
- `UserAdminRefusal` (enum): `UsernameInvalid`, `UsernameTaken`, `DisplayNameMissing`, `UserNotFound`,
  `NotAnEmpleado` (an Admin, or a user in no group), `AlreadyDeactivated` (for reset), `AlreadyActive` (for
  reactivate), `CreatedByAnotherAdmin`, `AdminOnly`.
- `UserAdminRefusalText.OperatorText()` (new), the same pattern as `SetupRefusalText`
  (`IGoLiveSetup.cs:81-103`). Exact Spanish wording is set when writing it; each names what to do next
  (for example `AlreadyDeactivated`: "El usuario está desactivado. Para que vuelva a entrar, reactívelo.").

Deactivating an already deactivated user is **not** a refusal (spec R4): it returns success and changes
nothing, as `DeactivateAsync` already does (`PostgresUserProvisioning.cs:166`).

### 2.2 `Username` (new) — `Username.cs`

- `Normalize(string typed)`: `Trim()`, then `Normalize(NormalizationForm.FormC)`, then
  `ToLowerInvariant()`. `Ñ` and a decomposed `n` + U+0303 both become `ñ`.
- `IsValid(string normalized)`: `^[a-zñ][a-z0-9ñ._]{2,29}$` (spec Decision 8).

`AppUser`'s constructor keeps its lowercase check (`AppUser.cs:54`) unchanged.

### 2.3 `PasswordRule` (new) — `PasswordRule.cs`

`Check(string username, string currentPassword, string newPassword) → PasswordRefusal?`, in this order:

1. `SameAsCurrent`: ordinal equality, the existing rule (`PostgresPasswordService.cs`, today an
   `InvalidOperationException`).
2. `TooShort`: fewer than 8 **text elements** (`StringInfo.LengthInTextElements`), so an accented letter
   or an emoji counts as one character as typed (spec Decision 14).
3. `EqualsUsername`: `newPassword.ToLowerInvariant() == username` (the username is already normalized).

No upper limit, no trimming, no composition check. `PasswordRefusalText.OperatorText()` (new) keeps the
existing Spanish for `SameAsCurrent` ("La nueva contraseña debe ser distinta de la actual.",
`ChangePasswordViewModel.cs:96`) and names the rule for the other two.

### 2.4 `ProvisionalPassword` (new) — `ProvisionalPassword.cs`

- `Generate()` takes three words with `RandomNumberGenerator.GetInt32(256)` and five digits with
  `RandomNumberGenerator.GetInt32(10)`, joined `word-word-word-ddddd`.
- `Words` (internal, 256 entries): short everyday Spanish words, 3 to 6 letters, `a`-`z` only, no `ñ`, no
  accents, all distinct, nothing offensive. Written 8 per line.
- Look-alikes: a word block never holds a digit and the digit block never holds a letter, so `l`/`1` and
  `o`/`0` cannot be confused by position (spec R6).
- An overload `Generate(Func<int, int> next)` lets a test drive the choices; production uses the
  cryptographic one only.

### 2.5 `SecretMask` (new) — `SecretMask.cs`

`Mask(string text)` replaces both `password=<value>` (today's rule, `FileErrorLog.cs:47-50`) and
`PASSWORD E'...'` / `PASSWORD '...'` (with doubled quotes inside) by `***`. `FileErrorLog.Mask` calls it.
In Domain so both Infrastructure.Tests and Presentation.Tests can prove it (Finding F5).

## 3. Infrastructure

### 3.1 `PostgresUserAdministration` — rename of `src/Inmobiliaria.Infrastructure/Access/PostgresUserProvisioning.cs`

Constructor: `(ISessionDbContextFactory factory, IUserSession session, Func<string>? generate = null)`;
`generate` defaults to `ProvisionalPassword.Generate`.

| Method | Steps |
|---|---|
| `ListAsync` | One query: `app_users` left-joined to two `EXISTS` over `pg_auth_members` ⨝ `pg_roles` for `inmobiliaria_admin` and `inmobiliaria_empleado` (D9). Order is the view-model's (§5) |
| `CreateEmpleadoAsync` | Normalize; invalid → `UsernameInvalid`; blank display name → `DisplayNameMissing`; taken → `UsernameTaken` (the existing pre-check, `:63-69`, **before** generating or sending a password); then the existing transaction (`:75-87`) with the generated password |
| `ResetPasswordAsync` | Load by id; missing → `UserNotFound`; not an Empleado → `NotAnEmpleado` (checked before any password is sent); inactive → `AlreadyDeactivated`; then the existing transaction (`:126-140`) |
| `DeactivateAsync` | Load; missing; not an Empleado; already inactive → success, nothing done; then the existing transaction (`:161-171`) |
| `ReactivateAsync` | Load; missing; not an Empleado; active → `AlreadyActive`; then **one transaction**: `app_set_role_login(name, true)`, `app_set_role_password(name, pw)`, `user.Activate()`, `user.RequirePasswordChange()`, save, commit (spec R5) |

Every action catches `PostgresException` with `SqlState == 42501`, directly or as the inner exception of a
`DbUpdateException`, and returns `CreatedByAnotherAdmin` when `session.Role == UserRole.Admin`, else
`AdminOnly` (D8). Anything else propagates to the shell's generic error.

`AppUser.Activate()` (new, `src/Inmobiliaria.Domain/Access/AppUser.cs`, beside `Deactivate()` at `:81`):
sets `IsActive = true`; idempotent.

The stale comments (`IUserProvisioning.cs:56-58`, `PostgresUserProvisioning.cs:58,68,163`) are rewritten
to cite `user-registry` "Admin Provisions and Deactivates Users From Inside the Application" (spec F3).

### 3.2 Registration — `SessionServices.cs`

`services.AddSingleton<IUserAdministration>(_ => new PostgresUserAdministration(factory, session));` beside
`IGoLiveSetup` (`SessionServices.cs:40`). It builds for an Empleado too; nothing connects at build time
(spec R11).

## 4. The login and the password dialog

### 4.1 Login — `src/Inmobiliaria.Infrastructure/Access/NpgsqlAuthenticator.cs`

First line after the null check (`:25`): `username = Username.Normalize(username);`. Everything below
already uses that variable: the Supavisor name (`:34`) and the `app_users` lookup (`:121-123`). The password
is not touched. The generic failure path (`:52-58`) is unchanged. `LoginViewModel` is unchanged.

### 4.2 Password change — `PostgresPasswordService.cs` and `ChangePasswordViewModel.cs`

- `PasswordChangeRefusedException(PasswordRefusal Refusal)` (new,
  `src/Inmobiliaria.Infrastructure/Access/PasswordChangeRefusedException.cs`). Its message is the operator
  text; it never contains a password.
- `ChangeOwnPasswordAsync` calls `PasswordRule.Check(_session.Username, current, new)` first and throws that
  exception on a refusal, **before** creating a context. This replaces the `InvalidOperationException` for
  `SameAsCurrent`.
- `ChangePasswordViewModel` (`:85-101`) catches `PasswordChangeRefusedException` and shows
  `ex.Refusal.OperatorText()`, in place of the `InvalidOperationException` branch. The generic branch is
  unchanged. A forced change stays pending because nothing reached the database.

## 5. Presentation — `src/Inmobiliaria.Presentation/Users/` (new)

### 5.1 `UsersViewModel`

Same shape as `GoLiveViewModel` (`GoLiveViewModel.cs:17-40`): `ObservableObject`, `OperationRunner Runner`,
async commands disabled while busy.

| Member | Behaviour |
|---|---|
| `Users` (`ObservableCollection<UserRow>`) | Active first, then by display name with an `es-AR` comparer (spec Decision 7) |
| `LoadCommand` | Lists; also run after **every** action, refused or not (spec Decision 18) |
| `NewUsername`, `NewDisplayName`, `CreateCommand` | Create; on success clears the form; on refusal keeps it (spec R8) |
| `Pending` (`PendingUserAction?`) + `ConfirmCommand`, `CancelCommand` | Reset, deactivate and reactivate set `Pending` (kind, user, text); nothing runs until Confirm (D11). The deactivate text says an open session keeps working until it is closed (spec F1) |
| `Revealed` (`ProvisionalCredential?`) + `CopyCommand`, `DismissCommand` | Set from a successful result; Dismiss sets it to null and nothing keeps a copy (spec R6) |
| `RefusalMessage` | The refusal's operator text; cleared when a new action starts |

`UserRow`: display name, username, role text ("Admin", "Empleado", or empty), state text, pending text,
and `CanReset`, `CanDeactivate`, `CanReactivate`, all false unless the role is Empleado (spec R7).

Every action runs through `Runner.RunAsync` with `OperationKind.Read`'s generic message. The error log
receives the exception, masked (§2.5).

### 5.2 `IClipboard` (new) — `src/Inmobiliaria.Presentation/Common/IClipboard.cs`

`void SetText(string text)`. Called only by `CopyCommand` (spec Decision 12). Tests use a fake that records
calls.

## 6. Desktop and the shell

The five touch points of the go-live precedent:

| Where | Change |
|---|---|
| `Presentation/Shell/NavigationEntry.cs:4-19` | `NavigationTarget.Users`, title "Usuarios" |
| `Presentation/Shell/ShellViewModel.cs:48-51` | Added only for `UserRole.Admin`, after go-live |
| `Desktop/App.xaml.cs:144-146,179` | Builds `UsersViewModel` only for an Admin, with `IUserAdministration` and `WpfClipboard`; passes it nullable to `MainWindow` |
| `Desktop/MainWindow.xaml(.cs)` | Constructor parameter, DataContext, one more DataTrigger |
| `Desktop/Views/UsersView.xaml(.cs)` (new) | Form on top, list below, a confirmation band and a password band that appear when `Pending` or `Revealed` is set. The password shows in a large monospace font |

`Desktop/WpfClipboard.cs` (new): `Clipboard.SetText(text)`. Checked by hand (M1).

## 7. Testing strategy

| Project | What it proves | Docker |
|---|---|---|
| `Inmobiliaria.Domain.Tests` | `Username`, `PasswordRule`, `ProvisionalPassword` (with the word list), `SecretMask`, `AppUser.Activate()`, refusal texts | No |
| `Inmobiliaria.Infrastructure.Tests` | The adapter against `postgres:17.6` as an Admin and as an Empleado; login normalization; the password service's rule; wiring; the masked log with a real `PostgresException` | **Yes** |
| `Inmobiliaria.Presentation.Tests` | `UsersViewModel` with a fake port and a fake clipboard; shell navigation | No |
| By hand (M1 to M3) | M1 Copy pastes into WhatsApp Web; M2 the screen at 1366×768; M3 live smoke on the test Supabase: `tomas` creates, resets, deactivates and reactivates an Empleado, and one Empleado with `ñ` logs in (spec F4) | Supabase, Tomás only |

**Forcing the reactivation's password step to fail (spec Test 9):** the fixture, connected as the
container's superuser, revokes `EXECUTE` on `app_set_role_password` from `inmobiliaria_admin`, runs the
reactivation, and grants it back in a `finally`. The same revoke produces the real `PostgresException` for
Test 13.

**Proving the guards fail:** each new guard (the rule, the mask, the 42501 mapping) is seen failing once,
by breaking it locally, before it is trusted. Recorded in `tasks.md` beside its task.

## 8. Spec claims checked against the code

| # | Claim | Checked |
|---|---|---|
| C1 | Taken usernames are refused before a password is sent | True: the pre-check at `PostgresUserProvisioning.cs:63-69` runs before `app_create_login_role` (`:78`) |
| C2 | Resetting a deactivated user is refused | True, as an exception today (`:118-124`); becomes `AlreadyDeactivated` |
| C3 | Reactivation needs no migration | True: `app_set_role_login(name, boolean)` takes `true` (`AddUsersAndRoles.cs:191`) |
| C4 | The login username goes to Supavisor as `role.projectref` | True: `SupavisorUsername.For` (`SupavisorUsername.cs`), used at `NpgsqlAuthenticator.cs:34` |
| C5 | An Empleado is refused by the database | Create and set-login: `EXECUTE` is Admin-only (`AddUsersAndRoles.cs:213-216`). Reset: `app_set_role_password` is granted to both roles (`:209`), but `ALTER ROLE` on a role the Empleado did not create fails with 42501, and `UPDATE app_users` is Admin-only (`:163`). Proven by Test 22 |

## 9. Findings

| # | Finding | What this design does |
|---|---|---|
| F5 | **Today's error log can record a password.** When the dynamic `ALTER ROLE %I PASSWORD %L` fails inside a SQL function, PostgreSQL reports the statement in the error's context (`Where`), and Npgsql's `PostgresException.ToString()` prints it. `FileErrorLog` masks only `password=...` (`FileErrorLog.cs:47-50`), not `PASSWORD '...'`. It affects the existing self-service change too. **Expected from how PostgreSQL reports errors in functions; not yet seen in this project's log** | D10: `SecretMask` masks both forms, and Test 13 proves it with a real exception from the container. If the exception turns out not to carry the statement, the test still passes and the mask is harmless |
| F6 | `pg_has_role(name, ...)` raises an error for a name with no PostgreSQL role, so it cannot list a row whose role was dropped by hand | D9: the list uses `EXISTS` over `pg_auth_members`, which returns false instead |

## 10. Slices, sizes and test mapping

Advisory, about 400 authored lines per slice. All three are security-touching (high-risk item 2), so each
is reviewed on its own.

| Slice | Content | Spec tests | Estimate | Docker |
|---|---|---|---|---|
| **S1 rules and login** | `Username`, `PasswordRule`, `ProvisionalPassword` + words, `SecretMask` + `FileErrorLog`, `PasswordChangeRefusedException`, the password service and dialog, the login normalization | 4, 11, 13 (mask half), 18, 19, 24, 25 | ~380 | **Yes** |
| **S2 port and adapter** | `IUserAdministration` and records, `AppUser.Activate()`, `PostgresUserAdministration`, deletion of `IUserProvisioning`, registration, test-class rename | 1-3, 5-10, 13 (adapter half), 14 (port half), 15, 21, 22, 23 | ~420 | **Yes** |
| **S3 screen** | `UsersViewModel`, `UserRow`, `IClipboard`, shell and desktop wiring, spec deltas merged into `openspec/specs/` | 12, 14 (view-model half), 16, 17, 20 | ~450 | Yes for the full suite |

**Total about 1,250 authored lines**, more than the proposal's 970: the word list, the mask and the login
normalization were not in the proposal's count. Three chained PRs, each merged before the next starts.

## 11. Threats and rollback

| Threat | Mitigation |
|---|---|
| An Empleado provisions or resets | GRANT (C5); Test 22 as an Empleado session |
| A password reaches the log | `SecretMask` (F5); `ProvisionalCredential.ToString()` omits it; refusals carry no password; Test 13 |
| A stale list acts on the wrong person | Actions by `Guid` (§2.1); the list reloads after every action |
| A weak chosen password | `PasswordRule` (S1) |
| Guessing a provisional password before it is changed | 40.6 bits, online only, through Supavisor |

Rollback: no migration and no data change. Reverting the PRs leaves users made through the screen as
ordinary users. Reverting S1 alone removes the minimum length; passwords already set stay valid.

## 12. Questions for Tomás

None open. D6 is the only one that changes what Nicolás sees: the password is three words and five digits,
not two words and four. That is what keeps it at the strength the spec asked for.
