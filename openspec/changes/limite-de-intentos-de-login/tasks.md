# Tasks: limite-de-intentos-de-login (keeping one person's typos from locking the office out)

Turns `design.md` §9 (one slice) into ordered, checkable work. It does not re-plan the change and does not edit `spec.md` or
`design.md`. Where applying exposes a gap, it is listed under **Issues found**.

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, and **Phase 2** (the live check you type) | **Phase 1** except its **Verification** table. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 280 authored (additions + deletions, tests included), design §9 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Low |
| Chained PRs recommended | No: one PR |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No

## Issues found

None yet.

## Deviations

None yet.

## Phase 1: The gate, the outcome and their tests (PR 1, est. about 280 lines)

**Docker: yes (full suite). Supabase: no. Model change: no.** Spec tests: 1 to 12. Requirements 1 to 4. Design anchors: D1 to
D10, §2 to §6, F1 and F2. Risk: item 2 (a guard on the login).

- [ ] 1.1 **[Second inspection, read-only]** Anchors match: `NpgsqlAuthenticator.cs:29-33,103,111`, `AuthenticationResult.cs`, `LoginViewModel.cs:74,81,86-87`, `LoginWindow.xaml:35,39-41`, `App.xaml.cs:98-102`, `LoginWithoutVerdictTests.cs:21-38`, `PresentationArchitectureGuardTests.cs`, `RecordingErrorLog.cs`, `AgencyClockTests.cs:14`. Baseline suite recorded with counts and `TIME_WAIT` (expected on `develop`: 2221 passed, Domain 738, Presentation 554, Infrastructure 929)
- [ ] 1.2 **[RED, Tests 1 and 2]** Two cases in `LoginWithoutVerdictTests` with the fake pooler, one per breaker text, asserting `AuthenticationResult.Blocked` and its `PostgresException`. Observed failing to compile, then, with `Blocked` declared and not yet returned, failing on `Unavailable`
- [ ] 1.3 **[GREEN]** `AuthenticationResult.Blocked`, `IsPoolerBlock` and its catch between lines 103 and 111 (design §2). Tests 1 to 3 green: the existing three cases unchanged
- [ ] 1.4 **[RED, Tests 4 to 12]** `LoginGateTests` in `Inmobiliaria.Presentation.Tests`, with a hand-written fixed `TimeProvider` moved forward by the test, `RecordingErrorLog`, and a scripted `attempt` lambda that counts its calls (design §6). Observed failing to compile
- [ ] 1.5 **[GREEN]** `Pres/Login/LoginOutcome.cs`, `LoginGate.cs` (the table of design §3) and `LoginTexts.cs` (design §5, texts exact, "2:30" written from `LoginGate.Wait`). Tests 4 to 12 green; `PresentationArchitectureGuardTests` green
- [ ] 1.6 **[Desktop]** `LoginViewModel` takes the gate, translates `AuthenticationResult` to `LoginOutcome`, binds `ErrorMessage`, `WaitTitle`, `ButtonText`, adds `!_gate.IsWaiting` to `CanLogin`, runs a one-second `DispatcherTimer` only during a wait, and loses its own log line (design §4). `LoginWindow.xaml` binds the button's content and the title line. `App.xaml.cs` builds one gate before the login loop. Build only: Desktop is not in CI
- [ ] 1.7 **[Prove the guards by making them fail]** Each mutation run and reverted byte-identical (md5): the count removed (`Rejected` never starts the wait) → Test 4; the early return during a wait removed → Test 11; `IsPoolerBlock` returning `false` → Tests 1 and 2
- [ ] 1.8 **[Guardrails]** Build, suite, per the table below, with `TIME_WAIT` sampled during the run
- [ ] 1.9 **[Independent verification and gentle-ai review]** Item 2 named. The verifier gets the whole spec, the baseline commit and the probe forms; the review runs on a scratch clone with one candidate commit holding code and tests only
- [ ] 1.10 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit code 0; **Advertencia(s)** and **Errores** both 0. MSB3027 means the app is open: close it |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + the new tests |

`has-pending-model-changes` is not needed: no model change.

---

## Phase 2: The live check (Tomás types, after PR 1 is merged)

**Docker: no. Supabase: yes, the test project (PRUEBA). Only Tomás types passwords; the assistant launches the application
and reads `%LOCALAPPDATA%\SistemaInmobiliaria\logs`.** Proposal §10, spec Preconditions.

- [ ] 2.1 **Rebuild and point at PRUEBA:** the compiled `appsettings.json` checked with `grep` for `chygrzoqmcqpncogeiuz` before opening (`docs/ESTADO.md`)
- [ ] 2.2 **Three wrong passwords:** after waiting 3 minutes with no attempts, three wrong passwords for an active user. The third shows "Demasiados intentos fallidos" and the button reads "Espere 2:30", counting down; Enter does nothing
- [ ] 2.3 **The office never trips:** keep typing wrong passwords as fast as the button allows for 5 minutes. No `ECIRCUITBREAKER` line appears in the log, and a correct password from another user logs in at the end
- [ ] 2.4 **Close:** `docs/ESTADO.md`'s failed-logins pending item removed; the `user-registry` delta merged into `openspec/specs/` at archive
