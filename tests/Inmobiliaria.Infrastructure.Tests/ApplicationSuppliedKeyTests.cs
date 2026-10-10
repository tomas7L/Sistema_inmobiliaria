using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Every primary key in this project is supplied by the application: no id column carries a
/// database default, and no domain type generates its own — every id arrives through a
/// constructor parameter.
///
/// EF has to be told that. It decides whether an untracked entity reached through a navigation is
/// new by asking whether it generates the key itself; left to assume it does, it sees an id that
/// is already set, concludes the row must already exist, and marks the entity Modified. The UPDATE
/// that follows matches no row.
///
/// The shape that triggers it is adding a child to an ALREADY-PERSISTED parent — the second unit
/// of work, not the first. Every test below does exactly that, because seeding a parent and its
/// children in one save hides the whole class of bug.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationSuppliedKeyTests
{
    private readonly PostgresFixture _fixture;

    public ApplicationSuppliedKeyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static Address SomeAddress() =>
        new("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322");

    /// <summary>A contract that is already on disk, with its unit, and nothing else.</summary>
    private static async Task<Contract> PersistedContractAsync(InmobiliariaDbContext context)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), SomeAddress());
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(unit.Id, 100m)]);
        context.Contracts.Add(contract);

        await context.SaveChangesAsync();

        return contract;
    }

    /// <summary>
    /// The case this was found by: confirming a rent adjustment on a contract loaded in an earlier
    /// save. This is the ordinary production path — a contract is read from the database, the
    /// operator confirms an adjustment, it is saved — and it failed with
    /// <c>23503 ... violates foreign key constraint</c> on <c>rent_adjustment_index_values</c>,
    /// because the parent adjustment was never inserted.
    /// </summary>
    [SkippableFact]
    public async Task ConfirmingAnAdjustment_OnAnAlreadyPersistedContract_Succeeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var contract = await PersistedContractAsync(context);

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);

        var confirmer = new AppUser(
            Guid.NewGuid(), $"key-{Guid.NewGuid():N}"[..24], "Application Key Test User");
        context.AppUsers.Add(confirmer);

        await context.SaveChangesAsync();

        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 7), 110m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, confirmer.Id);

        await context.SaveChangesAsync();

        await using var fresh = _fixture.CreateDbContext();
        var stored = await fresh.RentAdjustments
            .AsNoTracking()
            .SingleAsync(a => a.Id == adjustment.Id);

        Assert.Equal(contract.Id, stored.ContractId);
    }

    /// <summary>
    /// Attaching an adjustment clause to a contract already on disk. Same shape, a different
    /// single-Guid key reached through a navigation.
    /// </summary>
    [SkippableFact]
    public async Task AttachingAnAdjustmentClause_ToAnAlreadyPersistedContract_Succeeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var contract = await PersistedContractAsync(context);

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);
        await context.SaveChangesAsync();

        var clause = new AdjustmentClause(
            Guid.NewGuid(), contract.Id, [index.Id], 6, RoundingRule.TruncateToWholePeso);
        context.AdjustmentClauses.Add(clause);
        contract.AttachAdjustmentClause(clause);

        await context.SaveChangesAsync();

        await using var fresh = _fixture.CreateDbContext();
        Assert.True(await fresh.AdjustmentClauses.AsNoTracking().AnyAsync(c => c.Id == clause.Id));
    }

    /// <summary>
    /// Assigning a party to a contract already on disk. A composite key rather than a single Guid,
    /// included because the claim "this project's keys are application-supplied" has to be checked
    /// against the composite ones too rather than assumed to be irrelevant to them.
    /// </summary>
    [SkippableFact]
    public async Task AssigningAParty_ToAnAlreadyPersistedContract_Succeeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var contract = await PersistedContractAsync(context);

        var party = new Party(Guid.NewGuid(), "Noelia", "Abregu");
        context.Parties.Add(party);
        await context.SaveChangesAsync();

        contract.AssignParty(party, PartyRole.Tenant);
        await context.SaveChangesAsync();

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await fresh.Contracts
            .AsNoTracking().Include(c => c.Parties)
            .SingleAsync(c => c.Id == contract.Id);

        Assert.Contains(reloaded.Parties, p => p.PartyId == party.Id && p.Role == PartyRole.Tenant);
    }

    /// <summary>
    /// Replacing the rent split on a contract already on disk. Also a composite key, and also
    /// reached through a navigation.
    /// </summary>
    [SkippableFact]
    public async Task ReplacingTheRentSplit_OnAnAlreadyPersistedContract_Succeeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var contract = await PersistedContractAsync(context);

        var second = new PropertyUnit(Guid.NewGuid(), SomeAddress());
        context.Units.Add(second);
        await context.SaveChangesAsync();

        var existing = contract.Units.Single().UnitId;
        contract.SetUnitShares([new UnitShare(existing, 60m), new UnitShare(second.Id, 40m)]);

        await context.SaveChangesAsync();

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await fresh.Contracts
            .AsNoTracking().Include(c => c.Units)
            .SingleAsync(c => c.Id == contract.Id);

        Assert.Equal(2, reloaded.Units.Count);
        Assert.Equal(100m, reloaded.Units.Sum(u => u.SharePercentage));
    }

    /// <summary>
    /// The structural guard, so this class of bug cannot come back silently: every entity whose
    /// primary key is a single <see cref="Guid"/> must declare that the application supplies it.
    ///
    /// Asserted over the built EF model rather than over the configuration source, because what
    /// matters is what EF concluded, not what somebody wrote. A new entity added without the
    /// declaration fails here instead of failing the first time somebody adds a child to a parent
    /// that is already on disk.
    /// </summary>
    [SkippableFact]
    public void EverySingleGuidKey_IsDeclaredApplicationSupplied()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        using var context = _fixture.CreateDbContext();

        var offenders = new List<string>();

        foreach (var entity in context.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1)
            {
                continue;
            }

            var property = key.Properties[0];
            if (property.ClrType != typeof(Guid))
            {
                continue;
            }

            if (property.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            {
                offenders.Add($"{entity.ShortName()}.{property.Name} ({property.ValueGenerated})");
            }
        }

        Assert.Empty(offenders);
    }
}
