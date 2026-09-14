# Design: Contract and Parties

Domain vocabulary (Argentine lease terms, glossed once): **locador** = lessor/owner,
**locatario** = tenant, **codeudor** = solidary co-debtor, **honorarios** = the agency's
management fee, **canon** = the monthly rent total. Identifiers and code are English.

## Technical Approach

This change turns the proposal's six entities into a Clean/Hexagonal layout whose core has no
infrastructure dependency, an EF Core mapping onto the six target Postgres tables, and the first
migration against a dedicated Supabase project. No use cases, repositories or UI ship here: the
deliverable is a compiling, tested, migrated persistence foundation.

Scope guard: the `DbContext` plus its six `IEntityTypeConfiguration<T>` classes **are** the whole
infrastructure surface of this change. Repository ports and adapters arrive with the first
use-case change, not here — `sdd-tasks` must not invent them.

## Architecture Decisions

### Decision 1 — Project and solution structure

**Choice**: five projects, feature-named folders (Screaming), one forbidden dependency edge.

```
Inmobiliaria.sln                 (repo ROOT — the CI .sln glob only looks here)
Inmobiliaria.Core.slnf           (solution filter: everything except Desktop)
Directory.Build.props            (LangVersion, Nullable, TreatWarningsAsErrors)
Directory.Packages.props         (central package management)
src/Inmobiliaria.Domain/         net10.0          → references NOTHING
      Parties/  Units/  Leasing/  Shared/
src/Inmobiliaria.Infrastructure/ net10.0          → Domain
      Persistence/  Persistence/Configurations/  Persistence/Migrations/
src/Inmobiliaria.Desktop/        net10.0-windows  → Infrastructure, Domain (WPF shell only)
tests/Inmobiliaria.Domain.Tests/         net10.0  → Domain
tests/Inmobiliaria.Infrastructure.Tests/ net10.0  → Infrastructure
```

**Alternatives considered**: adding an `Application` project now (rejected — this change has zero
use cases, so it would ship empty; revisit trigger: the first use case); folder-per-technical-type
inside Domain (rejected — `Entities/`, `Enums/` hide the domain, `Parties/`, `Leasing/` announce it).

**Rationale, and how the boundary is *enforced* rather than intended** — two mechanisms, because
neither alone is sufficient:

| Forbidden reference | Enforcement | Why it actually fires |
|---|---|---|
| Domain → WPF | The Linux CI job (`core`) builds `Inmobiliaria.Core.slnf`. A WPF reference forces `net10.0-windows` + `UseWPF`, which fails on Linux with NETSDK1100. | Mechanical, unskippable, no test to forget. |
| Domain → EF Core / Npgsql | `Inmobiliaria.Domain.Tests/ArchitectureGuardTests.cs`: assert `typeof(Contract).Assembly.GetReferencedAssemblies()` contains no name starting with `Microsoft.EntityFrameworkCore`, `Npgsql`, `PresentationFramework`, `PresentationCore`, `WindowsBase`. | EF Core builds fine on Linux, so the CI job above would **not** catch it. Caveat: `GetReferencedAssemblies()` reports assemblies actually used in IL, so it catches *use*, not a dangling unused `PackageReference` — which is the failure that matters. |

### Decision 2 — EF Core mapping

| Concern | Choice | Rationale / rejected |
|---|---|---|
| `ContractParty`, `ContractUnit` | Explicit join entities. `contract_parties` PK `(contract_id, party_id, role)`; `contract_units` PK `(contract_id, unit_id)`. | Both carry payload, so skip navigations are impossible. Including `role` in the PK keeps "same person in two roles on one contract" a *validation* question (proposal open question 3, unanswered) instead of baking an unevidenced schema rule. |
| `Unit` TPH | `abstract Unit` with `PropertyUnit` / `ParkingUnit`, both currently field-free. `HasDiscriminator<string>("unit_type").HasValue<PropertyUnit>("property").HasValue<ParkingUnit>("parking")`. | The empty subclasses are the point: the collection change adds per-utility account numbers to `PropertyUnit` only. String discriminator is readable in Supabase Studio. |
| `Address` | `.OwnsOne(u => u.Address)` flattened into `units` as `address_street`, `address_number`, `address_floor`, `address_apartment`, `address_city`, `address_province`, `address_postal_code`; required. | No identity of its own, never queried independently. A separate table would be a join for zero benefit. |
| **Enums** | **Plain `text` column** via `.HasConversion<string>()` + `HasMaxLength` + a DB `CHECK (col IN (...))`. | Native Postgres enums rejected: `ALTER TYPE ... ADD VALUE` fights EF's transactional migrations, and Npgsql needs global type mapping registered before the data source is built — a mismatch is a *runtime* failure. `int` rejected: opaque in Supabase Studio (the team will inspect data there) and silently corrupts on member reorder. `text` costs width; the `CHECK` buys back the DB-level validation `int`/`text` would otherwise lose. |
| **Naming** | PascalCase in C#, snake_case in Postgres, via `EFCore.NamingConventions` `.UseSnakeCaseNamingConvention()` (MIT, one line). | Postgres folds unquoted identifiers to lowercase, so PascalCase tables force `"Contracts"` double-quoting in every hand-written query and in Studio. DbSet names `Parties`/`Units`/`Contracts`/`ContractParties`/`ContractUnits`/`ContractDocuments` land exactly on the proposal's six target table names. |
| **Money** | `contracts.monthly_rent numeric(14,2)`, C# `decimal`. | 2 dp = ARS centavos; 12 integer digits is deliberate headroom against Argentine inflation, and widening `numeric` later is a rewriting migration. |
| **Percentages** | `contract_units.share_percentage numeric(9,6)` (CHECK `> 0 AND <= 100`); `contracts.honorarios_percentage numeric(5,2)` nullable (CHECK `>= 0 AND <= 100`). | 6 dp on the share because it multiplies the canon: at 4 dp the smallest representable step is ~1 peso on a 1,000,000 canon; at 6 dp it is ~0.01. Honorarios is a typed human rate (`8.00`), so 2 dp is honest. Nullable per the "Sin Asignar" assumption. |
| **Keys** | `Guid` (`uuid`) generated client-side as UUIDv7 (`Guid.CreateVersion7()`, .NET 9+). | Sequential, so index locality is preserved. Critically, no DB round-trip for identity means `Contract` + its `ContractUnit` rows can be built as one fully-valid in-memory graph *before* `SaveChanges` — which is what makes Decision 3 enforceable in the domain. Also gives the rounding rule a stable, time-ordered tie-break. |

### Decision 3 — The rent-split invariant

**Choice**: enforced in **both** places, with different jobs.

- **Domain (primary)**: `Contract` is the aggregate root and owns a private `List<ContractUnit>`
  exposed as `IReadOnlyCollection`. The only mutator is
  `Contract.SetUnitShares(IReadOnlyCollection<UnitShare> shares)`, which throws
  `RentSplitInvariantException` unless `shares.Sum(s => s.Percentage) == 100m` exactly and every
  share is `> 0`. `ContractUnit` has no public setter. Unit-testable with zero infrastructure.
- **Database (backstop)**: a per-row `CHECK` cannot express a cross-row sum, so the migration
  hand-writes a `CONSTRAINT TRIGGER ... DEFERRABLE INITIALLY DEFERRED` on `contract_units` that
  raises unless `SUM(share_percentage) = 100` for the affected `contract_id` at commit.
  **Deferred is mandatory** — EF inserts the join rows one at a time, so an immediate trigger
  fails on the first row of every valid two-unit contract.
- **Rejected**: domain-only (a hand-written SQL fix or a second client would bypass it);
  DB-only (pushes a business rule into a trigger the team cannot unit-test).
- **Residual gap, accepted**: a contract with *zero* `contract_units` never fires a row trigger, so
  the DB permits it. The domain forbids it (a `Contract` cannot be constructed without at least one
  unit). A statement-level guard on `contracts` would be over-engineering for two internal users.

**Deterministic rounding for an equal split of N units** — `decimal` throughout, never `double`:

1. `base = Math.Floor(100m / n * 1_000_000m) / 1_000_000m` (truncate to 6 dp, matching the column).
2. Assign `base` to every unit.
3. `residue = 100m - base * n` (always `>= 0`, at most `n * 0.000001`).
4. Add the whole residue to the unit with the **largest** share; ties broken by the **smallest**
   `Unit.Id` under `Guid.CompareTo`. In an equal split all shares tie, so it is always the
   lowest-id unit — deterministic, and stable because UUIDv7 is time-ordered.

Worked case, N=3: `base = 33.333333`, residue `0.000001`, result `33.333334 / 33.333333 /
33.333333` = exactly `100`. A canon change never touches these numbers (Decision 4 of the proposal).

### Decision 4 — How EF Core is tested against Postgres

**Choice**: **Testcontainers for .NET**, image `postgres:<major>-alpine` pinned to the major version
the Supabase project actually reports (verify at apply time, do not assume). The EF InMemory
provider is rejected upstream by the proposal and is not revisited.

| Criterion | Testcontainers *(chosen)* | Dedicated Supabase test schema |
|---|---|---|
| Isolation | Fresh database per test run | Shared: two developers running tests concurrently corrupt each other's rows |
| CI secrets | **None** — no connection string exists in CI at all | Requires a live cloud credential in GitHub Secrets; a leak is a real database |
| Speed | ~2–4 s container start, amortised over one xUnit collection | Network round-trip per query from the runner |
| Fidelity | Same engine, same constraints and triggers | Exact production surface, including pooler/TLS/RLS |
| Cost | Free; needs Docker locally | Consumes the free-tier project's quota |

**Rationale**: the whole reason InMemory was rejected is that constraints must be real — and a
container enforces the deferred trigger, the CHECKs and the composite keys identically. The
Supabase option's only genuine advantage is pooler/TLS/RLS fidelity, which this change does not
exercise, and it buys that advantage by putting a live production-grade credential into CI. That
trade is not worth it for two internal users.

Two implementation details that decide whether this works at all:

- The fixture must call `context.Database.Migrate()`, **never** `EnsureCreated()`.
  `EnsureCreated` builds the schema from the model and skips migrations entirely — the
  hand-written trigger SQL from Decision 3 would silently not exist, and the invariant test would
  pass against a database that has no invariant.
- Docker Desktop is friction on the office Windows PC. The collection fixture skips with an
  explicit message when Docker is unreachable locally; the Linux CI job always runs them, so the
  gate cannot be bypassed on the way to `main`.

**Compensating check** (belongs to the migration task, not to CI): after applying the migration to
the real Supabase project, run one manual smoke insert/read through Npgsql over the real
connection. That is what covers pooler and TLS.

### Decision 5 — CI structure

**Choice**: split into two jobs; keep the existing one byte-identical in name.

| Job | Runner | Builds | Required? |
|---|---|---|---|
| `build` | `windows-latest` | Full `Inmobiliaria.sln`, including WPF | **Yes — unchanged** |
| `core` (new) | `ubuntu-latest` | `Inmobiliaria.Core.slnf` + `dotnet test` (Docker present → Testcontainers run) | Not until an admin adds it |

> **RULESET WARNING — read before touching `ci.yml`.** The required status check is named `build`
> and is enforced by a GitHub ruleset. This design deliberately keeps both the job id and its
> `name:` as `build` so the ruleset keeps matching. **If anyone renames, splits or removes that
> job without updating the ruleset first, every PR hangs forever waiting for a check that will
> never report.** Separately: the new `core` job does **not** block merge until a repository admin
> adds `core` to the same ruleset. Until that is done, a red `core` is advisory only. That admin
> action is a required step of this change and must appear in `sdd-tasks`.

**Also fixed here**: the current `compgen -G "*.sln"` guard only globs the repository *root*, so a
solution in a subdirectory would pass the check green without building anything. Rather than
patch the glob, **delete the conditional entirely in the same PR that adds the `.sln`** — the
guard existed only for the spec-only-PR era, which ends with this change. Doc-only PRs will then
compile the solution, which is cheap and desirable.

The Linux job is not just faster: it is the mechanical half of Decision 1's enforcement.

### Decision 6 — Connection string and secrets

| Context | Mechanism |
|---|---|
| Local development | `dotnet user-secrets` on `Inmobiliaria.Desktop` (`UserSecretsId` in the csproj), key `ConnectionStrings:SupabasePostgres`. |
| `dotnet ef migrations add` | `DesignTimeDbContextFactory` in Infrastructure reads env var `INMOBILIARIA_DB`, falling back to a placeholder string so migration *generation* works offline with no credential present. |
| Integration tests | None. Testcontainers supplies its own ephemeral string. |
| **CI** | **No secret is added by this change.** Decision 4 removes the need for one. If a Supabase smoke job is added later it uses `secrets.SUPABASE_DB_CONNECTION`, gated to `push` on `main` only — fork PRs do not receive secrets, and a `pull_request`-triggered job that needs one is a leak waiting to happen. |
| Committed config | `src/Inmobiliaria.Desktop/appsettings.json` is committed **with no `ConnectionStrings` section** — non-secret settings only (Storage bucket name). Load order: `appsettings.json` → user-secrets → environment variables. |

`.gitignore` already excludes `appsettings.Development.json`, `appsettings.*.local.json` and
`secrets.json`; no `.gitignore` change is required. Nothing in this change places a credential in
a tracked file.

### Decision 7 — Migration strategy

- **Generate** from Infrastructure with Infrastructure as its own startup project:
  `dotnet ef migrations add InitialSchema -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.
  Not via the Desktop startup project, so the command runs on Linux and inside VS Code without a
  Windows-only build.
- **Hand-written part**: the deferred constraint trigger and its function are appended to the
  generated `Up` via `migrationBuilder.Sql(...)`, with matching `DROP TRIGGER` / `DROP FUNCTION` in
  `Down` so `dotnet ef database update 0` (the proposal's rollback step 1) actually reverses.
- **Review**: reviewers read `Up`/`Down` and the trigger SQL; the generated `.Designer.cs` and
  `ModelSnapshot.cs` are diff noise, not authored risk. Produce
  `dotnet ef migrations script --idempotent -o artifacts/initial-schema.sql` locally for eyeball
  review — `artifacts/` is already gitignored, so it is not committed.
- **Apply**: **one** developer runs `dotnet ef database update` manually against the shared Supabase
  project. **The WPF app never calls `Database.Migrate()` on startup** — two desktop clients racing
  to migrate a shared production database is exactly the failure this rule prevents.
- **Second developer**: `git pull`, then `dotnet ef database update` — a no-op, because
  `__EFMigrationsHistory` in the shared project already records it. That table is the coordination
  point; there are no per-developer databases (local schemas exist only inside test containers).
- **Rule**: never edit an applied migration. Add a new one.

## Data Flow

```
  WPF View ──► ViewModel ──► [ port ]  ──► Repository ──► DbContext ──► Npgsql ──► Supabase PG
  (Desktop)   (Desktop)     (later)       (later)         │
                                                          └──► contract_documents row (pointer)
                                                                        ▲
                              file bytes ──► Supabase Storage bucket ───┘
```

Everything left of `DbContext` is **out of scope for this change**; it is drawn only so `sdd-tasks`
does not re-derive it. This change delivers `DbContext` → Npgsql → Supabase Postgres, plus the
`contract_documents` shape that will later hold the Storage pointer. No bytes are uploaded here.

Write path for the invariant:

```
  Contract.SetUnitShares(...)  ──throws RentSplitInvariantException if sum ≠ 100
        │
        ▼  SaveChanges  (all contract_units rows in ONE transaction)
  deferred CONSTRAINT TRIGGER re-checks SUM = 100 at COMMIT
```

## File Changes

| File | Action | Description |
|---|---|---|
| `Inmobiliaria.sln` | Create | Root solution — the CI glob depends on this location |
| `Inmobiliaria.Core.slnf` | Create | Non-Windows projects for the Linux job |
| `Directory.Build.props`, `Directory.Packages.props` | Create | Nullable, warnings-as-errors, central package versions |
| `src/Inmobiliaria.Domain/Parties/Party.cs`, `PartyRole.cs` | Create | Role-agnostic identity; DNI/CUIL unique |
| `src/Inmobiliaria.Domain/Units/Unit.cs`, `PropertyUnit.cs`, `ParkingUnit.cs`, `UnitType.cs` | Create | TPH root and the two currently field-free subclasses |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs`, `ContractParty.cs`, `ContractUnit.cs`, `ContractDocument.cs`, `ContractStatus.cs`, `EndReason.cs`, `DocumentKind.cs` | Create | Aggregate root, joins, lifecycle enums |
| `src/Inmobiliaria.Domain/Leasing/RentSplit.cs` | Create | Equal-split + deterministic residue rule |
| `src/Inmobiliaria.Domain/Shared/Address.cs` | Create | Owned value type |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Create | Six DbSets, snake_case convention |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/*.cs` (×6) | Create | One `IEntityTypeConfiguration<T>` per entity |
| `src/Inmobiliaria.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | Create | Offline-safe migration generation |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/*_InitialSchema.cs` | Create | Generated + hand-written trigger SQL |
| `src/Inmobiliaria.Desktop/` (App.xaml, MainWindow, csproj, appsettings.json) | Create | WPF shell so the Windows `build` job has real work; no credentials in appsettings |
| `tests/Inmobiliaria.Domain.Tests/RentSplitTests.cs`, `ArchitectureGuardTests.cs` | Create | Invariant + rounding; forbidden-reference guard |
| `tests/Inmobiliaria.Infrastructure.Tests/PostgresFixture.cs`, `SchemaConstraintTests.cs` | Create | Testcontainers fixture; constraints proven against real PG |
| `.github/workflows/ci.yml` | Modify | Add `core` job; delete the `.sln` skip conditional; keep `build` named `build` |
| `openspec/config.yaml` | Modify | `testing.status` → available; fill `test_command` / `build_command`; record the EF-testing decision |

## Interfaces / Contracts

```csharp
// Domain — the only way shares are ever set.
public sealed class Contract
{
    private readonly List<ContractUnit> _units = [];
    public IReadOnlyCollection<ContractUnit> Units => _units;

    public void SetUnitShares(IReadOnlyCollection<UnitShare> shares); // throws RentSplitInvariantException
}

public static class RentSplit
{
    // Equal split; residue to largest share, ties by smallest Guid. decimal only.
    public static IReadOnlyList<UnitShare> Equal(IReadOnlyList<Guid> unitIds);
}
```

```sql
-- Infrastructure migration, hand-written. DEFERRABLE is load-bearing.
CREATE CONSTRAINT TRIGGER contract_units_share_sum
  AFTER INSERT OR UPDATE OR DELETE ON contract_units
  DEFERRABLE INITIALLY DEFERRED
  FOR EACH ROW EXECUTE FUNCTION assert_contract_share_sum();
```

## Testing Strategy

| Layer | What to test | Approach |
|---|---|---|
| Unit (Domain) | Split sums to exactly 100; N=3 residue lands on the lowest-id unit; a 99.99 split is rejected; canon change leaves shares untouched | xUnit, zero infrastructure, `decimal` assertions |
| Unit (Domain) | Domain assembly references no EF Core, Npgsql or WPF assembly | `GetReferencedAssemblies()` guard test |
| Integration | Deferred trigger rejects a committed split ≠ 100; composite PKs; enum CHECKs; TPH round-trip; two sequential contracts on one unit both readable; original + addendum coexist | Testcontainers Postgres + `Database.Migrate()` |
| Build | Domain/Infrastructure compile without Windows | `core` job on `ubuntu-latest` |
| Manual | Real Supabase connectivity (pooler, TLS) | One smoke insert/read after applying the migration |

## Threat Matrix

N/A — no routing, shell command, subprocess, VCS/PR automation, executable-file classification or
process-integration boundary is introduced. The one genuine security surface (database credentials)
is handled in Decision 6, and the CI workflow change adds no secret.

## Migration / Rollout

Per Decision 7. Rollback is the proposal's plan unchanged: `dotnet ef database update 0` (the
`Down` drops the trigger and function first), delete the Storage bucket if created, `git revert`
the chained PRs in reverse order.

**Delivery slicing** — forecast 1110–1720 authored lines against an 800-line budget, targeting
400–500 per slice as the user asked. Three chained PRs; each builds green on its own:

| PR | Contents | Est. authored lines | Seam rationale |
|---|---|---|---|
| 1 | Solution, props, Domain (entities, enums, `Address`, rent-split + rounding), Domain.Tests, WPF shell, CI split | 400–520 | Ends at a green two-job CI with the architecture guard live — the enforcement mechanism must exist before the code it guards grows |
| 2 | Infrastructure project, `DbContext`, six configurations, naming convention, design-time factory, secrets wiring | 380–450 | Mapping is reviewable on its own; no migration, no database touched |
| 3 | Initial migration + trigger SQL, Infrastructure.Tests with Testcontainers, `core` job runs them, config.yaml testing block | 450–650 | The generated migration inflates the diff but not authored risk; the hand-written SQL and the tests that prove it land together |

## Open Questions

- [ ] **`credential-exposure` — DEFERRED, NOT RESOLVED.** This change ships the first real Npgsql
      connection string. A direct client connection puts the database credential on the office PC,
      which makes any future UI-level Admin/Empleado separation **cosmetic**: anyone with the
      machine can open the string and connect with full rights. Users and roles are out of scope,
      so nothing here depends on resolving it. **What it will cost when it is resolved**: adopting
      Supabase Auth with real Postgres roles and RLS means per-table RLS policies, a login flow,
      swapping the Npgsql connection to a per-user token, and reworking every EF query that assumes
      unrestricted access — a change of the same order as this one, and cheaper the earlier it is
      taken. Flag it again at the users-and-roles change.
- [ ] Postgres major version of the Supabase project — pin the Testcontainers image to it at apply
      time; do not assume.
- [ ] Can one natural person hold two roles on the same contract? The `(contract_id, party_id,
      role)` PK permits it; a domain rule can forbid it later at no schema cost.
- [ ] Repository admin must add `core` to the GitHub ruleset after PR 1, or the Linux job is
      advisory only.

Untouched by this change: `scheduler-mechanism`, `pdf-library`.
