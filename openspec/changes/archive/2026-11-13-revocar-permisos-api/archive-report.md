# Archive Report: revocar-permisos-api

**Change**: `revocar-permisos-api` (closing Supabase's API roles out of the database)
**Archived to**: `openspec/changes/archive/2026-11-13-revocar-permisos-api/`
**Archive Date**: 2026-11-13
**Status**: CLOSED. Spec merged; implementation and application are recorded in `tasks.md` (all tasks complete).

**Merge method**: scripted, with no model transcription of the added requirements, as in `2026-11-04-gestion-de-usuarios`.

## Verification Before Archive

| Check | Result |
|---|---|
| Suite, build and `has-pending-model-changes` on the merged code (PR #123, `b45f59d`) | `tasks.md` 1.6: exit 0, 2221 passed, 0 failed, 0 skipped; build 0 warnings; no pending model change. Not rerun: nothing under `src/` or `tests/` changed since |
| Applied to the test Supabase (`chygrzoqmcqpncogeiuz`) | `tasks.md` 2.1: exit 0; F2 query returned no rows; the app logged in |
| Applied to production (`bwiqgfhokolndygqkchp`), with `AddTenantNotices` first | `tasks.md` 2.2: exit 0; F2 query returned no rows; the app logged in as `nicolas` |

## Main Specs Updated

| Spec | Action | Requirements |
|------|--------|--------------|
| `openspec/specs/access-control/spec.md` | 5 ADDED | Change Requirements 1 to 5, before `## Tests`, so `access-control/spec.md:107`, cited by Requirement 4, stays valid |

The change spec labels the capability MODIFIED; no existing requirement was edited. The delta's `Decisions`, `Findings`,
`Table of Contents`, `Capabilities covered`, `Out of Scope`, `Parameters Left to Design`, `Questions for Tomás` and
`Preconditions` sections were not carried. They stay in the archived `spec.md`.

## Tests Placement

| Change test | Living number |
|---|---|
| 1 to 8 | 36 to 43 |
| 9 | not carried: conditional on closing trigger functions (P4), which design did not do |
| 10 | 44 |

## Not Placed Cleanly

- **In-text numbers.** Requirement, Decision, `P` and Test numbers inside the added requirements refer to the change's
  numbering; a note above them in `access-control` says where to find it. Requirement 4's "Test 8" is living Test 43.

## Follow-ups Carried Over

- **The Data API stays off** in both projects. With these grants gone it would expose nothing, but it stays off.
- **Supavisor's circuit breaker** (found in 2.2): repeated failed logins make the pooler block new connections for a while,
  shown by the app as its connection message. Whether that block is per user or for the whole project is not known. Recorded in
  `docs/ESTADO.md`.

## Verification Evidence

- 5 of 5 added requirement bodies verified identical to the change spec, by script, with only the heading format changed.
- `access-control` tests run 22 to 44 with no duplicated number.
