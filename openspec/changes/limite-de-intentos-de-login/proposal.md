# Proposal: limite-de-intentos-de-login (keeping one person's typos from locking the office out)

Written 2026-11-13, from `openspec/changes/limite-de-intentos-de-login/exploration.md`. Vocabulary: the **pooler** is
Supabase's shared connection pooler (Supavisor); a **breaker** is its circuit breaker, which refuses every new
connection for a while; a **failed login** is a login the database refused (`AuthenticationResult.Rejected`).

Rules come from the `user-registry` spec, Requirement "Login Is the Connection Attempt". This proposal cites them; it
does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope and §5 Decisions. **Skim** §9 Size. **Skip** §3, §4 and §6 to §8 unless you
want the detail.

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
9. Size Forecast
10. Success Criteria

---

## 1. Intent

About ten failed logins in two and a half minutes from the office lock every PC there out of the system for two
minutes, and the application says it is a network problem (exploration, **Finding** and **What the test proved**). This
change makes each PC slow itself down after a few failed logins, so one person's typos never reach the pooler's limit,
and makes the application say plainly when the block happens and that waiting is the cure.

It is required before the agency uses the system. Nicolás is involved in one number only: how many PCs log in (§5, P2).

## 2. Scope

### In Scope

- After **3 failed logins in a row** on one PC, the Ingresar button waits **2 minutes and 30 seconds**, with the time left
  on screen. A successful login starts the count again.
- When the pooler answers with its breaker (`ECIRCUITBREAKER`), the login window says so, says to wait, and the button
  waits too. The message does not read as a wrong password nor as a network problem.
- The `user-registry` spec: a new requirement for the wait, and the "no verdict" scenario no longer covers the breaker.
- Tests for the count, the wait, the reset and the new message, through the login's view-model.

### Out of Scope

- Changing the pooler, its thresholds, or how the system connects (exploration, options C and D).
- A session already open that needs a new connection during a block. It is rarer, because the session keeps the
  connections it holds, and it shows today's generic error. Recorded as a follow-up, not built here (P6).
- Counting failures across PCs, or on a server. Each PC counts its own.
- Telling the operator that a username does not exist. The spec forbids it and this change keeps that.

## 3. Capabilities

### New Capabilities

None.

### Modified Capabilities

- `user-registry`: a new requirement, "Failed Logins Slow Down on Each PC", and the scenario "A login with no verdict
  keeps the window open" narrowed so the pooler's breaker gets its own message.

## 4. Approach

**A new outcome for the breaker.** `NpgsqlAuthenticator` already sorts each failure into `Rejected` (the credentials) or
`Unavailable` (anything else). It gains a third, for the pooler's breaker, recognised by its `SqlState` and the
`(ECIRCUITBREAKER)` prefix the pooler puts in its message (exploration, log lines 19:17:44 to 19:33:33).

**The count lives in the login's view-model, in memory.** It counts `Rejected` results in a row, starts the wait on the
third, and starts it again on the breaker outcome. The clock is injected, so the wait is tested without sleeping. The
application's `IAgencyClock` gives only the date (`IAgencyClock.cs`), so the count takes .NET's `TimeProvider`, which
has a fake for tests. Where the counting class lives is design's call: it must be in a project
the Linux CI job builds, which `Inmobiliaria.Desktop` is not.

**Why 3 and 2½ minutes.** The pooler counts 10 failures in any 150 seconds from one address. With the wait measured from
the third failure, one PC can never put more than 3 failures in any 150-second stretch, so three PCs failing at the same
moment still stay at 9.

## 5. Decisions

| # | Decision | Strength |
|---|---|---|
| P1 | **Fix it in the application** (exploration options A and B together), not in Supabase | Strong. It is the only lever this system holds |
| P2 | **3 failures, then 2½ minutes.** Safe for up to three PCs failing at once. With four or more PCs logging in at the agency the margin is thinner; the number is one constant, and Tomás asks Nicolás how many PCs there are | Medium, until Nicolás answers |
| P3 | **The count is kept in memory, not on disk**: closing and reopening the application starts it again. It guards against mistakes, not against an attacker, who would not use this application; a file on disk would be state to keep right for little gain | Medium |
| P4 | **The breaker gets its own message and its own wait.** The wording is a design decision and Mateo writes it, for this message and for the 3-failure wait. Each must say three things: there were too many failed attempts, how long to wait, and that trying before then makes the wait longer. It must not read as a wrong password nor as a network problem | Strong on the wait and on what the message says; the wording is Mateo's |
| P5 | **The same message for every user.** The wait and the breaker message never depend on whether the username exists, as the spec already requires of the wrong-password message | Strong |
| P6 | **The deactivated-user breaker is reduced, not removed.** It fires at 5 failures in 5 minutes from anywhere and blocks for 30 seconds (exploration, **Finding**). With P2, one PC can still reach 6 in 5 minutes. Closing that would mean waiting 5 minutes after 3 failures, which punishes every honest typo to save 30 seconds | Medium |
| P7 | **An open session during a block is a follow-up**, not this change (§2, Out of Scope) | Weak. If design finds it is a few lines in the shared error handling, it goes in |

## 6. Affected Areas

| Area | Change |
|---|---|
| `src/Inmobiliaria.Infrastructure/Access/NpgsqlAuthenticator.cs`, `AuthenticationResult.cs` | The third outcome for the breaker |
| `src/Inmobiliaria.Desktop/LoginViewModel.cs`, `LoginWindow.xaml` | The count, the wait with the time left, the new message |
| A project the CI job builds (design decides which) | The counting class, so its tests gate CI |
| `tests/` | Tests for the count, the reset, the wait and the breaker outcome |
| `openspec/specs/user-registry/spec.md` | The new requirement and the narrowed scenario, at archive |
| `docs/ESTADO.md` | The pending item about failed logins closes once the live check passes |

## 7. Risks

| Risk | Likelihood | Answer |
|---|---|---|
| The pooler changes its message, so the breaker is no longer recognised | Low | It falls back to today's behaviour, the connection message, and the 3-failure wait still protects the office. A test pins the recognised text |
| Someone forgets the password and waits 2½ minutes between tries | Medium | Intended: three wrong tries mean asking the Administrador for a reset, not guessing. The message can say so |
| More PCs than P2 assumes fail at the same moment | Low | Needs four people mistyping three times each in the same 2½ minutes. The constant moves if Nicolás's answer requires it |
| The wait blocks a correct password typed right after three mistakes | Certain, by design | That is the price of keeping the office in. 2½ minutes once, against 2 minutes for everyone |

## 8. Rollback Plan

No stored data, no migration, no database change. Rolling back is installing the previous version.

## 9. Size Forecast

About 200 to 300 lines in one PR: the breaker outcome (~30), the counting class (~60), the view-model and the window
(~60), the tests (~120), and the spec delta. **Risk: item 2 (security: a guard on the login)**, so it gets the
independent verifier and the gentle-ai review.

**Preconditions:** Docker for the Infrastructure tests. Supabase PRUEBA restored for the final live check, where Tomás
types the wrong passwords on the real application; the assistant never handles a password.

## 10. Success Criteria

- On one PC, three wrong passwords in a row make Ingresar wait 2½ minutes, with the time left shown; a correct
  password after the wait logs in.
- When the pooler's breaker answers, the window shows the breaker message, not "Usuario o contraseña incorrectos." nor
  the connection message, and the button waits.
- Live, on PRUEBA: one PC typing wrong passwords as fast as the button allows never trips the breaker.
- Every existing login test passes unchanged, apart from the narrowed "no verdict" scenario.
