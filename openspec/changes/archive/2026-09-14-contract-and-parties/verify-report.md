# Verify Report: contract-and-parties

**Date**: 2026-09-14
**Verifier**: sdd-verify
**Change state at verification time**: `develop` at `aa9b9d5`, PR #4-#7 merged. Migration `20260913215911_InitialSchema` applied by the user to the real Supabase project (not re-verified here -- this agent did not connect to Supabase or run `dotnet ef database update`, per instructions).

## Verdict: PASS WITH WARNINGS

Build and both test projects pass in full, for real, against a live `postgres:17.6` Testcontainer (Docker Desktop was down at session start, was started, and the full suite then ran to completion -- see Build & Test Evidence). Domain and infrastructure code match the four specs closely. Two issues are serious enough to flag prominently before archive: (1) `Contract` does not actually forbid zero-unit construction, contradicting an explicit, repeated design claim; (2) the Honorarios-percentage requirement has zero test coverage at either the domain or database level. Neither is a build/test failure -- both are silent correctness gaps.

---

## Build & Test Evidence

| Command | Result |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | Succeeded. 0 warnings, 0 errors. All 5 projects compiled (Domain, Infrastructure, Desktop, Domain.Tests, Infrastructure.Tests). |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` | Succeeded. Inmobiliaria.Domain.Tests: 16 total, 16 passed, 0 failed, 0 skipped. Inmobiliaria.Infrastructure.Tests: 12 total, 12 passed, 0 failed, 0 skipped. |

Environment note: Docker Desktop was not running at the start of this verification session (docker info failed with "cannot find the file specified" on the named pipe -- the daemon process was not present at all, not merely unresponsive). This agent started Docker Desktop.exe and polled until the daemon came up, then re-ran the full test command. All 12 integration tests in SchemaConstraintTests.cs therefore executed for real against a live postgres:17.6 container via Testcontainers (not skipped), matching the expected 16 domain + 12 integration count from the task brief. No test in either project failed or skipped.

---

## Spec Compliance Matrix

Status legend: COVERED = a runtime test passed against this exact scenario. STRUCTURAL = the scenario asserts an absence (no column/no field); verified by reading the entity/migration, not by a runtime assertion, since there is nothing to execute. DEFERRED/OUT OF SCOPE = the scenario describes application/query-layer behavior that design.md's scope guard explicitly excludes from this change (no repositories, no use cases ship here) -- no implementing code exists yet, so none was expected. UNTESTED = in-scope, implemented code with no covering test at all -- a genuine gap.

### party-registry

| Requirement | Scenario | Status | Evidence |
|---|---|---|---|
| Single Party Identity Per Person | Same person as owner and later tenant | DEFERRED/OUT OF SCOPE | No Party-creation/reuse service exists in this change (design scope guard: repositories/use cases arrive later). ContractPartyAssignmentTests.AssignParty_SamePartyAsLessorAndCodebtor_BothAccepted proves the domain permits one Party in two roles, which is the substrate this future logic needs, but the reuse-instead-of-duplicate decision itself is not implemented anywhere yet. |
| Single Party Identity Per Person | Condominio does not duplicate identity | DEFERRED/OUT OF SCOPE | Same reasoning; no creation-dedup logic exists in this change. |
| Natural Key Uniqueness | Duplicate DNI rejected | COVERED | SchemaConstraintTests.DuplicateDni_RejectedByUniqueIndex -- passed against real Postgres. |
| Natural Key Uniqueness | Duplicate CUIL rejected | COVERED | SchemaConstraintTests.DuplicateCuil_RejectedByUniqueIndex -- passed against real Postgres. |
| Role-Agnostic Storage | Querying identity returns no role data | STRUCTURAL | Party.cs has no role/contract fields at all (checked directly); nothing to query differently. No dedicated regression test guards against a future accidental field addition. |

### unit-registry

| Requirement | Scenario | Status | Evidence |
|---|---|---|---|
| Unit Is Physical Asset Only | Unit record has no availability column | STRUCTURAL | Unit/PropertyUnit/ParkingUnit (checked directly) and the units table in the generated migration have no availability/tenant/contract column. |
| UnitType Classification | Create a property unit | COVERED | SchemaConstraintTests.TphRoundTrip_PreservesConcreteUnitType persists and reads back a PropertyUnit. |
| UnitType Classification | Create a cochera unit | COVERED | Same test, ParkingUnit half. |
| Availability Is Derived, Never Stored | Unit becomes available when its contract ends | DEFERRED/OUT OF SCOPE, but flagged | No availability-computation code exists anywhere in src/ (grep for Available/IsAvailable/Availability returns only a doc-comment in Unit.cs). This is consistent with design.md scope guard (no use cases, repositories ship here), but it means this requirement actual behavior is 0% implemented, not merely untested. SchemaConstraintTests.TwoSequentialContractsOnSameUnit_BothRemainReadable proves the data shape a future query would need (two contract_units rows for one unit, both readable), nothing more. |
| Availability Is Derived, Never Stored | Unit under an active contract is not available | Same as above | Same as above. |
| Availability Is Derived, Never Stored | Unit with prior ended contract is available | Same as above | Same as above. |

### lease-contract

| Requirement | Scenario | Status | Evidence |
|---|---|---|---|
| Party Role Multiplicity | Condominio -- two lessors | COVERED | ContractPartyAssignmentTests.AssignParty_TwoLessors_BothAccepted. |
| Party Role Multiplicity | Exactly one tenant enforced | COVERED | ContractPartyAssignmentTests.AssignParty_SecondTenant_IsRejected. |
| Party Role Multiplicity | Zero or many codebtors | COVERED | ContractPartyAssignmentTests.AssignParty_ZeroOrTwoCodebtors_BothAccepted. |
| Role-Scoped Uniqueness | Same party as lessor and codebtor | COVERED | ContractPartyAssignmentTests.AssignParty_SamePartyAsLessorAndCodebtor_BothAccepted. |
| Role-Scoped Uniqueness | Duplicate Party+Role rejected | COVERED | ContractPartyAssignmentTests.AssignParty_DuplicatePartyAndRole_IsRejected. |
| Codebtor Terminology | Role enum has no Garante value | COVERED | PartyRole enum inspected directly (Lessor/Tenant/Codebtor only) + SchemaConstraintTests.InvalidRoleCheckConstraint_Rejected proves the DB CHECK rejects the Garante value -- passed against real Postgres. |
| No Draft State | New contract recorded directly as Active | COVERED | ContractLifecycleTests.Constructor_AlwaysCreatesActiveContract. |
| No Draft State | Future start date still Active | UNTESTED | No test constructs a contract with a future StartDate and asserts Active. Low risk -- the constructor has no date-based branching that could special-case a future start -- but the scenario as written is not exercised. |
| Past Nominal End Date Is Not Ended | Contract past nominal end date without termination | COVERED (partial) | ContractLifecycleTests.PastNominalEndDate_WithNoNoticeGiven_RemainsActive proves the state stays Active. The scenario second clause (reported as the governing contract of its unit) is a query-layer concern out of scope for this change (no repository exists). |
| Past Nominal End Date Is Not Ended | Termination initiated after nominal end date | COVERED (generalized) | ContractLifecycleTests.GiveNotice_TransitionsToPendingTermination proves the Active to PendingTermination transition mechanically; it does not specifically combine this with a past NominalEndDate, but GiveNotice has no such branch, so the mechanism is the same either way. |
| Lifecycle Dates | Contract created with only start and nominal end dates | COVERED (partial) | PastNominalEndDate_WithNoNoticeGiven_RemainsActive asserts NoticeGivenDate/ActualEndDate are null; PlannedMoveOutDate null is not separately asserted anywhere (minor gap). |
| Ended Requires a Reason | Ending without a reason rejected | COVERED | ContractLifecycleTests.End_WithoutReason_IsRejected. |
| Multi-Unit Coverage With a Single Stored Canon | Single-unit contract is implicitly 100% | COVERED (incidental) | Exercised repeatedly as setup in SchemaConstraintTests (e.g. DuplicateContractUnitCompositeKey_Rejected calls SetUnitShares with a single 100 percent share), but there is no dedicated test asserting this scenario by name. |
| Multi-Unit Coverage With a Single Stored Canon | Multi-unit contract splits shares | COVERED | RentSplitTests + SchemaConstraintTests.DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow. |
| Shares Must Sum to Exactly 100% | Valid split accepted | COVERED | SchemaConstraintTests.DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow uses a 60/40 split and commits successfully against real Postgres. |
| Shares Must Sum to Exactly 100% | Invalid split rejected | COVERED | RentSplitTests.SetUnitShares_SplitNotSummingTo100_IsRejected (60/39.99, functionally the same rule as the spec 60/30 example) + SchemaConstraintTests.DeferredTrigger_RejectsCommittedSplitNotSummingTo100 at the DB level. |
| Share Stored as Percentage, Never Amount | Canon change leaves percentages untouched | COVERED | RentSplitTests.ChangeMonthlyRent_LeavesStoredSharesUntouched. |
| Deterministic Residue on Equal Split | Equal three-way split with residue | COVERED | RentSplitTests.Equal_ThreeWaySplit_ResidueGoesToLowestIdUnit -- exact worked example from design.md (33.333334/33.333333/33.333333). |
| Split Persisted, Not Re-Derived Monthly | Split reused across months | DEFERRED/OUT OF SCOPE | No billing/liquidation cycle exists in this change; nothing to re-derive from yet. |
| Split Persisted, Not Re-Derived Monthly | Adding a unit reopens the split | DEFERRED/OUT OF SCOPE | SetUnitShares always replaces the whole set (clears the unit list then re-adds), which is consistent with reopening the split for edit, but no test exercises this specific add-a-unit-to-an-existing-split path. |
| Honorarios Percentage Is Stored, Nullable | Honorarios unassigned | UNTESTED | No test asserts a contract with a null honorariosPercentage stores/retrieves null. The constructor default parameter makes this trivially true, but it is not exercised or asserted anywhere. |
| Honorarios Percentage Is Stored, Nullable | Honorarios rate recorded | UNTESTED | No test constructs a contract with an 8 percent honorarios rate and asserts the stored value. No DB-level test exercises ck_contracts_honorarios_percentage_range the way InvalidRoleCheckConstraint_Rejected/InvalidStatusCheckConstraint_Rejected/InvalidDocumentKindCheckConstraint_Rejected exercise their respective CHECKs. This is the one requirement in lease-contract with zero coverage at any layer, despite being fully in scope and fully implemented (Contract.HonorariosPercentage, ContractConfiguration numeric(5,2) + CHECK). |

### contract-documents

| Requirement | Scenario | Status | Evidence |
|---|---|---|---|
| One-to-Many Document Storage | Original lease and later addendum coexist | COVERED | SchemaConstraintTests.OriginalAndAddendumDocuments_CoexistForOneContract -- passed against real Postgres. |
| Document Metadata | Upload records full metadata | COVERED (partial) | The same test asserts Kind/count for two documents; it does not separately assert every metadata field (storage path, filename, content type, uploaded-at, uploader) round-trips correctly on read, though the constructor requires all of them non-null and the configuration maps them all explicitly. |
| Uploader as Plain Identifier | Upload records a string uploader | COVERED (implicit) | ContractDocument constructor takes uploadedBy as string; ContractDocumentConfiguration maps uploaded_by as plain text, no FK. No dedicated assertion that it is not an FK, but there is no FK in the schema (checked directly in the migration). |
| No Content Processing | Uploading a PDF stores pointer only | STRUCTURAL | No parsing/OCR/generation code exists anywhere in the codebase for ContractDocument; nothing to test against. |

---

## Known Deviations -- Confirmation

1. OwnerMandate dropped, honorarios moved to Contract. Confirmed consistent. exploration.md Section 4 is superseded per the proposal; lease-contract/spec.md Honorarios Percentage Is Stored, Nullable requirement and Contract.HonorariosPercentage (nullable decimal, validated 0-100 in the constructor, mapped numeric(5,2) with a matching DB CHECK) agree with each other and with design.md. No OwnerMandate type exists anywhere in src/. However, see the Honorarios test-coverage gap flagged above -- the decision itself is sound, but nothing proves the implementation behaves as decided.

2. Contract.SetUnitShares rejects the same unit twice -- not in spec text. Confirmed present and confirmed absent from lease-contract/spec.md (no scenario mentions a repeated unit id). The code (Contract.cs, the duplicate-unit check preceding the sum check) and a dedicated test (RentSplitTests.SetUnitShares_SameUnitTwice_IsRejected) both exist and the test passed. Recommend the spec be amended at archive time to add a scenario under Shares Must Sum to Exactly 100% (or a new requirement) for the same unit appearing twice in a split, since this is real enforced behavior with no textual basis today.

3. EF-only constructors on Unit/PropertyUnit/ParkingUnit. Confirmed. The public constructors are unchanged (PropertyUnit(Guid id, Address address), ParkingUnit(Guid id, Address address) -- both still require Address, both still public). The added constructors are a protected Unit(Guid id, UnitType unitType) and matching private one-parameter constructors on the two subclasses -- neither is reachable from application code, and neither references any EF/Npgsql type (the constructor bodies are plain scalar assignment; Address is left null for EF to set via its existing private setter afterward). The Domain assembly still references no EF/Npgsql assembly -- reconfirmed by ArchitectureGuardTests.DomainAssembly_ReferencesNoInfrastructureOrUiAssembly, which passed.

4. Contract has no Documents navigation -- judged. The contract-documents spec RFC-2119 language (a Contract MUST support multiple ContractDocument records, uploading a new document MUST NOT overwrite) describes persistence behavior, not an object-model shape. It does not require a Contract.Documents collection property. ContractDocumentConfiguration maps a real FK (ContractDocument.ContractId to Contract.Id, cascade delete) with HasOne of Contract, WithMany (no navigation on either side), and SchemaConstraintTests.OriginalAndAddendumDocuments_CoexistForOneContract proves two documents for one contract both persist and are both independently queryable via a direct ContractDocuments query filtered by ContractId. Judgment: the spec is satisfied. The lack of a navigation property is a deliberate, documented modeling choice (design.md: ContractDocument rows are queried separately), not a gap -- nothing in the spec text requires in-memory graph loading through Contract.

5. Zero-contract_units contracts -- domain does NOT actually forbid this. This is a real finding, not a confirmation. design.md Decision 3 and the migration own code comment both state that the domain forbids it because a Contract cannot be constructed without at least one unit. This is false as implemented. The Contract constructor takes no unit/share parameter at all and has no validation requiring the internal unit list to be non-empty. SetUnitShares does reject an empty collection if called (RentSplitInvariantException when the shares count is zero), but nothing requires a caller to ever call SetUnitShares. A Contract constructed and persisted without ever calling SetUnitShares is a fully valid domain object with zero ContractUnit rows -- exactly the state design.md says the domain prevents. No test in the suite constructs this case (there is no test asserting a zero-unit contract is rejected, because no such rejection exists to test), so this went unnoticed until this verification. This is flagged as CRITICAL below.

---

## Also Checked

- garante occurrences: 7 files match case-insensitively (SchemaConstraintTests.cs, apply-progress.md, tasks.md, PartyRole.cs, lease-contract/spec.md, proposal.md, exploration.md). All seven are the deliberate ones -- a doc comment documenting the exclusion, the RFC-2119 requirement that it must not appear, historical narrative, the test that asserts the DB rejects it as a role value, and this verification own task brief. No enum member, column, or identifier is named or derived from it. Confirmed clean.
- CI: the build job id and its name field are both still literally "build" (.github/workflows/ci.yml lines 16-17), on windows-latest, unchanged. A core job exists (ubuntu-latest, builds and tests Inmobiliaria.Core.slnf). Confirmed.
- Secrets: grepped src/ for ConnectionStrings, Host=, Password=, postgresql://. The only match is DesignTimeDbContextFactory.cs explicit offline placeholder (localhost, placeholder username and password), which is intentional per design.md Decision 6. src/Inmobiliaria.Desktop/appsettings.json contains only a Logging section -- no ConnectionStrings. Confirmed no secret is committed.
- Availability derivation: confirmed no stored column exists on units (checked the migration CreateTable call for units directly -- columns are id plus seven address fields plus unit_type, nothing else). See the unit-registry matrix above for the corresponding behavioral gap.
- Manual/human-follow-up tasks (3.14, 3.15): both remain unchecked in tasks.md, which is the correct literal state of that file -- this agent did not check them. Per the task brief, both are reported complete in reality: the migration was applied to the real Supabase project (six tables plus the EF migrations history table plus the trigger, confirmed by the user, not re-verified here since this agent must not connect to Supabase), and the core job was added to the GitHub ruleset. Recommend tasks.md and apply-progress.md be updated to reflect this at archive time, or that the archive phase record the real-world completion explicitly, since the files as they stand under-report completion.
- Review budget: apply-progress.md records Slice 1 (PR 1) at roughly 1019 added and 17 removed lines against an estimated 400-520-line budget -- explicitly flagged there as over budget with an exception requested. This verification does not have visibility into whether that exception was explicitly granted by the user; flagged here for completeness, not re-litigated.

---

## Issues

### CRITICAL

1. Zero-unit Contract is constructible and persistable, contradicting design.md Decision 3 explicit claim that the domain forbids it. The Contract constructor has no unit/share parameter and no validation requiring at least one ContractUnit; SetUnitShares empty-collection rejection only fires if a caller invokes it, which is optional. No test covers, or could cover given the current code, the "domain forbids it" claim. Recommend either (a) adding constructor/invariant enforcement that a Contract cannot be considered valid or persistable without at least one unit share (for example a factory method, or validating before save), or (b) correcting design.md and the migration comment to say the domain does not structurally forbid this today and re-scoping the accepted gap accordingly.
2. Honorarios Percentage Is Stored, Nullable has zero test coverage at any layer. No domain test asserts null honorarios or an 8 percent honorarios contract stores and round-trips correctly; no infrastructure test exercises the honorarios-range CHECK the way the other three CHECK constraints (role, status, document kind) are each exercised by a dedicated rejection test. This is fully in-scope, already-implemented code with no verification at all.

### WARNING

1. The unit-registry Availability Is Derived, Never Stored requirement has zero implementing code, not merely zero tests -- there is no availability-computation logic anywhere in src/. This is consistent with design.md explicit scope guard (repositories/use cases are out of scope for this change), but the spec as written asserts system behavior that literally does not exist yet. Recommend the spec be annotated at archive time to note this behavior ships with the first use-case/repository change, not this one -- otherwise it will keep reading as an unmet requirement indefinitely.
2. The party-registry Single Party Identity Per Person scenarios are similarly unimplemented (no create/reuse service exists), for the same design-scope reason. Same recommendation.
3. Future start date still Active (lease-contract) is untested. Low risk given the constructor has no date-branching logic that could special-case it, but the scenario as written is not exercised by any test.
4. Contract.SetUnitShares same-unit-twice rejection is real, tested code with no textual basis in the spec (Known Deviation 2). Recommend a spec amendment at archive time.
5. PR 1 authored line count (roughly 1019/17) significantly exceeded its 400-520 estimate, flagged as over budget with an exception requested in apply-progress.md. This verification cannot confirm whether that exception was explicitly granted.
6. tasks.md under-reports real completion: tasks 3.14 (Supabase migration apply) and 3.15 (GitHub ruleset core addition) are both still unchecked, though the task brief states both were completed manually by the user after this agent slices were merged.

### SUGGESTION

1. Add a dedicated test for the single-unit contract is implicitly 100 percent scenario -- currently only proven incidentally as setup inside other tests.
2. Add a dedicated test asserting PlannedMoveOutDate is null on a freshly-constructed contract (currently only NoticeGivenDate and ActualEndDate are asserted null).
3. Add a dedicated InvalidHonorariosPercentageCheckConstraint_Rejected test to SchemaConstraintTests.cs, mirroring the pattern already used for role, status and document kind, to close the DB-level half of the Honorarios gap.
4. Consider a dedicated assertion in the ContractDocumentConfiguration coverage that every metadata field (not just Kind) round-trips correctly.

---

## Result Contract

- status: partial
- executive_summary: PASS WITH WARNINGS -- build and all 28 tests (16 domain + 12 real Testcontainers integration, 0 skipped) passed, but verification surfaced 2 CRITICAL findings (a genuinely unenforced zero-unit-contract invariant contradicting design.md, and a fully-untested Honorarios requirement) and 6 WARNING findings, alongside 4 SUGGESTIONs.
- artifacts: openspec/changes/contract-and-parties/verify-report.md; Engram sdd/contract-and-parties/verify-report
- next_recommended: sdd-apply (to close the 2 CRITICAL findings, specifically the zero-unit-contract invariant and Honorarios test coverage, before archive)
- risks: (1) A Contract with zero units can be constructed and persisted today despite design.md explicit claim to the contrary -- this is the more serious of the two CRITICAL findings since it is a real, silent domain-invariant gap, not just a documentation mismatch. (2) Honorarios storage and retrieval and its DB CHECK are completely unverified in either direction (both null and a concrete percentage).
- skill_resolution: none -- no "Skills to load before work" block or skill registry was provided in this launch; proceeded with sdd-verify own SKILL.md and the shared phase-common protocol only.
