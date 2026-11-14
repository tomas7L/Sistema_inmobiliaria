# Design: limite-de-intentos-de-login (keeping one person's typos from locking the office out)

Technical design for the `limite-de-intentos-de-login` change, from `spec.md` (Requirements 1 to 5, Decisions P1 to P7
and 8 to 15, Tests 1 to 13; merged as PR #127 on 2026-11-13 with Requirements 1 to 4, and corrected the same day) and `proposal.md` in this folder. Written 2026-11-13.
Nothing here is code; nothing here edits the spec. Every existing file named was read or mapped and is cited `path:line`;
anything new is marked **(new)**. Paths use `Infra` = `src/Inmobiliaria.Infrastructure/`, `Pres` =
`src/Inmobiliaria.Presentation/`, `Desk` = `src/Inmobiliaria.Desktop/`, `ITests` = `tests/Inmobiliaria.Infrastructure.Tests/`
and `PTests` = `tests/Inmobiliaria.Presentation.Tests/`.

**How complete the audit was.** Read: `Infra/Access/NpgsqlAuthenticator.cs`, `Infra/Access/AuthenticationResult.cs`,
`Desk/LoginViewModel.cs`, `Desk/LoginWindow.xaml` (bindings), `Desk/App.xaml.cs:96-133` (the login loop), Npgsql 10.0.3's `NpgsqlConnector.OpenCore` (D11),
`Desk/ChangePasswordViewModel.cs:30-55`, `Pres/Common/IErrorLog.cs`, `Pres/Common/OperationRunner.cs:53`,
`ITests/LoginWithoutVerdictTests.cs`, `PTests/PresentationArchitectureGuardTests.cs`, and the projects in
`Inmobiliaria.Core.slnf`. **Not run:** no build and no test.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table right below | Every call in one line, with how hard to lean on it. The **medium** rows are where you can change my mind |
| **§5 Texts** | Mateo's approved wording, exactly as it goes in |

**Skip** §1 to §4 and §6 to §9 unless you want the detail: they are for whoever implements and for the second inspection.

---

## Decisions

| # | Decision | Strength |
|---|---|---|
| D1 | **A new outcome `AuthenticationResult.Blocked(Exception Cause)` (new)**, returned by `NpgsqlAuthenticator` when a `PostgresException` has `SqlState` `XX000` and a `MessageText` starting with `(ECIRCUITBREAKER)` (spec Decision 11). Caught before the general `NpgsqlException` catch, so it is never `Unavailable` | Strong |
| D2 | **The counting and the wait live in `Pres/Login/LoginGate.cs` (new)**, because `LoginViewModel` is in Desktop, which CI never builds (spec Finding F2). The gate decides everything: what counts, when the wait starts and ends, which message shows, what is logged | Strong |
| D3 | **The gate speaks its own outcome, `LoginOutcome` (new): `Accepted`, `NotProvisioned`, `Rejected`, `Blocked(Exception)`, `NoVerdict(Exception)`.** Presentation may not reference Infrastructure (`PTests/PresentationArchitectureGuardTests.cs`), so it cannot see `AuthenticationResult`. Desktop translates one into the other in a five-line switch | Strong |
| D4 | **The gate runs the attempt itself**: `TryLogInAsync(Func<Task<LoginOutcome>> attempt)`. During a wait it returns without calling `attempt`, so "no connection during the wait" (spec Decision 12) is enforced in tested code, not only by a disabled button | Strong |
| D5 | **One gate for the whole run of the application**, created once in `App` and handed to every `LoginViewModel`. Today a new view-model is built on every pass of the login loop (`Desk/App.xaml.cs:98-102`); one gate keeps the count across passes, as spec P3 says ("for as long as it stays open") | Strong |
| D6 | **The gate reads `TimeProvider`**, `TimeProvider.System` in the application, a hand-written fixed one in tests, as `ITests/AgencyClockTests.cs:14` already does. No new package | Strong |
| D7 | **The gate holds no timer.** It answers `TimeLeft` from the clock each time it is asked. The window asks once a second through a `DispatcherTimer` in `LoginViewModel`, only while a wait is on | Medium. A timer inside the gate would be harder to test and is not needed |
| D8 | **The gate also logs `Blocked` and `NoVerdict` causes**, through `IErrorLog`, with the context `"Login"` the view-model uses today (`Desk/LoginViewModel.cs:86`). The application passes `OperationRunner.DefaultErrorLog`. Moving the existing log line into the gate makes spec Decision 13 testable | Medium |
| D9 | **Mateo's texts go in `Pres/Login/LoginTexts.cs` (new)**, with the three messages that exist today moved beside them unchanged, so Test 12 compares them in one place. Spec Decision 14's placeholders are not needed: Mateo's wording arrived and Tomás approved it on 2026-11-13 | Strong |
| D10 | **Both waits last `LoginGate.Wait` = 150 seconds**, one constant (spec P2, Decision 10). If Nicolás's answer changes it, it changes there and in the two texts that name it | Strong |
| D11 | **`GssEncryptionMode = Disable` in the login's connection string** (`Infra/Access/NpgsqlAuthenticator.cs:45-52`), so a refused login is one connection, not two (spec Decision 15, Finding F5). The same data source serves the whole session (`SessionDbContextFactory`), which loses nothing: Supabase's pooler declines GSS encryption, as the fake pooler does | Strong |

## Table of Contents

1. Technical approach
2. The authenticator
3. The gate
4. The window
5. Texts
6. Tests
7. Findings
8. Threats and rollback
9. Slice, size and preconditions

## 1. Technical approach

```
LoginWindow ── LoginViewModel (Desk) ──► LoginGate (Pres) ──► attempt: IAuthenticator (Infra)
                 maps AuthenticationResult         counts, waits, picks the message, logs
                 to LoginOutcome (D3)              reads TimeProvider (D6)
```

The authenticator learns one new answer (§2). The gate owns the rules (§3). The view-model keeps what it does today for
`Accepted`, raising `LoginSucceeded`, and otherwise shows what the gate says (§4).

## 2. The authenticator

`Infra/Access/AuthenticationResult.cs` gains `Blocked(Exception Cause)`. `Infra/Access/NpgsqlAuthenticator.cs` gains, between
the `IsLoginRefusal` catch (line 103) and the `NpgsqlException` catch (line 111):

- a `catch (PostgresException ex) when (IsPoolerBlock(ex))` that disposes the data source, as the other catches do, and
  returns `Blocked(ex)`;
- `IsPoolerBlock`: `SqlState == PostgresErrorCodes.InternalError && MessageText.StartsWith("(ECIRCUITBREAKER)", Ordinal)`,
  beside `IsLoginRefusal` (lines 29-33).

`IsLoginRefusal` does not change: the `(EAUTHQUERY) user not found` case stays a refusal (spec Requirement 2, third
scenario).

The `NpgsqlConnectionStringBuilder` at lines 45-52 gains `GssEncryptionMode = GssEncryptionMode.Disable` (D11), with a
comment naming the retry it prevents. The comment at `ITests/LoginWithoutVerdictTests.cs:108`, which explains that the
fake pooler serves a second connection because Npgsql retries, is rewritten: after D11 there is no second connection.

## 3. The gate

`Pres/Login/LoginGate.cs` **(new)**, a plain class with no WPF types.

| Member | What it does |
|---|---|
| `LoginGate(TimeProvider time, IErrorLog log)` | |
| `static TimeSpan Wait` | 150 seconds (D10) |
| `bool IsWaiting` | `true` while `now < waitEndsAt` |
| `TimeSpan TimeLeft` | `waitEndsAt - now`, never negative |
| `string? Message` | What the window shows under the fields; `null` when nothing |
| `string? WaitTitle` | "Demasiados intentos fallidos" during a wait, else `null` |
| `Task<LoginOutcome?> TryLogInAsync(Func<Task<LoginOutcome>> attempt)` | Returns `null` without calling `attempt` while waiting (D4); otherwise runs it and applies the table below |

What each outcome does (spec Decision 8, Requirement 2 and Requirement 3):

| Outcome | Count | Wait | Message | Logged |
|---|---|---|---|---|
| `Accepted` | to 0 | none | `null` | no |
| `NotProvisioned` | to 0 | none | `LoginTexts.NotProvisioned` | no |
| `Rejected` | +1; on reaching 3, to 0 and the wait starts | from now, 150 s, on the third | `LoginTexts.Rejected`, or `LoginTexts.TooManyFailures` on the third | no |
| `Blocked` | to 0 | from now, 150 s | `LoginTexts.Blocked` | yes |
| `NoVerdict` | unchanged | none | `LoginTexts.NoVerdict` | yes |

When a wait has ended, the first question to `IsWaiting` or `Message` clears the wait message, so the message disappears
with the countdown, as Mateo's design shows. The username is never an input: the gate cannot count per user (spec
Decision 9).

## 4. The window

`Desk/LoginViewModel.cs`:

- Takes the `LoginGate` from `App` (D5). Its `LoginAsync` calls `_gate.TryLogInAsync(...)` with a lambda that calls
  `_authenticator.AuthenticateAsync(Username, Password)` and translates the result (D3); it keeps `SuccessResult` from the
  `Success` case, as today.
- `ErrorMessage` comes from `_gate.Message`; a new `WaitTitle` from `_gate.WaitTitle`; a new `ButtonText` is "Ingresar",
  or "Espere m:ss" from `_gate.TimeLeft` during a wait.
- `CanLogin` adds `!_gate.IsWaiting`, so the button and Enter (`IsDefault="True"`, `Desk/LoginWindow.xaml:41`) are disabled.
- A `DispatcherTimer`, one-second interval, runs only during a wait: each tick raises the changes above and stops itself
  when `IsWaiting` turns false.
- The `OperationRunner.DefaultErrorLog.Record("Login", ...)` line (`Desk/LoginViewModel.cs:86`) is removed: the gate logs (D8).

`Desk/LoginWindow.xaml`: the button's `Content` binds to `ButtonText`, and a title line above the message binds to
`WaitTitle`, hidden when `null`. Mateo's layout applies when S12 restyles the window; this change adds the two bindings
only.

`Desk/App.xaml.cs`: one `new LoginGate(TimeProvider.System, OperationRunner.DefaultErrorLog)` before the `while (true)` of
line 98, passed to each `new LoginViewModel(...)`.

## 5. Texts

`Pres/Login/LoginTexts.cs` **(new)**. The first three are today's, moved unchanged (`Desk/LoginViewModel.cs:74,81,87`); the
last three are Mateo's, approved by Tomás on 2026-11-13 (guide, "Iniciar sesión: dos estados nuevos").

| Constant | Text |
|---|---|
| `Rejected` | "Usuario o contraseña incorrectos." |
| `NotProvisioned` | "Esta cuenta no puede iniciar sesión. Consulte al administrador." |
| `NoVerdict` | "No se pudo conectar con el sistema. Revise la conexión a internet e intente de nuevo. Si sigue igual, avise al administrador." |
| `WaitTitle` | "Demasiados intentos fallidos" |
| `TooManyFailures` | "Hubo demasiados intentos fallidos. Espere 2:30 antes de volver a intentar. Si intenta antes, la espera se alarga." |
| `Blocked` | "Hubo demasiados intentos fallidos y el ingreso quedó en pausa. Espere 2:30 antes de volver a intentar. Si intenta antes, la espera se alarga." |
| `Countdown(TimeSpan left)` | "Espere m:ss", for example "Espere 2:30", "Espere 0:01" |

The "2:30" inside `TooManyFailures` and `Blocked` is written from `LoginGate.Wait`, not typed, so D10's one constant
stays one.

## 6. Tests

| Spec test | Where | How |
|---|---|---|
| 1, 2 | `ITests/LoginWithoutVerdictTests.cs` | Two new cases with the fake pooler (`LogInAgainstPoolerReplying`, as the case at line 23 uses it), one per breaker text, asserting `Blocked` and its `PostgresException`. Test 1 is shown failing first: today it is `Unavailable` |
| 3 | same file | The existing three cases, unchanged |
| 13 | same file | The fake pooler counts the connections it accepts; one refused login with a wrong-password error (`28P01`) and one with the breaker each leave the count at exactly 1. Shown failing first: today it is 2 |
| 4 to 12 | `PTests/LoginGateTests.cs` **(new)** | A fixed `TimeProvider` the test moves forward by hand, a `RecordingErrorLog` (`PTests/Fakes/RecordingErrorLog.cs`), and an `attempt` lambda that returns a scripted outcome and counts its calls (Test 11). Test 4 is shown failing with the count removed |
| 12 | same file | Asserts the six texts are pairwise distinct where the spec requires it, and that `Message` is the same whichever username the lambda closes over |

Decision 13 (the block is logged) is asserted inside Test 10, through `RecordingErrorLog`.

## 7. Findings

| # | Finding | Consequence |
|---|---|---|
| F1 | **The login loop builds a new view-model on every pass** (`Desk/App.xaml.cs:98-102`); a pass ends only after a successful login, which starts the count again anyway | One gate for the run (D5) costs nothing and survives any future change to the loop |
| F2 | **Presentation cannot see `AuthenticationResult`** (`PTests/PresentationArchitectureGuardTests.cs`) | D3 |
| F3 | **`ChangePasswordViewModel` has a voluntary mode, `isForced: false`, that nothing opens today** (`Desk/ChangePasswordViewModel.cs:36-43`; only `Desk/App.xaml.cs:117` builds it, forced). Mateo's subtitle "Su contraseña es provisoria" fits the only use | Not this change; noted for S12 if a voluntary entry is ever added |

## 8. Threats and rollback

| Threat | Answer |
|---|---|
| The pooler rewords its breaker | Only the prefix `(ECIRCUITBREAKER)` is matched. If even that changes, the block reads as `NoVerdict`, today's behaviour, and the three-failure wait still protects the office |
| The clock jumps (Windows adjusts the time) | `TimeLeft` is never negative, and a wait can at worst end early or late by the jump. Acceptable for a 150-second wait |
| A bug leaves the button disabled | The wait has a fixed end read from the clock, not a counter that must be decremented; closing and reopening the application clears it (spec P3) |

Rollback: install the previous version. No migration, no stored data.

## 9. Slice, size and preconditions

**One PR**, about 300 lines: authenticator, outcome and D11 (~30), `LoginGate` (~90), `LoginTexts` (~25), view-model, window and
`App` (~50), tests (~90 in `PTests`, ~20 in `ITests`). **Risk: item 2 (a guard on the login)**: the independent verifier
and the gentle-ai review run, as the proposal says.

| Step | Docker | Supabase |
|---|---|---|
| Code and full suite | **Yes** | No |
| Live check on PRUEBA: one PC typing wrong passwords as fast as the button allows never trips the breaker | No | **Yes**; Tomás types, the assistant launches the application and reads the log |
