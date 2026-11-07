using System.Text.RegularExpressions;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 30 to 34 and 94 (Requirements 11, 12 and 18) through the port against real Postgres, with
/// effects read back by raw SQL. Each test has its own database, because a list answers over every row. All people and
/// addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndexRegistryTests
{
    private static readonly FixedClock Clock = new(new DateOnly(2026, 11, 6));
    private static readonly IndexPeriod July = new(2026, 7);
    private static readonly IndexPeriod August = new(2026, 8);

    private readonly PostgresFixture _fixture;

    public IndexRegistryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static IndexRegistry Registry(DeskWorld world) => new(world.NewContext(), Clock);

    private static async Task<(IndexRegistry Registry, InmobiliariaDbContext Context, string Login)> RegistryAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (login, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!)
        {
            Username = login,
            Password = password,
        };
        var context = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);

        return (new IndexRegistry(context, Clock), context, login);
    }

    private static async Task<Guid> IndexIdAsync(DeskWorld world, string name)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);

        return await ReciboTestSupport.ScalarAsync<Guid>(connection, "SELECT id FROM economic_indices WHERE name = @n", ("n", name));
    }

    private static async Task<IndexValueListing> AddedAsync(Task<IndexEntryResult> call)
    {
        var result = await call;
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}.");
        return result.Value!;
    }

    private static Task<long> ValuesAsync(DeskWorld world) => world.CountAsync("SELECT count(*) FROM index_values");

    private static Task<long> RecordsAsync(DeskWorld world) => world.CountAsync("SELECT count(*) FROM index_value_corrections");

    private static async Task<decimal> StoredLevelAsync(DeskWorld world, Guid value)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);

        return await ReciboTestSupport.ScalarAsync<decimal>(connection, "SELECT level FROM index_values WHERE id = @v", ("v", value));
    }

    // ---- The catalog ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task TheList_NamesTheSeededIndices_WithTheirValuesLatestFirst()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        await AddedAsync(Registry(world).AddAsync(ipc, August, 9_100m, false));

        var list = await Registry(world).ListAsync();

        Assert.Equal(["ICL", "IPC", "RIPTE"], list.Select(i => i.Name));
        Assert.Equal([August, July], list.Single(i => i.Name == "IPC").Values.Select(v => v.Period));
        Assert.Empty(list.Single(i => i.Name == "ICL").Values);
    }

    // ---- Test 31 -------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task RipteOverAMillion_IsStoredWithoutLoss()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var added = await AddedAsync(Registry(world).AddAsync(await IndexIdAsync(world, "RIPTE"), new IndexPeriod(2026, 6), 1_915_878.76m, false));

        Assert.Equal(1_915_878.76m, await StoredLevelAsync(world, added.Id));
    }

    // ---- Test 32 -------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("0", IndexRefusal.LevelNotPositive)]
    [InlineData("-1", IndexRefusal.LevelNotPositive)]
    [InlineData("1.1234567", IndexRefusal.TooManyDecimals)]
    [InlineData("1000000000000", IndexRefusal.LevelTooLarge)]
    public async Task ALevelTheRulesRefuse_StoresNothing(string level, IndexRefusal refusal)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var result = await Registry(world).AddAsync(
            await IndexIdAsync(world, "IPC"), August, decimal.Parse(level, System.Globalization.CultureInfo.InvariantCulture), false);

        Assert.Equal(refusal, result.Refusal);
        Assert.Equal(0, await ValuesAsync(world));
    }

    [SkippableFact]
    public async Task AFutureMonth_IsRefused_AndTheCurrentOneIsNot()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var icl = await IndexIdAsync(world, "ICL");

        var future = await Registry(world).AddAsync(icl, new IndexPeriod(2026, 12), 25m, false);

        Assert.Equal(IndexRefusal.FuturePeriod, future.Refusal);
        Assert.Equal(0, await ValuesAsync(world));
        await AddedAsync(Registry(world).AddAsync(icl, new IndexPeriod(2026, 11), 25m, false));
    }

    [SkippableFact]
    public async Task ADuplicate_IsRefused_NamingTheExistingValueToCorrect()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var first = await AddedAsync(Registry(world).AddAsync(ipc, August, 9_440m, false));

        var result = await Registry(world).AddAsync(ipc, August, 9_400m, false);

        Assert.Equal(IndexRefusal.DuplicatePeriod, result.Refusal);
        Assert.Equal(first, result.Existing);
        Assert.Equal(1, await ValuesAsync(world));
        Assert.Equal(9_440m, await StoredLevelAsync(world, first.Id));
    }

    [SkippableFact]
    public async Task TwoPeopleTypingTheSameMonth_OneIsSaved_AndTheOtherIsRefusedByItsOwnMessage()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var barrier = new SessionBarrier(2);
        var insert = new Regex(@"INSERT INTO ""?index_values""?\s", RegexOptions.Compiled);
        await using var firstContext = world.NewContext(CommandHook.WaitAtFirst(insert, barrier));
        await using var secondContext = world.NewContext(CommandHook.WaitAtFirst(insert, barrier));

        var results = await Task.WhenAll(
            new IndexRegistry(firstContext, Clock).AddAsync(ipc, August, 9_440m, false),
            new IndexRegistry(secondContext, Clock).AddAsync(ipc, August, 9_400m, false));

        var saved = Assert.Single(results, r => r.Succeeded);
        var refused = Assert.Single(results, r => !r.Succeeded);
        Assert.Equal(IndexRefusal.DuplicatePeriod, refused.Refusal);
        Assert.Equal(saved.Value, refused.Existing);
        Assert.Equal(1, await ValuesAsync(world));
    }

    [SkippableFact]
    public async Task AnIndexNoLongerThere_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var result = await Registry(world).AddAsync(Guid.NewGuid(), August, 9_440m, false);

        Assert.Equal(IndexRefusal.IndexNotFound, result.Refusal);
    }

    // ---- Test 33 -------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task AFarLevel_WarnsAndStoresNothing_UntilConfirmedAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var july = await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));

        var warned = await Registry(world).AddAsync(ipc, August, 90_000m, false);

        Assert.Equal(IndexRefusal.FarLevelNotAccepted, warned.Refusal);
        Assert.Equal(july, warned.Existing);
        Assert.Equal(1, await ValuesAsync(world));

        var confirmed = await AddedAsync(Registry(world).AddAsync(ipc, August, 90_000m, true));

        Assert.Equal(90_000m, await StoredLevelAsync(world, confirmed.Id));
    }

    [SkippableFact]
    public async Task AFarCorrection_WarnsAndChangesNothing_UntilConfirmedAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        var august = await AddedAsync(Registry(world).AddAsync(ipc, August, 9_100m, false));

        var warned = await Registry(world).CorrectAsync(august.Id, 91_000m, false);

        Assert.Equal(IndexRefusal.FarLevelNotAccepted, warned.Refusal);
        Assert.Equal(9_100m, await StoredLevelAsync(world, august.Id));
        Assert.Equal(0, await RecordsAsync(world));

        await AddedAsync(Registry(world).CorrectAsync(august.Id, 91_000m, true));

        Assert.Equal(91_000m, await StoredLevelAsync(world, august.Id));
        Assert.Equal(1, await RecordsAsync(world));
    }

    // ---- Test 34 -------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ATypedValue_ClearsThatContractsMissingIndexEntry()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var dueDate = new DateOnly(2026, 9, 1);
        await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        Contract contract;
        await using (var context = world.NewContext())
        {
            var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", "1258", "Sunchales", "Santa Fe", "2322"));
            context.Units.Add(unit);
            var start = dueDate.AddMonths(-1);
            contract = new Contract(Guid.NewGuid(), start, start.AddYears(2), 100_000m, [new UnitShare(unit.Id, 100m)]);
            context.Contracts.Add(contract);
            var clause = new AdjustmentClause(Guid.NewGuid(), contract.Id, [ipc], intervalMonths: 1, RoundingRule.TruncateToWholePeso);
            context.AdjustmentClauses.Add(clause);
            contract.AttachAdjustmentClause(clause);
            await context.SaveChangesAsync();
        }

        await using var readContext = world.NewContext();
        var before = Assert.Single(await new DueAdjustmentQuery(readContext).GetDueAsync(dueDate), d => d.ContractId == contract.Id);
        Assert.Contains(before.Missing, m => m.EconomicIndexId == ipc && m.Period == August);

        await AddedAsync(Registry(world).AddAsync(ipc, August, 9_100m, false));

        await using var again = world.NewContext();
        var after = Assert.Single(await new DueAdjustmentQuery(again).GetDueAsync(dueDate), d => d.ContractId == contract.Id);
        Assert.DoesNotContain(after.Missing, m => m.EconomicIndexId == ipc && m.Period == August);
        Assert.NotNull(after.ProposedCanon);
    }

    // ---- Test 83, with what the source really sends -------------------------------------------------------------------------

    [SkippableFact]
    public async Task ALevelWithTheTrailingZerosTheBcraSends_IsStoredAsThatLevel()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var icl = await IndexIdAsync(world, "ICL");

        var added = await AddedAsync(Registry(world).AddAsync(icl, July, 34.24000000000m, false));

        Assert.Equal(34.24m, await StoredLevelAsync(world, added.Id));
    }

    // ---- Test 94, through the port ---------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task BothRoles_CanTypeAndCorrect_AndTheRecordNamesWhoDidIt(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var (registry, context, login) = await RegistryAsAsync(world, groupRole);
        await using var _ = context;

        var value = await AddedAsync(registry.AddAsync(ipc, August, 9_440m, false));
        var corrected = await registry.CorrectAsync(value.Id, 9_400m, false);

        Assert.True(corrected.Succeeded, $"Refused: {corrected.Refusal}.");
        Assert.Equal(9_440m, corrected.PreviousLevel);
        Assert.Equal(9_400m, corrected.Value!.Level);
        Assert.Equal(9_400m, await StoredLevelAsync(world, value.Id));

        var record = Assert.Single(await registry.ListCorrectionsAsync(value.Id));
        Assert.Equal(9_440m, record.PreviousLevel);
        Assert.Equal(9_400m, record.NewLevel);
        Assert.Equal(login, record.RecordedBy);
        Assert.True(record.RecordedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [SkippableFact]
    public async Task TheRecords_ComeBackLatestFirst_AndOnlyForThatValue()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var july = await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        var august = await AddedAsync(Registry(world).AddAsync(ipc, August, 9_100m, false));
        await AddedAsync(Registry(world).CorrectAsync(august.Id, 9_110m, false));
        await AddedAsync(Registry(world).CorrectAsync(august.Id, 9_120m, false));
        await AddedAsync(Registry(world).CorrectAsync(july.Id, 9_001m, false));

        var records = await Registry(world).ListCorrectionsAsync(august.Id);

        Assert.Equal([9_120m, 9_110m], records.Select(r => r.NewLevel));
    }

    [SkippableFact]
    public async Task SavingTheLevelAlreadyStored_RecordsNothing_NotEvenWhenItIsFar()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        var august = await AddedAsync(Registry(world).AddAsync(ipc, August, 90_000m, true));

        var again = await Registry(world).CorrectAsync(august.Id, 90_000m, false);

        Assert.True(again.Succeeded, $"Refused: {again.Refusal}.");
        Assert.Equal(0, await RecordsAsync(world));
    }

    [SkippableFact]
    public async Task CorrectingAValueNoLongerThere_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var result = await Registry(world).CorrectAsync(Guid.NewGuid(), 9_400m, false);

        Assert.Equal(IndexRefusal.ValueNotFound, result.Refusal);
    }

    [SkippableFact]
    public async Task ACorrectionARuleRefuses_ChangesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var august = await AddedAsync(Registry(world).AddAsync(await IndexIdAsync(world, "IPC"), August, 9_440m, false));

        var result = await Registry(world).CorrectAsync(august.Id, 0m, true);

        Assert.Equal(IndexRefusal.LevelNotPositive, result.Refusal);
        Assert.Equal(9_440m, await StoredLevelAsync(world, august.Id));
        Assert.Equal(0, await RecordsAsync(world));
    }

    // ---- Requirement 18: the adjustments a correction affects ------------------------------------------------------------

    private async Task<(Contract Contract, Guid Adjustment)> SeedConfirmedAsync(DeskWorld world, Guid index, IndexPeriod basePeriod, decimal baseLevel, IndexPeriod endPeriod, decimal endLevel, string tenantName)
    {
        var user = await AccessTestSupport.ProvisionUserAsync(world.Database.Context, "adjusts-" + Guid.NewGuid().ToString("N")[..8], UserRole.Admin);
        await using var context = world.NewContext();
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", Guid.NewGuid().ToString("N")[..4], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);
        var contract = new Contract(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1), 100_000m, [new UnitShare(unit.Id, 100m)]);
        var tenant = new Party(Guid.NewGuid(), tenantName, "Prueba");
        context.Parties.Add(tenant);
        contract.AssignParty(tenant, PartyRole.Tenant);
        context.Contracts.Add(contract);
        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            contract, index, basePeriod, baseLevel, endPeriod, endLevel, new DateOnly(2026, 9, 1), DateTimeOffset.UtcNow, user);
        context.RentAdjustments.Add(adjustment);
        await context.SaveChangesAsync();

        return (contract, adjustment.Id);
    }

    [SkippableFact]
    public async Task CorrectingAnIndex_ListsTheAdjustmentThatUsedIt_AndChangesNoRent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var august = await AddedAsync(Registry(world).AddAsync(ipc, August, 9_440m, false));
        var (contract, adjustment) = await SeedConfirmedAsync(world, ipc, July, 9_000m, August, 9_440m, "Maria");
        var canon = await world.CountAsync("SELECT monthly_rent::bigint FROM contracts WHERE id = @c", ("c", contract.Id));

        var preview = await Registry(world).ListAffectedAdjustmentsAsync(august.Id, 9_400m);
        await AddedAsync(Registry(world).CorrectAsync(august.Id, 9_400m, false));
        var after = await Registry(world).ListAffectedAdjustmentsAsync(august.Id, 9_400m);

        foreach (var list in new[] { preview, after })
        {
            var affected = Assert.Single(list);
            Assert.Equal(adjustment, affected.AdjustmentId);
            Assert.Equal(contract.Id, affected.ContractId);
            Assert.Equal("Maria Prueba", affected.TenantName);
            Assert.Equal(new DateOnly(2026, 9, 1), affected.EffectiveDate);
            Assert.Equal(9_440m, affected.UsedLevel);
        }

        Assert.Equal(canon, await world.CountAsync("SELECT monthly_rent::bigint FROM contracts WHERE id = @c", ("c", contract.Id)));
        Assert.Equal(1, await world.CountAsync(
            "SELECT count(*) FROM rent_adjustment_index_values WHERE end_level = 9440"));
    }

    [SkippableFact]
    public async Task AnAdjustmentUsingTheValueAsItsBase_IsListed_WithTheBaseLevel()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var july = await AddedAsync(Registry(world).AddAsync(ipc, July, 9_000m, false));
        await SeedConfirmedAsync(world, ipc, July, 9_000m, August, 9_440m, "Maria");

        var affected = Assert.Single(await Registry(world).ListAffectedAdjustmentsAsync(july.Id, 8_900m));

        Assert.Equal(9_000m, affected.UsedLevel);
    }

    [SkippableFact]
    public async Task AnAdjustmentOnAnotherIndexOrMonth_OrWhoseCopyAlreadyMatches_IsNotListed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var ipc = await IndexIdAsync(world, "IPC");
        var ripte = await IndexIdAsync(world, "RIPTE");
        var august = await AddedAsync(Registry(world).AddAsync(ipc, August, 9_440m, false));
        await SeedConfirmedAsync(world, ripte, July, 9_000m, August, 9_440m, "OtroIndice");
        await SeedConfirmedAsync(world, ipc, new IndexPeriod(2026, 5), 9_000m, new IndexPeriod(2026, 6), 9_440m, "OtroMes");

        Assert.Empty(await Registry(world).ListAffectedAdjustmentsAsync(august.Id, 9_400m));

        await SeedConfirmedAsync(world, ipc, July, 9_000m, August, 9_400m, "YaCoincide");

        Assert.Empty(await Registry(world).ListAffectedAdjustmentsAsync(august.Id, 9_400m));
    }

    [SkippableFact]
    public async Task AValueNoLongerThere_AffectsNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        Assert.Empty(await Registry(world).ListAffectedAdjustmentsAsync(Guid.NewGuid(), 9_400m));
    }
}
