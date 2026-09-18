# Tasks: Contract and Parties

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1230–1620 (design per-slice sum; proposal range 1110–1720) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Domain+CI) → PR 2 (Infrastructure) → PR 3 (Migration+Tests) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Domain entities, invariants, WPF shell, two-job CI live | PR 1 | `dotnet test tests/Inmobiliaria.Domain.Tests` | `core` job on ubuntu-latest (architecture guard) | Revert PR; no DB touched |
| 2 | DbContext + 6 EF configurations, secrets wiring | PR 2 | `dotnet build src/Inmobiliaria.Infrastructure` | N/A — no migration yet, no runtime DB call | Revert PR; no migration exists |
| 3 | Migration + trigger SQL + Testcontainers tests | PR 3 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests` | Testcontainers `postgres:<major>-alpine` (pinned per task P.2) | `dotnet ef database update 0` |

## Prerequisites (Non-Code — before PR 3 apply)

- [ ] P.1 Create the dedicated Supabase organization and provision a project inside it (not shared with the padel system). *(proposal: supabase-org, resolved)*
- [ ] P.2 Record the Postgres major version that project reports; needed to pin the Testcontainers image (task 3.3). Do not guess.

## Slice 1 (PR 1) — Solution Scaffolding, Domain Layer, CI Split — est. 400–520 lines

**Actual size: ~1019 added / 17 removed lines (see apply-progress) — see Risks in apply-progress for why this exceeded the estimate and the exception requested.**

- [x] 1.1 Create `Inmobiliaria.sln` at repo root (CI glob target) and `Inmobiliaria.Core.slnf` (non-Windows filter).
- [x] 1.2 Create `Directory.Build.props` (Nullable, TreatWarningsAsErrors) and `Directory.Packages.props` (central package versions).
- [x] 1.3 [P] Scaffold `Inmobiliaria.Domain` (net10.0, references nothing) with folders `Parties/ Units/ Leasing/ Shared/`.
- [x] 1.4 [P] `Party.cs` + `PartyRole.cs` (Lessor/Tenant/Codebtor only — no Garante). *(party-registry: Role-Agnostic Storage; lease-contract: Codebtor Terminology)*
- [x] 1.5 [P] `Address.cs` owned value object under `Shared/`. *(unit-registry: physical identity)*
- [x] 1.6 [P] `Unit.cs` (abstract, TPH) + `PropertyUnit.cs` + `ParkingUnit.cs` + `UnitType.cs`, no availability field. *(unit-registry: Unit Is Physical Asset Only, UnitType Classification)*
- [x] 1.7 `Contract.cs` aggregate root: `StartDate`, `NominalEndDate`, `NoticeGivenDate`, `PlannedMoveOutDate`, `ActualEndDate`, canon, honorarios (nullable), `ContractStatus.cs`, `EndReason.cs`. *(lease-contract: Lifecycle Dates, Honorarios Percentage)*
- [x] 1.8 Constructor forces `Active` state, no Draft path. *(lease-contract: No Draft State)*
- [x] 1.9 `Contract.GiveNotice(...)` → `PendingTermination`; `Contract.End(reason)` throws without a reason. *(lease-contract: Past Nominal End Date, Ended Requires a Reason)*
- [x] 1.10 `ContractParty.cs` (join, key = contract+party+role) + `Contract.AssignParty(party, role)` enforcing 1..N Lessor, exactly 1 Tenant, 0..N Codebtor, and reject duplicate Party+Role. *(lease-contract: Party Role Multiplicity, Role-Scoped Uniqueness)*
- [x] 1.11 `ContractUnit.cs` (join, key = contract+unit) + `UnitShare.cs` value object.
- [x] 1.12 `RentSplit.cs` static class: `Equal(unitIds)` — truncate to 6dp, residue to largest share, ties by lowest `Guid`, `decimal` only. *(lease-contract: Deterministic Residue on Equal Split)*
- [x] 1.13 `Contract.SetUnitShares(shares)` + `RentSplitInvariantException`: throws unless sum == 100m exactly and every share > 0. *(lease-contract: Shares Must Sum to Exactly 100%, Share Stored as Percentage)*
- [x] 1.14 `ContractDocument.cs` + `DocumentKind.cs` (Original/Addendum/TerminationNotice), `UploadedBy` as plain string. *(contract-documents: One-to-Many, Document Metadata, Uploader as Plain Identifier)*
- [x] 1.15 Scaffold `Inmobiliaria.Desktop` (WPF, `App.xaml`, `MainWindow`, csproj) — shell only, gives the Windows `build` job real work.
- [x] 1.16 `.github/workflows/ci.yml`: split into `build` (windows-latest, full `.sln`, **job id and `name:` stay literally `build`**) and new `core` (ubuntu-latest, `Inmobiliaria.Core.slnf`); delete the `.sln`-glob skip conditional entirely. **Do not rename/split `build` — the GitHub ruleset matches that literal name.**
- [x] 1.17 [P] `tests/Inmobiliaria.Domain.Tests/RentSplitTests.cs`: sum==100 exactly, N=3 residue to lowest-id unit, 99.99 split rejected, canon change leaves shares untouched.
- [x] 1.18 [P] `ArchitectureGuardTests.cs`: `Contract`'s assembly references no `Microsoft.EntityFrameworkCore*`, `Npgsql*`, `PresentationFramework/Core`, `WindowsBase` — the mechanical half of the Domain→WPF/EF boundary the Linux `core` job alone cannot fully catch.
- [x] 1.19 [P] `ContractLifecycleTests.cs`: no-Draft creation, past-`NominalEndDate` stays `Active`, notice → `PendingTermination`, `End` without reason rejected.
- [x] 1.20 [P] `ContractPartyAssignmentTests.cs`: two lessors accepted, second tenant rejected, 0/2 codebtors accepted, same party as lessor+codebtor accepted, duplicate party+role rejected.
- [ ] 1.21 Verify: both `build` and `core` CI jobs green on the PR. **Locally equivalent commands passed (`dotnet build Inmobiliaria.sln -c Release`, `dotnet build/test Inmobiliaria.Core.slnf -c Release`); actual GitHub Actions run still pending — requires the PR to exist.**

## Slice 2 (PR 2) — Infrastructure: DbContext & EF Mappings — est. 380–450 lines

**Actual size: ~327 added lines (see apply-progress) — under the 380–450 estimate; no exception needed.**

- [x] 2.1 Scaffold `Inmobiliaria.Infrastructure` (net10.0 → Domain); add EF Core, Npgsql, `EFCore.NamingConventions` via `Directory.Packages.props`.
- [x] 2.2 `InmobiliariaDbContext.cs`: six `DbSet`s (`Parties, Units, Contracts, ContractParties, ContractUnits, ContractDocuments`), `.UseSnakeCaseNamingConvention()`.
- [x] 2.3 [P] `PartyConfiguration.cs`: unique index on DNI (nullable-safe), unique index on CUIL. *(party-registry: Natural Key Uniqueness)*
- [x] 2.4 [P] `UnitConfiguration.cs`: `HasDiscriminator<string>("unit_type")` mapping Property/Parking; `.OwnsOne(Address)` flattened columns.
- [x] 2.5 [P] `ContractConfiguration.cs`: `monthly_rent numeric(14,2)`, `honorarios_percentage numeric(5,2)` nullable CHECK 0–100, status enum as `text` + CHECK via `.HasConversion<string>()`.
- [x] 2.6 [P] `ContractPartyConfiguration.cs`: composite PK `(contract_id, party_id, role)`, role enum as `text` + CHECK (no Garante value).
- [x] 2.7 [P] `ContractUnitConfiguration.cs`: composite PK `(contract_id, unit_id)`, `share_percentage numeric(9,6)` CHECK `>0 AND <=100`.
- [x] 2.8 [P] `ContractDocumentConfiguration.cs`: `DocumentKind` as `text` + CHECK, `uploaded_by` plain `text` column (no FK).
- [x] 2.9 `DesignTimeDbContextFactory.cs`: reads `INMOBILIARIA_DB` env var, falls back to an offline placeholder connection string.
- [x] 2.10 Wire `dotnet user-secrets` on `Inmobiliaria.Desktop` (`UserSecretsId`, key `ConnectionStrings:SupabasePostgres`); commit `appsettings.json` with **no** `ConnectionStrings` section. **No connection string in any committed file.**
- [x] 2.11 Verify: `Inmobiliaria.Infrastructure` builds green on both CI jobs; no migration exists yet. **Local-equivalent verification only — see Work Unit Evidence in apply-progress; actual GitHub Actions run pending, no PR opened by this agent.**

## Slice 3 (PR 3) — Initial Migration, Trigger, Testcontainers, `core` Ruleset — est. 450–650 lines

**Actual size: see apply-progress for the authored-line count and Work Unit Evidence.**

- [x] 3.1 Generate migration: `dotnet ef migrations add InitialSchema -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`. **Deviation**: generated to `Persistence/Migrations/` via `-o Persistence/Migrations` to match design.md's file table (default `-o` would have been project-root `Migrations/`).
- [x] 3.2 Hand-append to `Up`: `CREATE CONSTRAINT TRIGGER contract_units_share_sum ... DEFERRABLE INITIALLY DEFERRED` + `assert_contract_share_sum()` function; matching `DROP TRIGGER`/`DROP FUNCTION` in `Down`. **Stays `DEFERRABLE INITIALLY DEFERRED`** — verified by test 3.5's positive-control fact, which inserts two contract_units rows one at a time inside one SaveChanges transaction and asserts it commits.
- [x] 3.3 Confirmed: Testcontainers image pinned to `postgres:17.6` (exact version, not `-alpine`/floating), matching the Postgres major/minor the provisioned Supabase project reports per `openspec/config.yaml` `resolved_decisions: postgres-version`.
- [x] 3.4 `PostgresFixture.cs`: Testcontainers fixture calling `context.Database.MigrateAsync()` — never `EnsureCreated()`. Skips (does not fail) with an explicit `SkipReason` when Docker is unreachable locally; both `PostgreSqlBuilder.Build()` and `.StartAsync()` are guarded, since `Build()` itself probes the Docker endpoint and throws first when the daemon is down.
- [x] 3.5 `SchemaConstraintTests.cs` — `DeferredTrigger_RejectsCommittedSplitNotSummingTo100` (rejection) **and** `DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow` (positive control proving the deferred trigger doesn't fail EF's row-by-row inserts for a valid split).
- [x] 3.6 `SchemaConstraintTests.cs` — `DuplicateContractPartyCompositeKey_Rejected` and `DuplicateContractUnitCompositeKey_Rejected`.
- [x] 3.7 `SchemaConstraintTests.cs` — `InvalidRoleCheckConstraint_Rejected`, `InvalidStatusCheckConstraint_Rejected`, `InvalidDocumentKindCheckConstraint_Rejected`.
- [x] 3.8 `SchemaConstraintTests.cs` — `TphRoundTrip_PreservesConcreteUnitType`.
- [x] 3.9 `SchemaConstraintTests.cs` — `TwoSequentialContractsOnSameUnit_BothRemainReadable`. *(unit-registry: Availability Is Derived, Never Stored)*
- [x] 3.10 `SchemaConstraintTests.cs` — `OriginalAndAddendumDocuments_CoexistForOneContract`. *(contract-documents: One-to-Many)*
- [x] 3.11 `SchemaConstraintTests.cs` — `DuplicateDni_RejectedByUniqueIndex`, `DuplicateCuil_RejectedByUniqueIndex`. *(party-registry: Natural Key Uniqueness)*
- [x] 3.12 `.github/workflows/ci.yml`: no `core` job edit was needed beyond adding the new test project to `Inmobiliaria.Core.slnf` — the existing `dotnet test Inmobiliaria.Core.slnf` step already picks it up (Docker present on ubuntu-latest → Testcontainers tests execute for real there).
- [x] 3.13 Updated `openspec/config.yaml`: `testing.status: available`, `apply.test_command`/`verify.test_command`/`verify.build_command` filled, `ef_testing_decision` records the Testcontainers-vs-Supabase-schema decision.
- [ ] 3.14 **NOT DONE — explicitly out of scope for this agent.** The user's instructions for this PR forbid running `dotnet ef database update` or connecting to Supabase in this session ("do not touch the live database"). This manual smoke check (apply the migration to the real Supabase project, one insert/read via Npgsql) is a required **human follow-up after this PR merges**, using the developer's own connection string, which this agent must never see or request.
- [ ] 3.15 **MANUAL, non-code, admin-only, AFTER `core` has run at least once on a PR:** add `core` as a required status check in the GitHub ruleset. Until this is done, a red `core` job is advisory only and does not block merge.
- [x] 3.16 Repo-wide check: the only occurrences of "garante" (case-insensitive) are the deliberate ones that document/prove its exclusion — `PartyRole.cs`'s doc comment, `spec.md`'s RFC-2119 requirement that it must not appear, historical `proposal.md`/`exploration.md` narrative predating this slice, and `SchemaConstraintTests.InvalidRoleCheckConstraint_Rejected`, which asserts the DB CHECK rejects `'Garante'` as a role value. No enum member, column value, or identifier named/derived from it exists anywhere.
- [x] 3.17 Verified: `build` job id/`name:` still literally `build` (`.github/workflows/ci.yml` lines 16-17) after the Slice 1 and Slice 3 CI touches.

## Key Learnings

1. Party role multiplicity and lifecycle-state rules are domain invariants on `Contract`, mirroring the `SetUnitShares` pattern, even though the design's file table only enumerated the rent-split and architecture-guard test files explicitly.
2. Availability-derivation and DNI/CUIL uniqueness have no repository layer in this change, so their spec scenarios are validated structurally (no stored column) and at the database unique-index/integration level, not through a query service.
3. The `core` CI job cannot block merges until a repository admin manually adds it to the GitHub ruleset, and that can only happen after the job has run at least once — this is a hard ordering dependency, not a preference.
