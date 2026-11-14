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
| Estimated changed lines | About 300 authored (additions + deletions, tests included), design §9 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Low |
| Chained PRs recommended | No: one PR |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No

## Issues found

- **2026-11-13, found at 1.1: each refused login counts twice at the pooler.** Npgsql retries a failed open once without
  GSS encryption, so the application opens 2 connections per refused login (measured with a fake pooler; 1 with
  `GssEncryptionMode = Disable`). The exploration had said one. Tomás: "Si hazlo asi, corije ese error." Corrected in
  `exploration.md` and `proposal.md` §4; added spec Decision 15, Finding F5, Requirement 5 and Test 13; design D11; task
  1.3b.
- **Independent verifier, 2026-11-13: no blockers.** Six advisories:
  - **(1) Left:** `LoginSucceeded` now fires after `IsBusy = false`, not inside the `try`. Synchronous and the window
    closes on it; no visible effect.
  - **(2) Fixed:** the constructor comment in `LoginViewModel` claimed a window can open mid-wait after a cancelled
    password change; it cannot, since a new pass needs a successful login. The comment now says so.
  - **(3) Left:** the button may re-enable up to a second after the 150 s mark, because the timer ticks once a second;
    the gate itself is exact (Test 5). Within design D7.
  - **(4) Fixed:** `design.md`'s header still named Requirements 1 to 4 and Tests 1 to 12.
  - **(5) Not a defect:** it read `tasks.md` before 1.1 to 1.8 were checked off.
  - **(6) Left:** Test 13 runs with `SslMode.Disable`; the verifier also probed `Prefer`. `Require` needs TLS; the setting
    does not depend on the SSL mode (`NpgsqlConnector.OpenCore`).
- **gentle-ai review, 2026-11-13** (`review-798e40733e046965`, approved): three non-blocking findings, left for later.
  - **(R3-wallclock-wait, R4-wall-clock-wait) Left.** The wait reads the wall clock (`TimeProvider.GetUtcNow`), so a
    Windows clock change during a wait ends it early or late. Design §8 accepted it; a monotonic timestamp
    (`TimeProvider.GetTimestamp`) would remove it, as a follow-up.
  - **(R3-translate-untested) Left.** The translation from `AuthenticationResult` to `LoginOutcome` lives in
    `LoginViewModel`, which CI cannot test (spec Finding F2); the verifier read it and found all five cases covered.

## Deviations

None yet.

## Phase 1: The gate, the outcome and their tests (PR 1, est. about 300 lines)

**Docker: yes (full suite). Supabase: no. Model change: no.** Spec tests: 1 to 13. Requirements 1 to 5. Design anchors: D1 to
D11, §2 to §6, F1 and F2. Risk: item 2 (a guard on the login).

- [x] 1.1 **[Second inspection, read-only]** Anchors match: `NpgsqlAuthenticator.cs:29-33,103,111`, `AuthenticationResult.cs`, `LoginViewModel.cs:74,81,86-87`, `LoginWindow.xaml:35,39-41`, `App.xaml.cs:98-102`, `LoginWithoutVerdictTests.cs:21-38`, `PresentationArchitectureGuardTests.cs`, `RecordingErrorLog.cs`, `AgencyClockTests.cs:14`. Baseline suite recorded with counts and `TIME_WAIT` (expected on `develop`: 2221 passed, Domain 738, Presentation 554, Infrastructure 929) **Done 2026-11-13**: Docker 29.9.3; working tree clean at `1601262`; no file under `src/` or `tests/` changed since `f9d072a`, so the baseline is that run: 2221 passed (Domain 738, Presentation 554, Infrastructure 929). Every anchor as cited. Found here: each refused login opens two pooler connections (Issues found)
- [x] 1.2 **[RED, Tests 1 and 2]** Two cases in `LoginWithoutVerdictTests` with the fake pooler, one per breaker text, asserting `AuthenticationResult.Blocked` and its `PostgresException`. Observed failing to compile, then, with `Blocked` declared and not yet returned, failing on `Unavailable` **Done**: `ThePoolersBlock_IsItsOwnOutcome` (2 cases). Observed failing to compile (`Blocked` missing), then with `Blocked` declared: both failed with `Unavailable`
- [x] 1.3 **[GREEN]** `AuthenticationResult.Blocked`, `IsPoolerBlock` and its catch between lines 103 and 111 (design §2). Tests 1 to 3 green: the existing three cases unchanged **Done**: `IsPoolerBlock` and its catch; `LoginWithoutVerdictTests` 9 passed (5 existing unchanged + 4 new)
- [x] 1.3b **[RED then GREEN, Test 13, added 2026-11-13]** The fake pooler counts accepted connections; one refused login (wrong password, and breaker) must leave exactly 1. Observed failing at 2 on today's code; then `GssEncryptionMode = Disable` (design D11) and the comment at `LoginWithoutVerdictTests.cs:108` rewritten **Done**: `ARefusedLogin_ReachesThePoolerOnce` (28P01 and the breaker) failed at 2, then green with `GssEncryptionMode = Disable`; the line-108 comment rewritten
- [x] 1.4 **[RED, Tests 4 to 12]** `LoginGateTests` in `Inmobiliaria.Presentation.Tests`, with a hand-written fixed `TimeProvider` moved forward by the test, `RecordingErrorLog`, and a scripted `attempt` lambda that counts its calls (design §6). Observed failing to compile **Done**: `LoginGateTests` (11) and `Fakes/ManualTimeProvider`; observed failing to compile (`Inmobiliaria.Presentation.Login` missing)
- [x] 1.5 **[GREEN]** `Pres/Login/LoginOutcome.cs`, `LoginGate.cs` (the table of design §3) and `LoginTexts.cs` (design §5, texts exact, "2:30" written from `LoginGate.Wait`). Tests 4 to 12 green; `PresentationArchitectureGuardTests` green **Done**: 11 passed; `PresentationArchitectureGuardTests` green
- [x] 1.6 **[Desktop]** `LoginViewModel` takes the gate, translates `AuthenticationResult` to `LoginOutcome`, binds `ErrorMessage`, `WaitTitle`, `ButtonText`, adds `!_gate.IsWaiting` to `CanLogin`, runs a one-second `DispatcherTimer` only during a wait, and loses its own log line (design §4). `LoginWindow.xaml` binds the button's content and the title line. `App.xaml.cs` builds one gate before the login loop. Build only: Desktop is not in CI **Done**: `Inmobiliaria.sln` built, exit 0, 0 Advertencia(s), 0 Errores
- [x] 1.7 **[Prove the guards by making them fail]** Each mutation run and reverted byte-identical (md5): the count removed (`Rejected` never starts the wait) → Test 4; the early return during a wait removed → Test 11; `IsPoolerBlock` returning `false` → Tests 1 and 2; `GssEncryptionMode = Disable` removed → Test 13 **Done**, each reverted byte-identical (md5): count removed (`< int.MaxValue`) → Test 4 and four others; early return made unreachable (`IsWaiting && _log is null`) → Test 11 only; `IsPoolerBlock` forced false → Tests 1-2; GSS line removed → Test 13. A first try with `if (true)`/`if (false)` did not compile (warnings are errors) and proved nothing: discarded
- [x] 1.8 **[Guardrails]** Build, suite, per the table below, with `TIME_WAIT` sampled during the run **Done 2026-11-13**: build exit 0, 0/0; suite exit 0, 2236 passed (Domain 738, Presentation 565 = 554 + 11, Infrastructure 933 = 929 + 4), 0 failed, 0 skipped; `TIME_WAIT` 27 before, peak 5,114 during (20-second samples), 5,233 after
- [x] 1.9 **[Independent verification and gentle-ai review]** Item 2 named. The verifier gets the whole spec, the baseline commit and the probe forms; the review runs on a scratch clone with one candidate commit holding code and tests only **Done 2026-11-13.** Verifier: R1 to R5 and Tests 1 to 13 met, no blockers; its own probes reproduced 2 connections per refused login at `1601262` and 1 with the change, under `SslMode` Disable and Prefer; six advisories (Issues found). gentle-ai, on a scratch clone in one candidate commit (code and tests only; 11 files, 635 lines; lenses risk, resilience, reliability; consent granted by Tomás): `review-798e40733e046965`, approved, acknowledged; three non-blocking findings (Issues found)
- [x] 1.10 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant **Handed over 2026-11-13**: 16 paths (11 code and tests, 5 change documents), plus the reading extract outside git

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
