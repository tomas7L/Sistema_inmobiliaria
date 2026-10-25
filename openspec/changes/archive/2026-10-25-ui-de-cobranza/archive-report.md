# Archive Report: ui-de-cobranza

**Change**: `ui-de-cobranza` (the cash desk, the recibo on paper, and go-live setup)
**Archived to**: `openspec/changes/archive/2026-10-25-ui-de-cobranza/`
**Archive Date**: 2026-10-25
**Status**: CLOSED. Specs merged; implementation is recorded in `tasks.md` (73 of 73 tasks complete, human follow-ups H.1 to H.6 done).

**Merge method**: manual, as in `2026-10-15-cobranza-y-recibos`. The installed gentle-ai 4.2.0 still has no `sdd-archive-compose`. The merge was scripted (byte-splice, no model transcription of scenario text). Each delta requirement body was verified present verbatim in its target.

## Verification Before Archive

Run on 2026-10-25 against `ramatomy` (content identical to `origin/develop` at `cefe958`, PR #77), Docker running:

| Check | Result |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit 0, 0 warnings, 0 errors |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` | exit 0; 899 passed (Domain 373, Presentation 218, Infrastructure 308), 0 failed, 0 skipped |
| `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| Manual M1 to M3 (printing on paper) | Passed, H.4, 2026-10-24 (`tasks.md`, "Human Follow-Up Results") |
| Live money path (H.5) | Passed after the fixes merged in PR #77; the three delivery-blocking defects are resolved (`tasks.md`, "Fixes") |

## Main Specs Updated

| Spec | Action | Requirements |
|------|--------|--------------|
| `openspec/specs/desktop-shell/spec.md` | NEW (Purpose, Requirements, Tests) | 5 requirements (change Requirements 1 to 5), tests 1 to 7 |
| `openspec/specs/cash-desk/spec.md` | NEW (Purpose, Requirements, Tests) | 11 requirements (change Requirements 6 to 16), tests 8 to 26 with 13a and 24a to 24d, plus manual M1 to M3 |
| `openspec/specs/go-live-setup/spec.md` | NEW (Purpose, Requirements, Tests) | 3 requirements (change Requirements 17 to 19), tests 27 to 31 |
| `openspec/specs/contract-account/spec.md` | 1 MODIFIED | `A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date`: replaced by change Requirement 20 (cutover value bounds, Admin-only, four new scenarios) |
| `openspec/specs/access-control/spec.md` | 1 MODIFIED, 1 ADDED | MODIFIED `The Operation Rule Is Stated Over Actions, Not Screens` (two categories plus one named departure, new scenario); ADDED `The Cutover Date Is Admin-Only, Enforced by the Database` |
| `openspec/specs/receipt/spec.md` | Purpose edited, 1 ADDED | Purpose: display and system printing belong to `cash-desk`, PDF to `recibo-pdf` (change Requirement 23); ADDED `A Printed Recibo Says What the Issued Recibo Says` |

`collection` and `lease-contract` are not modified, as the change spec states.

The delta's `Decisions`, `Findings`, `Table of Contents`, `Capabilities covered`, `Out of Scope`, `Parameters Left to Design`, `Questions for Tomás` and `Preconditions` sections were not copied into main specs, matching the precedents.

## Tests Placement

New specs keep the change's test numbers, as `collection` and `receipt` did. Tests appended to living specs are **renumbered** after each spec's highest number, because two of the change's numbers already existed there:

| Spec | Change test | Living number |
|---|---|---|
| `contract-account` | 32, 33, 34 | 34, 35, 36 |
| `access-control` | 35, 36, 37, 38, 39, 40 | 30, 31, 32, 33, 34, 35 |
| `receipt` | 41 | 52 |

## Not Placed Cleanly

- **In-text requirement numbers.** Main specs carry unnumbered requirement headings, so text such as "(`cash-desk`, Requirement 15)" or "(`access-control`, Requirement 22)" cites the archived change's numbering. Look them up in `openspec/changes/archive/2026-10-25-ui-de-cobranza/spec.md`.
- **"this change" in `access-control`.** The modified operation rule says "the cutover date: this change"; it means `ui-de-cobranza`.
- **Requirement 23's scenario** ("The Purpose names both") is not carried. It checked the archive edit itself, and the Purpose now says it.
- **Decision 18 versus the code.** The spec allows any ANULADO placement; the implementation prints the block at the top of each half instead of design D13's diagonal watermark (`tasks.md`, 6.9 deviation). The spec text stays true.

## Follow-ups Carried Over

- **Open question Q2** (spec): whether the go-live screen is enough to catch a forgotten cutover date, or a "predates the system" marker is needed. Belongs to the initial data load. Held medium: no marker.
- **Printing:** wrap the sheet build in `ReciboViewModel.PrintAsync` so a failure becomes a print message (proposed for the next change that touches printing).
- **Review follow-ups, non-blocking (2026-10-24):**
  - `R1-clear-must-change-callable-standalone`: a user connecting with a raw SQL client could clear their own forced-change flag without changing the password. Option: fold the clearing into the password-changing function.
  - `R3-usepassword-after-commit-can-throw` / `R4-usepassword-throws-after-commit`: not reachable in production.
  - `R3-token-cancel-inner-logged-as-failure`, `R3-mask-misses-aliases`, `R4-error-log-sync-io-unbounded`.
- **Next change, required before delivery:** the Admin user-management screen (`openspec/specs/user-registry/spec.md:78`).
- **Known defect, not fixed:** the comment at `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.

## Verification Evidence

- `git mv` of the change folder: `diff -r` against a pre-move snapshot returned no output (exit 0).
- 23 of 23 delta requirement bodies (all but Requirement 23, which is a Purpose edit) verified present byte-identical in their main specs. The receipt Purpose edit was verified by exact-match replacement (one occurrence).
- No duplicated test number in any modified spec after the merge.
- All written files decode as UTF-8 and are CRLF, matching the existing main specs.

## Engram Traceability

The change artifacts were read from the filesystem (`openspec/changes/ui-de-cobranza/`) and then moved; no Engram observation IDs for proposal, spec, design or tasks were read in this archive run.
