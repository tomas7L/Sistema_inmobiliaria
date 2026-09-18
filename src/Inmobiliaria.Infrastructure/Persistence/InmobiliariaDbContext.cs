using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping onto the six target Postgres tables. Repository ports/adapters are not
/// part of this change — see design.md Decision 2 and the scope guard in the technical
/// approach.
/// </summary>
public sealed class InmobiliariaDbContext : DbContext
{
    public InmobiliariaDbContext(DbContextOptions<InmobiliariaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractParty> ContractParties => Set<ContractParty>();
    public DbSet<ContractUnit> ContractUnits => Set<ContractUnit>();
    public DbSet<ContractDocument> ContractDocuments => Set<ContractDocument>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Applied here, not only at the call site that builds the options, so the
        // naming convention holds regardless of how the context is constructed
        // (design-time factory, desktop host, or a future test fixture).
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The rent-adjustment types exist in the domain but are not persisted yet: their EF
        // configurations and their migration are the next slice. EF discovers types by
        // convention through navigations, so the moment Contract gained `AdjustmentClause` and
        // `Adjustments`, EF pulled all four into the model and failed validation on mappings
        // that do not exist yet.
        //
        // Ignoring them states the current truth rather than working around it — in this slice
        // they genuinely have no tables. The next slice deletes these four lines and adds the
        // configurations in the same commit, so the diff reads as "these become persistent".
        modelBuilder.Ignore<AdjustmentClause>();
        modelBuilder.Ignore<AdjustmentClauseIndex>();
        modelBuilder.Ignore<RentAdjustment>();
        modelBuilder.Ignore<RentAdjustmentIndexValue>();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InmobiliariaDbContext).Assembly);
    }
}
