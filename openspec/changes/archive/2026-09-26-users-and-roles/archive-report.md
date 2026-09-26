# Archive Report: users-and-roles

**Change**: `users-and-roles`  
**Archived to**: `openspec/changes/archive/2026-09-26-users-and-roles/`  
**Archive Date**: 2026-09-26  
**Status**: CLOSED, READY FOR PRODUCTION

## Artifact Traceability

All SDD artifacts from Engram for this change:

| Artifact | Observation ID | Created | Content |
|----------|----------------|---------|---------|
| Proposal | #32 | 2026-09-21 19:15:16 | `sdd/users-and-roles/proposal` — Final shape after user override of credential-exposure |
| Spec | #33 | 2026-09-21 20:11:02 | `sdd/users-and-roles/spec` — Single-file spec covering 4 capabilities, 17 requirements, 36 scenarios, 34 numbered tests |
| Design | #35 | 2026-09-23 10:16:51 | `sdd/users-and-roles/design` — 10 numbered ADR-style decisions, revised twice after coordinator feedback |
| Tasks | #39 | 2026-09-23 11:48:56 | `sdd/users-and-roles/tasks` — 86 tasks across 6 PR slices + 4 Human Follow-Ups (H.1–H.4). All implementation tasks [x] checked complete. H.3 resolved with spec.md Decision 6. |
| Verify Report | #53 | 2026-09-25 20:34:42 | `sdd/users-and-roles/verify-report` — PASS WITH WARNINGS. 0 CRITICAL, 2 WARNING (both fixed in later commits), 2 SUGGESTION. 134 tests passing, 0 skipped. All 34 spec tests traced to implementation. |

## Capability Outcomes

This change introduces two new capabilities and modifies two existing ones. All are now live in the main specification directory.

### NEW Capabilities

| Capability | Location | Requirements | Scenarios | Tests |
|------------|----------|--------------|-----------|-------|
| user-registry | `openspec/specs/user-registry/spec.md` | 8 | 22 | 1–21 |
| access-control | `openspec/specs/access-control/spec.md` | 6 | 14 | 22–29 |

### MODIFIED Capabilities

| Capability | Location | Changes |
|------------|----------|---------|
| contract-documents | `openspec/specs/contract-documents/spec.md` | MODIFIED "Document Metadata" requirement: `UploadedBy` is now a real FK to `AppUser`, not a plain string. REMOVED "Uploader as Plain Identifier" requirement. Added Tests 30–31. |
| rent-adjustment | `openspec/specs/rent-adjustment/spec.md` | MODIFIED "Append-Only Adjustment History" requirement: adds `ConfirmedBy` column (nullable, no backfill). Expanded rationale for trigger-disable gap narrowing. Added Tests 32–34. |

## Implementation Completion

**All 86 implementation tasks completed across 6 chained PRs:**

- PR 1: Domain/Access (272 lines, under budget)
- PR 2a + 2b: EF Configuration + Migration (~1,031 lines, over budget, merged per owner decision)
- PR 3: Infrastructure/Access + tests (~1,181 lines, over budget, owner approved)
- PR 4: In-App Provisioning (~807 lines, 7 lines over budget, accepted)
- PR 5: Desktop Bootstrap (734 lines, under budget)

**All six PRs merged to `develop` and `main` as of commit `e2d5c91`** (verified in git history on branch `ramatomy`).

## Verification Status

**Final Verdict: PASS WITH WARNINGS** (as of 2026-09-25, updated with post-verification facts below)

| Category | Result |
|----------|--------|
| Build | Release build clean, 0 warnings, 0 errors |
| Tests | 134/134 passing, 0 skipped, 0 failed. Testcontainers `postgres:17.6` executed for every integration test. |
| Spec Tests | All 34 spec tests traced to production code and verified passing. 17 requirements, 36 scenarios covered. |
| CRITICAL Findings | 0 — clear to archive |

**Warnings (both resolved after verify report was written):**

1. **tasks.md H.3 checkbox status** — was unticked in verify-report. H.3 is resolved: task 3.12's `ADMIN OPTION` non-inheritance was confirmed, and PR 4 chose Empleado-only provisioning, matching spec.md Decision 6 which the owner had already confirmed. **Resolved** — H.3 checkbox now ticked with cross-reference.

2. **Review budget overage consent** — verify-report noted three slices exceeded 800-line budget under `ask-on-risk` delivery strategy without visible consent record. **Resolved** — tasks.md now records a review-budget consent table (added 2026-09-25) showing each slice's overage and the owner's approval decision for each (PR 2a merged by compiler necessity, PR 3 owner reviewed and approved, PR 4 7-line overage accepted).

**Suggestions (open, do not block archive):**

1. The credential-scan guard in `CompositionGuardTests` is a lexical substring search, not semantic. Acceptable for current scope; worth strengthening if config surface grows.

2. Local `develop` branch was 2 commits behind `origin/develop` at verification time — fetch/pull away, not a change defect.

## Migration Applied to Live Database

**H.1 (User-performed task): Applied to live Supabase project.** Status verified:

- `app_users` table exists, columns as specified (id, username, display_name, is_active, must_change_password)
- `inmobiliaria_admin` and `inmobiliaria_empleado` group roles exist
- Four functions exist: `app_set_role_password`, `app_create_login_role`, `app_set_role_login`, `app_clear_must_change_password`
- `contract_documents.uploaded_by` (string column) is gone; `uploaded_by_user_id` (UUID FK) is present
- `rent_adjustments.confirmed_by` (UUID FK, nullable) is present with no rows backfilled

**H.2 (User-performed task): Log settings verified against live Supabase.**

Query run by project owner 2026-09-25:
```
log_statement = ddl
log_min_duration_statement = -1
log_parameter_max_length = -1
```

Gate result: **PASSES**. Design Decision 3's function-wrapping strategy neutralizes password logging because the client sends `SELECT app_set_role_password(...)` (a SELECT, not DDL), and DDL inside the plpgsql function is not logged by `log_statement=ddl`.

## Bootstrap and Access Control

**First Admin provisioned and verified:**

- Role: `nicolas` (matched to project owner name from meeting notes)
- Grants: LOGIN, CREATEROLE, member of `inmobiliaria_admin` and `inmobiliaria_empleado` WITH ADMIN OPTION
- `app_users` row: exists, username='nicolas', is_active=true, must_change_password=true (per design)
- Status: Ready for owner's first login with forced password change

**Roles verified:**

- `inmobiliaria_admin`: SELECT on all tables, INSERT/UPDATE on `app_users`, `contract_documents`, `rent_adjustments`, `rent_adjustment_index_values`, all collection tables, and `economic_indices`
- `inmobiliaria_empleado`: SELECT on all tables, INSERT/UPDATE on `contracts`, `contract_documents`, `rent_adjustments`, `rent_adjustment_index_values`, and operational tables (NOT on `app_users` or provisioning functions)

## Final-State Authority: Post-Verification Facts

Per the user's launch prompt, the following facts supersede earlier states in `verify-report`:

- **H.3 checkbox resolution recorded**: Task 3.12's `ADMIN OPTION` proof failed as designed predicted. Decision taken: Empleado-only in-app provisioning (explicit spec.md Decision 6). H.3 checkbox now ticked with rationale.

- **Review budget consent table recorded**: tasks.md now includes a review-budget consent table (added 2026-09-25) recording each slice's authored lines, budget, and owner's decision/instruction. No slice was shipped without approval.

- **Human Follow-Ups status**:
  - H.1: Applied to live Supabase ✓
  - H.2: Log settings confirmed ✓
  - H.3: Resolved, checkbox ticked ✓
  - H.4: Carried to collection change (not this one's responsibility)

## Carried Forward as Follow-Ups

The following items remain open and are assigned to future changes:

1. **CHECK constraint on contracts.status** (tasks.md H.4, design Decision 5) — deferred to collection change because it would require editing `ContractConfiguration.cs` (owned by archived contract-and-parties change).

2. **Strengthen credential-scan guard** (verify-report SUGGESTION 1) — lexical substring search is acceptable now; semantic or exhaustive detector would help if config surface expands.

3. **Open questions for agency owner** in `openspec/domain/respuestas-del-dueno.md` — three questions remain unanswered, gating the collection change.

## Spec Sync Summary

**Merge Operations Completed:**

1. ✓ Created `openspec/specs/user-registry/spec.md` — extracted from change spec, 8 requirements, 21 tests
2. ✓ Created `openspec/specs/access-control/spec.md` — extracted from change spec, 6 requirements, 8 tests (22–29)
3. ✓ Modified `openspec/specs/contract-documents/spec.md` — updated Document Metadata requirement, removed Uploader as Plain Identifier, added Tests 30–31
4. ✓ Modified `openspec/specs/rent-adjustment/spec.md` — expanded Append-Only Adjustment History requirement with ConfirmedBy, added Tests 32–34

**Diff Verification**: Empty diff after all moves and modifications confirms byte-identity preservation.

## Archive Contents

Folder: `openspec/changes/archive/2026-09-26-users-and-roles/`

```
2026-09-26-users-and-roles/
├── proposal.md          (full proposal, 2026-09-21)
├── spec.md              (single-file spec, 640 lines, 4 capabilities)
├── design.md            (10 ADR decisions, revised twice)
├── tasks.md             (86 tasks + 4 human follow-ups, all implementation checked)
├── verify-report.md     (PASS WITH WARNINGS, all 34 tests traced)
└── archive-report.md    (this file, 2026-09-26)
```

## SDD Cycle Completion

This change has completed all five SDD phases:

1. **Propose** (2026-09-21): Proposal approved after user override of credential-exposure
2. **Spec** (2026-09-21): Single-file spec covering 4 capabilities
3. **Design** (2026-09-23): 10 decisions, revised after feedback
4. **Tasks** (2026-09-23): 86 tasks across 6 PR slices
5. **Apply** (2026-09-23 to 2026-09-25): All PRs merged, live database migrated
6. **Verify** (2026-09-25): PASS WITH WARNINGS, 0 CRITICAL blockers
7. **Archive** (2026-09-26): Specs synced, change folder moved, cycle closed

Ready for next change.

## Key Learnings

1. A migration and the EF model changes that precede it cannot be split across PR boundaries without creating a CI failure (PendingModelChangesWarning); when compiler/engine boundaries conflict with review-budget boundaries, the technical boundary wins and the overage is recorded in consent tables rather than hidden.

2. The `ADMIN OPTION` permission granted to a role does not automatically flow through group membership; it is scoped per `(role, member)` grant edge and must be granted explicitly — this was tested against real PostgreSQL 17.6 and is a proven fact, not a design assumption.

3. Tests that assert a previously-removed restriction (Empleado excluded from money operations, tests 25–26) must be positive, end-to-end assertions rather than smoke tests, because the guard must survive future readers encountering no obvious reason for the code that implements it.

4. Two MUST-WORK facts (ADMIN OPTION inheritance, Supabase `log_statement` setting) were unproven until tested against the real target environment; pre-flight assumptions made by the design phase were wrong for one of them and required immediate design changes that turned out to be compatible with the proposal because they were already recorded there.

5. A specification is final-state authority only when all SDD phases are complete, not at intermediate checkpoints; `verify-report` and `apply-progress` snapshots describe only the moment they were written, and later commits can resolve open items without requiring new verification runs (though approval traces matter for audit).
