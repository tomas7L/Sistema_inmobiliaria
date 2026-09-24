using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping onto the thirteen target Postgres tables: the six from lease-contract, the
/// six added by rent-adjustments (economic-index and rent-adjustment capabilities), and
/// <c>app_users</c> added by users-and-roles — the FK target every other table points at when
/// it needs to name a person (design Decision 8). Repository ports/adapters are not part of
/// any of these changes — see design.md Decision 2 and the scope guard in the technical
/// approach.
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
    }
}
