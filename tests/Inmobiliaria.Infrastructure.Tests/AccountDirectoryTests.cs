using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// ui-de-cobranza spec tests 8 and 9 (flags half), design section 5.1, against a real database and as each
/// real login role. Each test runs on its own database: the starting number is one row for the whole system.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountDirectoryTests
{
    private readonly PostgresFixture _fixture;

    public AccountDirectoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly Start => DeskWorld.OldestPeriod;

    private sealed record Seeded(Guid ContractId, Guid AccountId);

    private static async Task<Seeded> SeedAsync(
        DeskWorld world,
        (string First, string Last) tenant,
        (string First, string Last) lessor,
        string street,
        decimal? rate = 8m)
    {
        await using var context = world.NewContext();

        var unit = new PropertyUnit(Guid.NewGuid(), new Address(street, "45", "Sunchales", "Santa Fe", "2322"));
        var tenantParty = new Party(Guid.NewGuid(), tenant.First, tenant.Last);
        var lessorParty = new Party(Guid.NewGuid(), lessor.First, lessor.Last);
        var contract = new Contract(
            Guid.NewGuid(), Start, Start.AddYears(2).AddDays(-1), 300_000m, [new UnitShare(unit.Id, 100m)],
            rate, Contract.DefaultDueDay, null, null);
        contract.AssignParty(tenantParty, PartyRole.Tenant);
        contract.AssignParty(lessorParty, PartyRole.Lessor);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        context.Units.Add(unit);
        context.Parties.AddRange(tenantParty, lessorParty);
        context.Contracts.Add(contract);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return new Seeded(contract.Id, account.Id);
    }

    private static async Task<InmobiliariaDbContext> ContextAsAsync(DeskWorld world, UserRole role)
    {
        var username = $"d{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, role);
        return AccessTestSupport.BuildDbContextAs(world.Database.Context, username);
    }

    private static AccountDirectory DirectoryOver(InmobiliariaDbContext context) =>
        new(context, new FixedClock(DeskWorld.Today));

    /// <summary>
    /// Spec test 8: by tenant, unit, lessor and short id, ignoring case and accents, ended contracts
    /// included, for both roles.
    /// </summary>
    [SkippableTheory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public async Task TheDirectory_FindsByTenantUnitLessorAndShortId_IgnoringCaseAndAccents(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var ana = await SeedAsync(world, ("Ana", "Demaría"), ("Juan", "Gómez"), "San Martín");
        var pedro = await SeedAsync(world, ("Pedro", "López"), ("Rosa", "Ibáñez"), "Bv. Belgrano");

        await using (var context = world.NewContext())
        {
            var contract = await context.Contracts.SingleAsync(c => c.Id == pedro.ContractId);
            contract.End(EndReason.Expiry, DeskWorld.Today);
            await context.SaveChangesAsync();
        }

        await using var asRole = await ContextAsAsync(world, role);
        var directory = DirectoryOver(asRole);

        Assert.Equal([ana.AccountId], (await directory.SearchAsync("demaria")).Select(l => l.AccountId));
        Assert.Equal([ana.AccountId], (await directory.SearchAsync("SAN MARTIN")).Select(l => l.AccountId));
        Assert.Equal([ana.AccountId], (await directory.SearchAsync("gomez")).Select(l => l.AccountId));
        var shortId = ana.ContractId.ToString("N")[..8];
        Assert.Equal([ana.AccountId], (await directory.SearchAsync(shortId.ToUpperInvariant())).Select(l => l.AccountId));

        var ended = Assert.Single(await directory.SearchAsync("lopez"));
        Assert.Equal(ContractStatus.Ended, ended.Status);

        var listing = Assert.Single(await directory.SearchAsync("Demaría"));
        Assert.Equal(
            (ana.ContractId, shortId, "Demaría, Ana", "Gómez, Juan", "San Martín 45, Sunchales", ContractStatus.Active),
            (listing.ContractId, listing.ContractShortId, listing.Tenants, listing.Lessors, listing.Units, listing.Status));

        Assert.Empty(await directory.SearchAsync("d"));
        Assert.Equal(2, (await directory.SearchAsync("sunchales")).Count);
    }

    /// <summary>
    /// Spec test 9, flags half: a missing rate and a frozen account each make an account not ready; a 0%
    /// rate and a missing cutover date do not.
    /// </summary>
    [SkippableFact]
    public async Task Readiness_NamesAMissingRateAndAFrozenAccount_AndIgnoresAZeroRateAndNoCutover()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var ready = await SeedAsync(world, ("Ana", "Ready"), ("Juan", "Gómez"), "Uno");
        var noRate = await SeedAsync(world, ("Ana", "Norate"), ("Juan", "Gómez"), "Dos", rate: null);
        var zeroRate = await SeedAsync(world, ("Ana", "Zerorate"), ("Juan", "Gómez"), "Tres", rate: 0m);
        var frozen = await SeedAsync(world, ("Ana", "Frozen"), ("Juan", "Gómez"), "Cuatro");

        await using (var context = world.NewContext())
        {
            var account = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == frozen.AccountId);
            account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 1_000m, DeskWorld.Today));
            await context.SaveChangesAsync();
        }

        await using var empleado = await ContextAsAsync(world, UserRole.Empleado);
        var listings = (await DirectoryOver(empleado).SearchAsync("gomez")).ToDictionary(l => l.AccountId, l => l.Readiness);

        Assert.Equal(new AccountReadiness(true, false, true), listings[ready.AccountId]);
        Assert.Equal(new AccountReadiness(false, false, true), listings[noRate.AccountId]);
        Assert.Equal(new AccountReadiness(true, false, true), listings[zeroRate.AccountId]);
        Assert.Equal(new AccountReadiness(true, true, true), listings[frozen.AccountId]);

        Assert.True(listings[ready.AccountId].IsReady);
        Assert.True(listings[zeroRate.AccountId].IsReady);
        Assert.False(listings[noRate.AccountId].IsReady);
        Assert.False(listings[frozen.AccountId].IsReady);
    }

    /// <summary>Spec test 9, flags half: an unset starting number makes every account not ready.</summary>
    [SkippableFact]
    public async Task AnUnsetStartingNumber_MakesEveryAccountNotReady()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var seeded = await SeedAsync(world, ("Ana", "Demaría"), ("Juan", "Gómez"), "San Martín");

        await using var admin = await ContextAsAsync(world, UserRole.Admin);
        var listing = Assert.Single(await DirectoryOver(admin).SearchAsync("demaria"));

        Assert.Equal(seeded.AccountId, listing.AccountId);
        Assert.Equal(new AccountReadiness(true, false, false), listing.Readiness);
        Assert.False(listing.Readiness.IsReady);
    }

    /// <summary>Design section 5.1: a search writes nothing; no accrual is materialised.</summary>
    [SkippableFact]
    public async Task ASearch_WritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, ("Ana", "Demaría"), ("Juan", "Gómez"), "San Martín");

        await DirectoryOver(world.NewContext()).SearchAsync("demaria");

        await using var check = world.NewContext();
        Assert.Equal(0, await check.AccountMovements.CountAsync());
        Assert.Equal(seeded.AccountId, (await check.ContractAccounts.SingleAsync()).Id);
    }
}
