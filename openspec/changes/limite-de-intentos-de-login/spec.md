# Spec: limite-de-intentos-de-login (keeping one person's typos from locking the office out)

Single-file specification for the `limite-de-intentos-de-login` change. One capability is **MODIFIED**:
**`user-registry`**. None is new.

Vocabulary, glossed once: the **pooler** is Supabase's shared connection pooler (Supavisor); a **breaker** is its circuit
breaker, which refuses every new connection for a while; a **failed login** is a login the database refused, the
`AuthenticationResult.Rejected` outcome; a **block** is the pooler answering with its breaker; the **wait** is the time
the login window keeps the Ingresar button disabled.

The proposal is `openspec/changes/limite-de-intentos-de-login/proposal.md`, merged as PR #126 on 2026-11-13, with its
exploration beside it. Rules already in `user-registry` are cited, not restated.

**What this spec does not do.** It states what must be true, observably. Where the counting lives, how the countdown is
drawn, and the operator texts' final wording (Mateo's, proposal P4) are not decided here.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Decisions** table, rows 8 to 14 | The calls this spec had to make beyond the proposal. Each has a strength. The **medium** rows are where you can change my mind. |
| Requirements 2 and 3 | The block, and the wait after three failures: the two promises that matter. |

**Skip** the Tests section and "Parameters Left to Design". **Skim** the rest; the scenarios are there so a test can be
written from each one.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code, a living spec, or the test database
(source given). **SPEC CALL** = this spec had to decide because the proposal did not. Strength: **strong**, **medium**,
**weak** (act on it only provisionally).

Rows P1 to P7 are the proposal's Decisions, in the proposal's numbering, merged as PR #126 on 2026-11-13. Rows 8 onwards
are this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | Fix it in the application, not in Supabase. | Proposal §5 | Strong |
| P2 | 3 failed logins in a row, then a wait of 2½ minutes. | Proposal §5 | Medium, until Nicolás says how many PCs log in |
| P3 | The count is kept in memory: closing and reopening the application starts it again. | Proposal §5 | Medium |
| P4 | The block gets its own message and its own wait. The wording is Mateo's; each message says there were too many failed attempts, how long to wait, and that trying earlier makes the wait longer. | Proposal §5, CONFIRMED BY USER 2026-11-13 | Strong on the content; the wording is Mateo's |
| P5 | The same message and the same wait for every username. | Proposal §5 | Strong |
| P6 | The deactivated-user breaker is reduced, not removed. | Proposal §5 | Medium |
| P7 | A session already open during a block is a follow-up. | Proposal §5 | Weak |
| 8 | **Only a failed login counts.** A login that reaches the database (`Success`, and `NotProvisioned`, which the database accepted) starts the count again. A login with no verdict (`Unavailable`) neither counts nor starts it again: the pooler never judged it, and resetting on it would let a bad connection erase real failures. | SPEC CALL. The pooler counts only refused credentials (exploration, **Finding**) | Strong |
| 9 | **The count belongs to the PC, not to the username.** Changing the username between attempts does not start it again. The pooler counts per address, and a count per username would let five usernames reach fifteen failures. | SPEC CALL, following P5 | Strong |
| 10 | **A block waits 150 seconds too, the same as the three-failure wait**, although the pooler blocks for 120. The pooler remembers failures for 150 seconds, so after a 120-second wait one more failure could close it again at once; after 150 seconds every earlier failure has dropped out. | SPEC CALL. `circuit_breaker.ex:36-40` (exploration) | Medium |
| 11 | **A block is recognised by `SqlState` `XX000` and a message starting with `(ECIRCUITBREAKER)`**, which covers both breakers, not by their full sentences. | VERIFIED FACT: both texts in the log of 2026-11-13, 19:24:33 and 19:33:01 (exploration, **What the test proved**) | Strong |
| 12 | **During a wait the application opens no connection.** The button is disabled and pressing Enter does nothing; an attempt the pooler never sees cannot count against the office. | SPEC CALL | Strong |
| 13 | **A block is written to the error log**, as a login with no verdict is today, so a block at the agency leaves evidence of when it happened. | SPEC CALL, following `user-registry` "A login with no verdict keeps the window open" | Strong |
| 14 | **Until Mateo's wording arrives, the code carries placeholder texts** that already say the three things P4 requires, marked as placeholders the way `MoraTexts` and `NoticesTexts` are. | SPEC CALL | Medium |

### Findings (the code and the test database)

| # | Finding | Consequence |
|---|---|---|
| F1 | **A fake pooler already exists in the tests**: `LoginWithoutVerdictTests.cs` answers the login with an exact pooler error from a loopback socket. | The block is tested without Supabase (Tests 1 and 2). |
| F2 | **The login's view-model has no tests, and cannot have them in CI**: `LoginViewModel` lives in `Inmobiliaria.Desktop`, which `Inmobiliaria.Core.slnf` leaves out. | The counting and the wait live where `Inmobiliaria.Presentation.Tests` can reach them; the view-model only shows their state. |
| F3 | **The application's clock gives only the date**: `IAgencyClock.Today` is a `DateOnly`. | The wait reads .NET's `TimeProvider`, which has a fake for tests. |
| F4 | **Today a block falls under "A login with no verdict keeps the window open"**, which requires the check-your-connection message (`user-registry/spec.md:50-56`). | Requirement 1 narrows that scenario. |

## Table of Contents

1. Capabilities covered
2. Requirement 1: Login Is the Connection Attempt (modified)
3. Requirement 2: The Pooler's Block Is Told Apart
4. Requirement 3: Failed Logins Slow Down on Each PC
5. Requirement 4: The Waits Say What Happened
6. Out of Scope
7. Parameters Left to Design
8. Questions for Tomás
9. Preconditions
10. Tests

## Capabilities covered

| Capability | Status | Requirements |
|---|---|---|
| `user-registry` | MODIFIED | 1 (modified), 2, 3 and 4 (new) |

## Capability: user-registry (MODIFIED)

### Requirement 1: Login Is the Connection Attempt (modified)

Unchanged, except its scenario "A login with no verdict keeps the window open", which now reads:

#### Scenario: A login with no verdict keeps the window open

- GIVEN the database cannot be reached, or fails with anything other than refusing the login **or the pooler's block**
- WHEN "maria" attempts to log in
- THEN the login window MUST stay open with what she typed, and show a message telling her to check the connection and try again
- AND that message MUST NOT read as a wrong password
- AND the failure MUST be written to the error log

### Requirement 2: The Pooler's Block Is Told Apart

When the pooler refuses a login with its breaker (Decision 11), the login MUST end in its own outcome, neither a failed
login nor a login with no verdict. The window MUST stay open with what was typed, MUST show the block message
(Requirement 4), MUST start the wait at once for 150 seconds (Decision 10), and MUST write the refusal to the error log
(Decision 13).

#### Scenario: Too many wrong passwords from the office

- GIVEN the pooler answers `XX000` with "(ECIRCUITBREAKER) too many authentication failures, new connections are temporarily blocked"
- WHEN "maria" attempts to log in, with her correct password
- THEN the window MUST show the block message, not "Usuario o contraseña incorrectos." nor the connection message
- AND the wait MUST start, for 150 seconds

#### Scenario: Too many attempts with an unknown or deactivated user

- GIVEN the pooler answers `XX000` with "(ECIRCUITBREAKER) failed to retrieve database credentials after multiple attempts, new connections are temporarily blocked"
- WHEN anyone attempts to log in
- THEN the outcome MUST be the same as in the scenario above

#### Scenario: The pooler's other errors are unchanged

- GIVEN the pooler answers "(EAUTHQUERY) user not found in the database", or any `XX000` error not starting with `(ECIRCUITBREAKER)`
- WHEN a login is attempted
- THEN the outcome MUST be what it is today: a failed login for the first, a login with no verdict for the rest

### Requirement 3: Failed Logins Slow Down on Each PC

The application MUST count failed logins in a row on the PC it runs on, for as long as it stays open (P3). On the third,
the wait MUST start, for 150 seconds measured from that third failure (P2). During the wait the window MUST show the time
left, and MUST NOT attempt a login (Decision 12). When the wait ends, the button MUST be enabled again and the count MUST
start again. What counts and what starts the count again is Decision 8; the username does not matter (Decision 9).

#### Scenario: Two mistakes do not wait

- GIVEN two failed logins in a row
- WHEN the second is refused
- THEN the button MUST be enabled at once, with "Usuario o contraseña incorrectos." as today

#### Scenario: The third mistake waits

- GIVEN two failed logins in a row
- WHEN a third is refused
- THEN the button MUST be disabled, and the window MUST show 2:30 left, counting down
- AND at 149 seconds after the third failure the button MUST still be disabled
- AND at 150 seconds it MUST be enabled

#### Scenario: Another username does not escape the wait

- GIVEN two failed logins as "maria"
- WHEN a third is refused as "jose"
- THEN the wait MUST start

#### Scenario: A lost connection does not erase the count

- GIVEN two failed logins in a row, then a login with no verdict
- WHEN the next login is refused
- THEN the wait MUST start

#### Scenario: A login the database accepted starts the count again

- GIVEN two failed logins in a row, then a login the database accepted that the application refuses as not provisioned
- WHEN the next login is refused
- THEN the button MUST be enabled at once: that is the first failure of a new count

#### Scenario: Nothing reaches the database during the wait

- GIVEN a wait has started
- WHEN the operator presses Enter or tries to log in in any way before it ends
- THEN no login attempt MUST be made

### Requirement 4: The Waits Say What Happened

Each wait MUST show its own message: the three-failure message (Requirement 3) and the block message (Requirement 2).
Each MUST say there were too many failed attempts, how long to wait, and that trying earlier makes the wait longer. Neither
MAY read as a wrong password nor as a network problem, and each MUST be worded identically whatever username was typed
(P5). The final wording is Mateo's (P4); until it arrives, placeholder texts that meet this requirement stand (Decision 14).

#### Scenario: The messages are their own

- GIVEN the three-failure wait, and separately a block
- WHEN each message is shown
- THEN neither MUST equal "Usuario o contraseña incorrectos." nor the connection message

#### Scenario: The username changes nothing

- GIVEN the same wait reached once with an existing username and once with one that does not exist
- WHEN the message is shown
- THEN it MUST be the same text

## Out of Scope

| Not here | Why |
|---|---|
| Changing the pooler, its thresholds, or how the system connects | Proposal §2; exploration options C and D |
| A session already open that needs a new connection during a block | P7 |
| Keeping the count on disk, or across PCs | P3, Decision 9 |
| Telling the operator a username does not exist | `user-registry`, "An unknown username produces the identical failure message" |
| The final wording of the two messages, and how the countdown looks | P4, Mateo |

## Parameters Left to Design

- Where the counting and the wait live in `Inmobiliaria.Presentation`, and how `LoginViewModel` shows them (Finding F2).
- The name of the new authentication outcome.
- How the time left is refreshed on screen (a timer on the window, ticking once a second, is enough).
- The placeholder texts (Decision 14).

## Questions for Tomás

None open. Tomás asks Nicolás how many PCs log in (P2); the answer can change one constant, not this spec.

## Preconditions

| Phase | Docker | Supabase |
|---|---|---|
| This spec, design, tasks | No | No |
| The code and its tests | **Yes**, for the full suite | No |
| The live check: one PC typing wrong passwords on PRUEBA never trips the breaker | No | **Yes**; Tomás types, the assistant launches the application and reads the log |

## Tests

Each names the requirement it proves and the project it lives in. Tests 1 to 3 use the fake pooler of Finding F1, in
`Inmobiliaria.Infrastructure.Tests`. Tests 4 to 12 use a fake `TimeProvider` and a fake authenticator, in
`Inmobiliaria.Presentation.Tests`. A skipped test is an unrun test.

1. **The pooler's "(ECIRCUITBREAKER) too many authentication failures, …" reply is the block outcome**, and the exception
   is kept for the error log. Shown failing on today's code, which calls it a login with no verdict. R2
2. **The pooler's "(ECIRCUITBREAKER) failed to retrieve database credentials after multiple attempts, …" reply is the block
   outcome.** R2
3. **"(EAUTHQUERY) user not found in the database" is still a failed login, and "Tenant or user not found" and
   "(EAUTHQUERY) unsupported or invalid secret format" are still logins with no verdict.** The existing
   `LoginWithoutVerdictTests` cases, unchanged. R1, R2
4. **Two failed logins leave the button enabled; the third disables it.** Shown failing with the count removed. R3
5. **The wait ends exactly 150 seconds after the third failure**: disabled at 149, enabled at 150, and the time left
   reads 2:30 at the start and 0:01 a second before the end. R3
6. **After the wait ends, one failed login does not start a new wait.** R3
7. **A third failure under a different username starts the wait.** R3, Decision 9
8. **A login with no verdict between failures neither counts nor starts the count again.** R3, Decision 8
9. **A successful or not-provisioned login starts the count again.** R3, Decision 8
10. **A block starts the 150-second wait at once, with no failures before it.** R2
11. **During a wait, logging in reaches the authenticator zero times.** The fake authenticator counts its calls. R3,
    Decision 12
12. **The three-failure message and the block message differ from each other, from "Usuario o contraseña incorrectos."
    and from the connection message, and each is the same text for any username.** R4

The existing login tests (`AuthenticationTests`, `ChosenPasswordAndLoginTests`, `LoginWithoutVerdictTests` and the
user-administration tests) MUST pass unchanged.

Verification, beyond the tests: Tests 1 and 4 are shown failing as stated; and the live check on PRUEBA (Preconditions)
is run once, with its result recorded in `tasks.md`.
