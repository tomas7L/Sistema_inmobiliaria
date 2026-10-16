# Archive Report: cobranza-y-recibos (slice A)

**Change**: `cobranza-y-recibos` (slice A: tenant payment and recibo)
**Archived to**: `openspec/changes/archive/2026-10-15-cobranza-y-recibos/`
**Archive Date**: 2026-10-15
**Status**: CLOSED for slice A. Specs merged; implementation is recorded in `tasks.md`. H.6 and H.7 remain open and are the user's, not an agent's.

**Merge method**: manual. The installed gentle-ai 4.0.0 lacks `gentle-ai sdd-archive-compose`. Authorized by Tomás on 2026-10-15. The merge was scripted (byte-splice, no model transcription of scenario text), and each delta block was verified present verbatim in its target.

## Main Specs Updated

| Spec | Action | Requirements |
|------|--------|--------------|
| `openspec/specs/collection/spec.md` | NEW (Purpose, Requirements, Tests) | 7 requirements |
| `openspec/specs/receipt/spec.md` | NEW (Purpose, Requirements, Tests) | 10 requirements |
| `openspec/specs/contract-account/spec.md` | 3 ADDED, 1 RENAMED+MODIFIED, 1 MODIFIED | see below |
| `openspec/specs/lease-contract/spec.md` | 1 ADDED, 1 MODIFIED Note | see below |
| `openspec/specs/access-control/spec.md` | 1 ADDED | see below |

- **contract-account, ADDED**: `Payment and RecargoCharged Are Ledger Movements`; `A Honorarios Contractuales Instalment Is Payable With Its Rent Period`; `A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date`.
- **contract-account, RENAMED+MODIFIED**: `A Payment Settles the Oldest Unpaid Period` renamed to `A Payment Settles the Period the Tenant Names, or the Oldest Unpaid One`; body replaced.
- **contract-account, MODIFIED**: `An Opening Balance Exists Only for a Contract That Predates the System`; body replaced.
- **lease-contract, ADDED**: `A Contract Carries a Honorarios Contractuales Plan`.
- **lease-contract, MODIFIED Note**: applied to the existing requirement `Honorarios Percentage Is Stored, Nullable`. Only the note paragraph changed (the "not modelled anywhere yet, belongs to the collection change" sentence now says it is modelled by the plan requirement and that `receipt` reads the administration rate). The requirement text and scenarios are unchanged. The OPEN note on where the rate belongs is kept.
- **access-control, ADDED**: `The Collection Tables Are Granted Deliberately, and the Counter Is a Named Departure`.

The delta's `Decisions`, `Table of Contents`, `Out of Scope`, `Parameters Left to Design` and "Unchanged requirements" sections were not copied into main specs, matching the precedents.

## Not Placed Cleanly

- **Living Tests sections not reconciled.** The delta says tests 3, 4 and 5 replace living `contract-account` Tests 14 and 15, and tests 52 to 56 replace living Tests 9 and 10. The living `## Tests` sections of `contract-account` and `lease-contract` were left unchanged, so they still list the superseded numbers. The new `collection` and `receipt` specs carry their own Tests.

## Verification Evidence

- `git mv` of the change folder: `diff -r` against a pre-move snapshot returned no output (exit 0).
- Every delta requirement body (3 ADDED + 1 RENAMED + 1 MODIFIED in contract-account; 1 ADDED in lease-contract; 1 ADDED in access-control) verified present byte-identical in its main spec. Lease MODIFIED Note verified by exact-match replacement (one occurrence).
- All written and moved files decode as UTF-8.
- Spec files are CRLF, matching the existing main specs.

## Tasks at Close

`tasks.md` (archived, bookkeeping edits kept uncommitted): 83 of 85 complete. Handover boxes 1.15, 5.11, 6.13 and H.8 are ticked in that uncommitted edit.

Open, not agent-closable:

- **H.6**: go-live cutover dates and starting recibo number, under Admin, when real data is loaded. There is no screen for either yet.
- **H.7**: question for Nicolás on the first honorarios instalment when a contract starts after the due day. The default is implemented (instalment 1 rides with the first month that charges rent). A different answer changes `InstalmentNumberFor` only.

## Follow-ups Carried Over

- Issues found 10(b): the materialisation loop is duplicated between `AccountMaterialiser` and `CollectionDesk.BuildPreview`.
- Review follow-ups:
  - The `ConcurrentChange` end-to-end path is proved only by the error-mapping table test.
  - The lost-race test may not exercise the race.
  - Savepoint rollback should use `CancellationToken.None`.
  - Pv1 sslmode parsing accepts odd enum strings.
  - A flake window exists across Buenos Aires midnight in date-caching tests.
- `CollectionDesk` is not wired into the desktop app. The next change is the collection UI.
- Decision 10 (non-fiscal recibo) and Decision 3 (CCyCN art. 900 imputation order) still need an accountant or lawyer.

## Engram Traceability

The change artifacts were read from the filesystem (`openspec/changes/cobranza-y-recibos/`) and then moved; no Engram observation IDs for proposal, spec, design or tasks were read in this archive run. The prior blocked archive note is obs 119, `sdd/cobranza-y-recibos/archive-report`, which this report supersedes.
