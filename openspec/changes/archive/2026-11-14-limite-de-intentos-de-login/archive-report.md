# Archive Report: limite-de-intentos-de-login

**Change**: `limite-de-intentos-de-login` (each PC waits after three failed logins, and the pooler's block is its own outcome)
**Archived to**: `openspec/changes/archive/2026-11-14-limite-de-intentos-de-login/`
**Archive Date**: 2026-11-14
**Status**: CLOSED. Spec merged; implementation and the live check are recorded in `tasks.md` (all tasks complete).

**Merge method**: scripted, with no model transcription of the added requirements, as in `2026-11-13-revocar-permisos-api`.

## Verification Before Archive

| Check | Result |
|---|---|
| Suite and build on the merged code (PR #132, `2eb69f2`) | `tasks.md` 1.8: exit 0, 2236 passed, 0 failed, 0 skipped; build 0 warnings. Not rerun: nothing under `src/` or `tests/` changed since. Release build rerun on 2026-11-14 for 2.1: exit 0, 0 warnings, 0 errors |
| Live check on the test Supabase (`chygrzoqmcqpncogeiuz`) | `tasks.md` 2.2 and 2.3: the third wrong password starts the 2:30 wait, Enter does nothing during it, three cycles of wrong passwords never tripped the pooler (no error log written that day), and the correct password logged in after each wait |

## Main Specs Updated

| Spec | Action | Requirements |
|------|--------|--------------|
| `openspec/specs/user-registry/spec.md` | 1 MODIFIED, 4 ADDED | Requirement 1's scenario "A login with no verdict keeps the window open" gains "or the pooler's block"; change Requirements 2 to 5 added before `## Tests` |

The change's bold marking on the modified phrase was dropped. The delta's `Decisions`, `Findings`, `Table of Contents`,
`Capabilities covered`, `Out of Scope`, `Parameters Left to Design`, `Questions for Tomás` and `Preconditions` sections
were not carried. They stay in the archived `spec.md`.

## Tests Placement

| Change test | Living number |
|---|---|
| 1, 2 | 46, 47 |
| 3 | not carried: it is the existing `LoginWithoutVerdictTests` cases, already living Test 44 |
| 4 to 13 | 48 to 57 |

## Not Placed Cleanly

- **In-text numbers.** Requirement, Decision and `P` numbers inside the added requirements, and the `R#` tags on the added
  tests, refer to the change's numbering; a comment above the requirements in `user-registry` says where to find it.

## Follow-ups Carried Over

- **The wait reads the wall clock** (gentle-ai review, `tasks.md` Issues found): a Windows clock change during a wait ends
  it early or late. `TimeProvider.GetTimestamp` would remove it. No urgency.
- **How many PCs log in** (proposal P2): Tomás asks Nicolás; the answer can change one constant, not the spec.
- **The final wording of the two wait messages** is Mateo's (P4); the placeholder texts stand until then.

## Verification Evidence

- 4 of 4 added requirement bodies verified identical to the change spec, by script, with only the heading format changed.
- `user-registry` tests run 1 to 57, contiguous, with no duplicated number.
