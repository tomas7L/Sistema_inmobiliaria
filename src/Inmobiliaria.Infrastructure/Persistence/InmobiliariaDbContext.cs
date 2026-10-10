using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping onto the fifteen target Postgres tables: the six from lease-contract, the
/// six added by rent-adjustments (economic-index and rent-adjustment capabilities),
/// <c>app_users</c> added by users-and-roles — the FK target every other table points at when
/// it needs to name a person (design Decision 8) — and <c>contract_accounts</c> plus
/// <c>account_movements</c> added by cuenta-corriente, the append-only ledger of what each
/// contract owes. Repository ports/adapters are not part of any of these changes — see
/// design.md Decision 2 and the scope guard in the technical approach.
/// </summary>
public sealed class InmobiliariaDbContext : DbContext
{
    public InmobiliariaDbContext(DbContextOptions<InmobiliariaDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractParty> ContractParties => Set<ContractParty>();
    public DbSet<ContractUnit> ContractUnits => Set<ContractUnit>();
    public DbSet<ContractDocument> ContractDocuments => Set<ContractDocument>();
    public DbSet<EconomicIndex> EconomicIndices => Set<EconomicIndex>();
    public DbSet<IndexValue> IndexValues => Set<IndexValue>();
    public DbSet<AdjustmentClause> AdjustmentClauses => Set<AdjustmentClause>();
    public DbSet<AdjustmentClauseIndex> AdjustmentClauseIndices => Set<AdjustmentClauseIndex>();
    public DbSet<RentAdjustment> RentAdjustments => Set<RentAdjustment>();
    public DbSet<RentAdjustmentIndexValue> RentAdjustmentIndexValues => Set<RentAdjustmentIndexValue>();

    public DbSet<ContractAccount> ContractAccounts => Set<ContractAccount>();

    /// <summary>
    /// Exposed as its own set for reading — a balance is a SUM over movements, and the mora
    /// worklist asks across accounts, neither of which should have to load whole aggregates.
    /// Writing still goes through <see cref="ContractAccount.Append"/>, which is where the
    /// invariants live.
    /// </summary>
    public DbSet<AccountMovement> AccountMovements => Set<AccountMovement>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Applied here, not only at the call site that builds the options, so the
        // naming convention holds regardless of how the context is constructed
        // (design-time factory, desktop host, or a future test fixture).
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InmobiliariaDbContext).Assembly);

        ApplyApplicationSuppliedKeys(modelBuilder);
    }

    /// <summary>
    /// Every primary key in this project is supplied by the application. No id column carries a
    /// database default, and no domain type generates its own — every id arrives through a
    /// constructor parameter, because an aggregate is valid from birth and cannot be half-built
    /// waiting for the database to name it.
    ///
    /// EF has to be told that, and left alone it assumes the opposite for a <see cref="Guid"/>
    /// key. That assumption is how it decides whether an untracked entity reached through a
    /// navigation is new: seeing an id already set, it concludes the row must exist and marks the
    /// entity <c>Modified</c>. The UPDATE that follows matches nothing, and if that entity owns
    /// children of its own, their inserts hit a foreign key with no parent —
    /// <c>23503 ... violates foreign key constraint</c>, which is exactly how this was found.
    ///
    /// It only bites on the SECOND unit of work: a child added to a parent already on disk.
    /// Seeding a parent and its children in one save makes children of an Added parent Added too,
    /// which is why every test in the project passed while confirming a rent adjustment on a
    /// loaded contract could not work.
    ///
    /// Declared here rather than in each configuration because it is one fact about this project,
    /// not nine coincidences — and because the next entity somebody adds inherits it without
    /// having to know any of the above.
    /// </summary>
    private static void ApplyApplicationSuppliedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1)
            {
                continue;
            }

            var property = key.Properties[0];
            if (property.ClrType == typeof(Guid))
            {
                property.ValueGenerated = ValueGenerated.Never;
            }
        }
    }
}
