# Archive Report: gestion-de-usuarios

**Change**: `gestion-de-usuarios` (the Admin's user screen)
**Archived to**: `openspec/changes/archive/2026-11-04-gestion-de-usuarios/`
**Archive Date**: 2026-11-04
**Status**: CLOSED. Specs merged; implementation is recorded in `tasks.md` (all tasks complete, follow-ups H.1 to H.3 done).

**Merge method**: manual and scripted, with no model transcription of the added requirements, as in `2026-10-25-ui-de-cobranza`.

## Verification Before Archive

Run on 2026-11-04 with Docker running, on content identical to `origin/develop` at `c35144f` (PR #84) apart from `docs/ESTADO.md`:

| Check | Result |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit 0, 0 warnings, 0 errors |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` | exit 0; 1011 passed (Domain 433, Presentation 244, Infrastructure 334), 0 failed, 0 skipped |
| `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." Last run before the test-only harness edit |
| CI on `develop` at `c35144f` | success |
| Live smoke H.2 and recheck H.3 on the test Supabase | Passed after the login correction in PR #84 (`tasks.md`). Production was not rechecked |

## Main Specs Updated

| Spec | Action | Requirements |
|------|--------|--------------|
| `openspec/specs/user-registry/spec.md` | 10 ADDED | Change Requirements 1 to 10, before `## Tests`, so the line references they make into this spec stay valid |
| `openspec/specs/desktop-shell/spec.md` | 1 MODIFIED | `Admin-Only Screens Are Hidden From an Empleado, and the Database Still Decides`: rewritten from change Requirement 11, one scenario added |

`access-control` is not modified, as the change spec states.

The delta's `Decisions`, `Findings`, `Table of Contents`, `Capabilities covered`, `Out of Scope`, `Parameters Left to Design`, `Questions for Tomás` and `Preconditions` sections were not carried. They stay in the archived `spec.md`.

## Tests Placement

| Spec | Change test | Living number |
|---|---|---|
| `user-registry` | 1 to 19 | 22 to 40 |
| `user-registry` | 23, 24, 25 | 41, 42, 43 |
| `user-registry` | none, added from the live smoke (`PoolerLoginRefusalTests`) | 44 |
| `desktop-shell` | 20 | merged into 2, as the change spec says it extends it |
| `desktop-shell` | 21 | merged into 3, as the change spec says it extends it |
| `desktop-shell` | 22 | 8 |

## Not Placed Cleanly

- **Requirement 11 is the one requirement not carried verbatim.** It is written as an instruction to change the existing requirement, so the existing one was edited to say it.
- **In-text requirement numbers.** Main specs carry unnumbered requirement headings, so text such as "(Requirement 5)" in the added requirements refers to the change's numbering. A note above them in `user-registry` says where to find it.
- **Test 44 and the login failure.** No requirement text changed for the pooler: Requirements 4 and 10 already demanded the ordinary message, and the code did not meet them until PR #84.

## Follow-ups Carried Over

- **Decision needed before delivery:** any other login failure, such as no network, still shows the generic message and closes the app (`src/Inmobiliaria.Desktop/App.xaml.cs:220`).
- **Before delivery:** check login refusals once on production; review and clear `%LOCALAPPDATA%\SistemaInmobiliaria\logs` on the agency's PC, which may hold passwords from before PR #81 (design F5).
- **Non-blocking:** no test catches removing the `SqlState` check from `IsLoginRefusal`. The message it guards is already specific.
- **Test data:** `muñoz` remains deactivated on the test Supabase.
- **Next change:** the initial load from Nicolás's Excel.

## Verification Evidence

- 10 of 10 added requirement bodies verified byte-identical to the change spec, by script, with only the heading format changed.
- `design.md`, `exploration.md`, `proposal.md` and `spec.md` verified identical to `HEAD` after the move, line endings aside. `tasks.md` differs only by checking 3.5.
- `user-registry` tests run 1 to 44 and `desktop-shell` tests run 1 to 8, with no duplicated number.
