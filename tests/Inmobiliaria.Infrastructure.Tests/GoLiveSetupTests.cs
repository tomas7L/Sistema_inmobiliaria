using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// ui-de-cobranza spec tests 27 to 30, port half (design section 5.3), against a real database. Each test
/// runs on its own database: the recibo counter and "no recibo yet" belong to the whole database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GoLiveSetupTests
{
    private readonly PostgresFixture _fixture;

    public GoLiveSetupTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly Today => DeskWorld.Today;

    private static DateOnly Start => DeskWorld.OldestPeriod;

    private static GoLiveSetup SetupOver(InmobiliariaDbContext context) => new(context, new FixedClock(Today));

    private static async Task<(Guid ContractId, Guid AccountId)> SeedAsync(DeskWorld world)
    {
        var accountId = await world.SeedAccountAsync(start: Start);
        await using var context = world.NewContext();
        var contractId = await context.ContractAccounts
            .Where(a => a.Id == accountId).Select(a => a.ContractId).SingleAsync();

        return (contractId, accountId);
    }

    private static async Task<Guid> SeedContractWithoutAccountAsync(DeskWorld world)
    {
        await using var context = world.NewContext();
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("San Martín", "45", "Sunchales", "Santa Fe", "2322"));
        var tenant = new Party(Guid.NewGuid(), "Ana", "Demaría");
        var contract = new Contract(
            Guid.NewGuid(), Start, Start.AddYears(2).AddDays(-1), 300_000m, [new UnitShare(unit.Id, 100m)],
            8m, Contract.DefaultDueDay, null, null);
        contract.AssignParty(tenant, PartyRole.Tenant);

        context.Units.Add(unit);
        context.Parties.Add(tenant);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        return contract.Id;
    }

    private static Task<int> InsertMovementAsync(InmobiliariaDbContext context, Guid accountId) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES ({Guid.NewGuid()}, {accountId}, 'OpeningBalance', 0, {Start}, NULL)
            """);

    private static async Task<DateOnly?> StoredCutoverAsync(DeskWorld world, Guid contractId)
    {
        await using var context = world.NewContext();
        return await context.Contracts.Where(c => c.Id == contractId).Select(c => c.CutoverDate).SingleAsync();
    }

    private static async Task<InmobiliariaDbContext> ContextAsAsync(DeskWorld world, UserRole role)
    {
        var username = $"g{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, role);
        return AccessTestSupport.BuildDbContextAs(world.Database.Context, username);
    }

    /// <summary>Spec test 27, port half: date or none, changeable or locked, and a contract with no account.</summary>
    [SkippableFact]
    public async Task TheList_ShowsEachContractsCutoverState()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var (none, _) = await SeedAsync(world);
        var (set, _) = await SeedAsync(world);
        var (locked, lockedAccount) = await SeedAsync(world);
        var noAccount = await SeedContractWithoutAccountAsync(world);

        var fifth = Start.AddDays(4);
        Assert.True((await SetupOver(world.NewContext()).SetCutoverDateAsync(set, fifth)).Succeeded);
        await InsertMovementAsync(world.NewContext(), lockedAccount);

        var listing = (await SetupOver(world.NewContext()).ListContractsAsync()).ToDictionary(l => l.ContractId);

        Assert.Equal(4, listing.Count);
        Assert.Equal((null, CutoverState.Changeable), (listing[none].CutoverDate, listing[none].State));
        Assert.Equal((fifth, CutoverState.Changeable), (listing[set].CutoverDate, listing[set].State));
        Assert.Equal((null, CutoverState.Locked), (listing[locked].CutoverDate, listing[locked].State));
        Assert.Equal((null, CutoverState.NoAccount), (listing[noAccount].CutoverDate, listing[noAccount].State));

        Assert.Equal("Perez, Maria", listing[none].Tenants);
        Assert.Equal("Bv. Belgrano 123, Sunchales", listing[none].Units);
        Assert.Equal(Start, listing[none].StartDate);
        Assert.Equal("Demaría, Ana", listing[noAccount].Tenants);
    }

    /// <summary>
    /// Spec test 28: a set date is stored, a correction before any movement is accepted, and after a
    /// movement the change is a refusal with the stored date unchanged. The domain's bounds and a missing
    /// account come back as refusals too.
    /// </summary>
    [SkippableFact]
    public async Task SettingACutoverDate_StoresIt_AndIsRefusedAsAValueOnceLocked()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var (contractId, accountId) = await SeedAsync(world);
        var noAccount = await SeedContractWithoutAccountAsync(world);

        Assert.True((await SetupOver(world.NewContext()).SetCutoverDateAsync(contractId, Start.AddDays(4))).Succeeded);
        Assert.True((await SetupOver(world.NewContext()).SetCutoverDateAsync(contractId, Start.AddDays(5))).Succeeded);
        Assert.Equal(Start.AddDays(5), await StoredCutoverAsync(world, contractId));

        Assert.Equal(
            SetupRefusal.CutoverAfterToday,
            (await SetupOver(world.NewContext()).SetCutoverDateAsync(contractId, Today.AddDays(1))).Refusal);
        Assert.Equal(
            SetupRefusal.CutoverBeforeContractStart,
            (await SetupOver(world.NewContext()).SetCutoverDateAsync(contractId, Start.AddDays(-1))).Refusal);
        Assert.Equal(
            SetupRefusal.NoAccount,
            (await SetupOver(world.NewContext()).SetCutoverDateAsync(noAccount, Start)).Refusal);

        await InsertMovementAsync(world.NewContext(), accountId);

        Assert.Equal(
            SetupRefusal.CutoverLocked,
            (await SetupOver(world.NewContext()).SetCutoverDateAsync(contractId, Start.AddDays(6))).Refusal);
        Assert.Equal(Start.AddDays(5), await StoredCutoverAsync(world, contractId));
    }

    /// <summary>The database's Admin-only rule reaches the port as a refusal, not an error (spec Decision 31).</summary>
    [SkippableFact]
    public async Task AnEmpleadoSettingACutoverDate_IsRefusedAsAdminOnly()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var (contractId, _) = await SeedAsync(world);
        await using var empleado = await ContextAsAsync(world, UserRole.Empleado);

        Assert.Equal(
            SetupRefusal.CutoverAdminOnly,
            (await SetupOver(empleado).SetCutoverDateAsync(contractId, Start)).Refusal);
        Assert.Null(await StoredCutoverAsync(world, contractId));
    }

    /// <summary>Spec test 29: not set, set and changeable, set and locked, read by both roles.</summary>
    [SkippableFact]
    public async Task TheStartingNumberState_IsReadByBothRoles()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        await using var admin = await ContextAsAsync(world, UserRole.Admin);
        await using var empleado = await ContextAsAsync(world, UserRole.Empleado);

        foreach (var context in new[] { admin, empleado })
        {
            Assert.Equal(new StartingNumberState(null, false), await SetupOver(context).GetStartingNumberStateAsync());
        }

        Assert.True((await SetupOver(admin).SetStartingNumberAsync(31001)).Succeeded);
        foreach (var context in new[] { admin, empleado })
        {
            var state = await SetupOver(context).GetStartingNumberStateAsync();
            Assert.Equal((31001, true, false), (state.StartingNumber, state.IsSet, state.IsLocked));
        }

        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await IssueOneReciboAsync(world, connection);
        }

        foreach (var context in new[] { admin, empleado })
        {
            var state = await SetupOver(context).GetStartingNumberStateAsync();
            Assert.Equal((31001, true, true), (state.StartingNumber, state.IsSet, state.IsLocked));
        }
    }

    /// <summary>
    /// Spec test 30, port half: 31001 then 31100 before any recibo are accepted and the first number taken
    /// is 31100. Afterwards the database's lock is a refusal; so are an Empleado and a number below 1.
    /// </summary>
    [SkippableFact]
    public async Task TheStartingNumber_IsCorrectedBeforeTheFirstRecibo_AndRefusedAsAValueAfterwards()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        await using var admin = await ContextAsAsync(world, UserRole.Admin);
        await using var empleado = await ContextAsAsync(world, UserRole.Empleado);

        Assert.Equal(SetupRefusal.StartingNumberAdminOnly, (await SetupOver(empleado).SetStartingNumberAsync(31001)).Refusal);
        Assert.Equal(SetupRefusal.StartingNumberOutOfRange, (await SetupOver(admin).SetStartingNumberAsync(0)).Refusal);

        Assert.True((await SetupOver(admin).SetStartingNumberAsync(31001)).Succeeded);
        Assert.True((await SetupOver(admin).SetStartingNumberAsync(31100)).Succeeded);

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(31100, await IssueOneReciboAsync(world, connection));

        Assert.Equal(SetupRefusal.StartingNumberLocked, (await SetupOver(admin).SetStartingNumberAsync(32000)).Refusal);
        Assert.Equal((31100, 31102), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    /// <summary>
    /// Takes the first number the way the desk does and writes the pair with it, so "a recibo exists" is
    /// true in the database, not only in the counter.
    /// </summary>
    private static async Task<int> IssueOneReciboAsync(DeskWorld world, Npgsql.NpgsqlConnection connection)
    {
        var number = await ReciboTestSupport.IssueAsync(connection, null)
            ?? throw new InvalidOperationException("The counter had no starting number.");
        await ReciboTestSupport.SeedPaidPeriodAsync(world.NewContext(), connection, Start, number);
        return number;
    }
}
