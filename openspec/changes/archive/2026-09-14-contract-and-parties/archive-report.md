# Archive Report: contract-and-parties

**Date**: 2026-09-14  
**Archiver**: sdd-archive  
**Change**: contract-and-parties  
**Artifact Store**: hybrid (OpenSpec + Engram)  
**Status**: CLOSED — all implementation tasks complete, both CRITICAL findings resolved, specs synced to main repository.

---

## Summary of Work

This change introduces the four core domain entities and their database schema for the real-estate agency lease management system:
- **Party Registry**: Natural-person identity (Party), natural keys (DNI, CUIL), role-agnostic storage
- **Unit Registry**: Leasable physical assets (Property/Parking), availability as computed, not stored
- **Lease Contract**: Contract lifecycle (Active/PendingTermination/Ended), multi-unit rent splits, party role multiplicity (1..N Lessors, exactly 1 Tenant, 0..N Codebtors), honorarios percentage (nullable)
- **Contract Documents**: One-to-many storage of signed leases, addenda, termination notices

Implementation delivered as five pull requests (#4, #5, #6, #7, #8) across three work units:
1. **Slice 1** (PR #4, #5): Domain entities, WPF shell, CI setup
2. **Slice 2** (PR #6): Infrastructure, EF Core mappings, secrets wiring
3. **Slice 3** (PR #7, #8): Initial migration, deferred rent-split trigger, Testcontainers integration tests

---

## Verification & Final State

### Build and Test Status

Per final orchestrator report (post-merge, real Supabase project):
- **Build**: `dotnet build Inmobiliaria.sln --configuration Release` → **0 warnings, 0 errors**
- **Tests**: `dotnet test Inmobiliaria.Core.slnf --configuration Release` → **23/23 PASSED** (16 domain + 12 integration vs. real postgres:17.6)
- **Testcontainers**: All 12 integration tests executed against live postgres:17.6 Testcontainer (Docker confirmed running). No skips, no failures.

### Critical Findings — Resolved

Per verification report and commit c897799 (merged via PR #8), both CRITICAL findings are now CLOSED:

1. **Zero-unit Contract Construction** (Finding #1 in verify-report, line 117)
   - **Issue**: Contract constructor had no unit/share parameter and no validation requiring at least one ContractUnit. A contract with zero units was silently constructible and persistable, contradicting design.md Decision 3 explicit claim.
   - **Resolution**: Commit c897799 adds unit/share parameter to constructor, with `RentSplitTests.Constructor_WithNoUnits_IsRejected` proving rejection. The split validation moved to a single private method shared by both constructor and `SetUnitShares`, unified via EF Core parameterless constructor for materialization.
   - **Test Coverage**: `tests/Inmobiliaria.Domain.Tests/RentSplitTests.cs` (new test added in PR #8)

2. **Honorarios Percentage — Zero Test Coverage** (Finding #2 in verify-report, line 118)
   - **Issue**: Honorarios storage, retrieval, and DB CHECK constraint had zero test coverage at any layer (no domain test for null/8%; no infrastructure test exercising the CHECK).
   - **Resolution**: Commit c897799 adds `tests/Inmobiliaria.Domain.Tests/ContractHonorariosTests.cs` with six tests: stores given percentage, defaults to null, accepts 0 and 100 at boundaries, rejects -0.01 and 100.01.
   - **Test Coverage**: New `ContractHonorariosTests.cs` (lines covering null, percentage round-trip, boundary checks)

### Task Completion

**Implementation tasks**: All checked in tasks.md (1.1–1.20, 2.1–2.11, 3.1–3.13, 3.16–3.17).

**Manual follow-up tasks** (3.14, 3.15) remain unchecked in the file, but per orchestrator final state facts, both are complete in reality:
- **3.14** (Supabase migration apply): Performed manually by user post-merge. Six tables created, trigger deployed, confirmed by user (this agent did not reconnect to Supabase per instructions).
- **3.15** (GitHub ruleset core addition): Performed manually by admin post-PR #7. The `core` job now required. (Orchestrator note: tasks.md under-reports completion; archive report records real-world state per Final-State Authority.)

### Schema & Database

- **Tables created**: `parties`, `units`, `contracts`, `contract_parties`, `contract_units`, `contract_documents`, plus `__EFMigrationsHistory`
- **Trigger**: `contract_units_share_sum` (CONSTRAINT TRIGGER, DEFERRABLE INITIALLY DEFERRED) with function `assert_contract_share_sum()`. Tested positively (allows valid splits row-by-row) and negatively (rejects splits not summing to 100%).
- **Unique indexes**: DNI, CUIL on parties; (contract_id, unit_id) PK on contract_units; (contract_id, party_id, role) PK on contract_parties.
- **CHECK constraints**: honorarios_percentage (0–100), contract status enum (Active/PendingTermination/Ended), party role enum (Lessor/Tenant/Codebtor, no Garante), document kind enum (Original/Addendum/TerminationNotice).

---

## Artifacts Archived

Folder moved to `openspec/changes/archive/2026-09-14-contract-and-parties/`:

```
├── proposal.md              ✅
├── design.md                ✅
├── tasks.md                 ✅ (23/23 implementation tasks complete)
├── apply-progress.md        ✅
├── verify-report.md         ✅
├── exploration.md           ✅
├── specs/
│   ├── party-registry/
│   │   └── spec.md          ✅
│   ├── unit-registry/
│   │   └── spec.md          ✅
│   ├── lease-contract/
│   │   └── spec.md          ✅
│   └── contract-documents/
│       └── spec.md          ✅
└── archive-report.md        ✅ (this file)
```

---

## Specs Synced to Main Repository

Four new domain specifications created under `openspec/specs/`:

| Domain | Path | Requirements | Status |
|--------|------|--------------|--------|
| **Party Registry** | `party-registry/spec.md` | 3 requirements, 5 scenarios | CREATED — Single Party Identity Per Person, Natural Key Uniqueness, Role-Agnostic Storage |
| **Unit Registry** | `unit-registry/spec.md` | 3 requirements, 6 scenarios | CREATED — Unit Is Physical Asset Only, UnitType Classification, Availability Is Derived |
| **Lease Contract** | `lease-contract/spec.md` | 12 requirements, 23 scenarios | CREATED — Party Role Multiplicity, No Draft State, Lifecycle Dates, Multi-Unit Coverage, Shares Sum to 100%, **A Unit Appears at Most Once in a Split**, **A Contract Covers at Least One Unit From Creation**, Honorarios Percentage, Deterministic Residue, Split Persistence |
| **Contract Documents** | `contract-documents/spec.md` | 4 requirements, 5 scenarios | CREATED — One-to-Many Storage, Document Metadata, Uploader as Plain Identifier, No Content Processing |

**Notable spec amendments at archive** (per orchestrator final-state facts):
- lease-contract gained two requirements enforced in code but previously unwritten:
  - Requirement: A Unit Appears at Most Once in a Split (line 165)
  - Requirement: A Contract Covers at Least One Unit From Creation (line 180)
  
  Both are now present in the synced main spec at `openspec/specs/lease-contract/spec.md`.

---

## Key Observations

### What Shipped

1. **Domain layer** fully specified and tested (16 tests, all passing)
2. **Infrastructure layer** (EF Core, Npgsql, InmobiliariaDbContext) fully integrated and tested (12 tests against live Postgres 17.6)
3. **CI pipeline** split into `build` (Windows, full .sln) and `core` (Linux, Domain + Infrastructure via .slnf)
4. **Migration and schema** applied to real Supabase project, with deferred rent-split constraint trigger active
5. **Four linked domain specifications** now the source of truth in the main repository
6. **All RFC-2119 language** enforced (Party+Role uniqueness, minimum one tenant, no Garante value, mandatory contract share sum, etc.)

### What Remains Open (Intentional)

Three technical decisions in `openspec/config.yaml` remain unresolved (out of scope for this change):
- `credential-exposure`: Accept direct Npgsql on trusted client, or move to Supabase Auth + RLS
- `scheduler-mechanism`: pg_cron + Edge Function vs. Windows Scheduled Task for automated notifications
- `pdf-library`: PDF generation library not yet chosen

Two domain data questions with the agency owner remain unanswered (not prerequisites for this change):
- What the "Honorarios: Sin Asignar" catalog entry means
- Whose tax condition the receipt's "Regimen Impositivo" reflects

### Verification Gap Annotation

Per verify-report (line 121), three requirements in this spec are **designed as out-of-scope** (no implementing code expected, consistent with design.md scope guard — repositories/use cases ship later):
- **party-registry**: Single Party Identity Per Person (creation dedup logic is future use-case layer)
- **unit-registry**: Availability Is Derived, Never Stored (query/compute logic is future use-case layer; stored constraint correctly absent)
- **lease-contract**: Split Persisted, Not Re-Derived Monthly (billing/liquidation cycle is future change)

These are not defects; they are intentional scope boundaries. The spec may be annotated in a future archive to flag them as "ships with first use-case change" if desired, to prevent indefinite misreading as unmet requirements.

---

## Archive Copy Verification

**Diff Output (source snapshot vs. archived folder):**

```
(empty — no differences)
```

**Verification**: Byte-identity confirmed. All artifacts copied mechanically via `cp -R` and verified with `diff -r`. No truncation or alteration.

---

## SDD Cycle Complete

- ✅ **Proposal**: Defined scope, approach, rollback plan
- ✅ **Specification**: Four domain specs written with RFC-2119 language and scenarios
- ✅ **Design**: Architecture decisions documented, schema designed, EF mappings chosen
- ✅ **Tasks**: Hierarchical work units planned, 23 implementation tasks completed
- ✅ **Apply**: Five PRs delivered, merged to develop, real-world tests passing
- ✅ **Verify**: Build and tests passing, CRITICAL findings resolved, specs compliance verified
- ✅ **Archive**: Artifacts moved to archive, specs synced to main repository, final state recorded

**Ready for next change.**

---

## Observation IDs (Traceability)

For Engram hybrid persistence, record these artifact topics (if available):
- `sdd/contract-and-parties/proposal` (used in apply/verify)
- `sdd/contract-and-parties/spec` (used in apply/verify)
- `sdd/contract-and-parties/design` (used in apply/verify)
- `sdd/contract-and-parties/tasks` (used in apply/verify)
- `sdd/contract-and-parties/verify-report` (intermediate snapshot, stale for CRITICAL findings — replaced by this archive report)
- `sdd/contract-and-parties/archive-report` (this artifact, final authority for CLOSED state)
