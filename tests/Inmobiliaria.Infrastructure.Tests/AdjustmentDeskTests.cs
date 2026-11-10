using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
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
/// gestion-de-alquileres spec Tests 39, 41 to 43, 45 and 46 (Requirements 14 and 15) through <see cref="AdjustmentDesk"/>, against
/// real Postgres. Every contract starts on 1 January 2026 with a 6-month clause at offset 1, so its adjustments fall due on 1 July
/// 2026 and 1 January 2027, and its due day is the 10th. All people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdjustmentDeskTests
{
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly January = new(2027, 1, 1);

    private readonly PostgresFixture _fixture;

    public AdjustmentDeskTests(PostgresFixture fixture) => _fixture = fixture;

    private sealed record Seeded(Guid Contract, Guid Account, Guid User, Guid FirstIndex, Guid SecondIndex);

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static AdjustmentDesk Desk(DeskWorld world, DateOnly today, Guid user, InmobiliariaDbContext? context = null) =>
        new(context ?? world.NewContext(), new FixedClock(today), user);

    /// <summary>
    /// A contract at 100,000 with a 10% rise in each window: December 2025 to June 2026 for July, June to December 2026 for
    /// January. With <paramref name="twoIndices"/> the clause averages a second index rising 20%, so July's coefficient is 15%.
    /// </summary>
    private static async Task<Seeded> SeedAsync(DeskWorld world, bool twoIndices = false, bool withJanuaryValues = true)
    {
        await using var context = world.NewContext();
        var user = await AccessTestSupport.ProvisionUserAsync(context, $"ajuste-{Guid.NewGuid():N}"[..20], UserRole.Empleado);

        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Bv. Belgrano", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        var tenant = new Party(Guid.NewGuid(), "Maria", "Prueba");
        var lessor = new Party(Guid.NewGuid(), "Juan", "Prueba");
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), 100_000m, [new UnitShare(unit.Id, 100m)], 8m, dueDay: 10);
        contract.AssignParty(tenant, PartyRole.Tenant);
        contract.AssignParty(lessor, PartyRole.Lessor);

        var first = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}"[..20]);
        var second = new EconomicIndex(Guid.NewGuid(), $"RIPTE-{Guid.NewGuid():N}"[..20]);
        var clause = new AdjustmentClause(
            Guid.NewGuid(), contract.Id, twoIndices ? [first.Id, second.Id] : [first.Id], 6, RoundingRule.TruncateToWholePeso, 1);
        contract.AttachAdjustmentClause(clause);

        context.Units.Add(unit);
        context.Parties.AddRange(tenant, lessor);
        context.Contracts.Add(contract);
        context.EconomicIndices.AddRange(first, second);
        context.AdjustmentClauses.Add(clause);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);

        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), first.Id, new IndexPeriod(2025, 12), 100m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), first.Id, new IndexPeriod(2026, 6), 110m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), second.Id, new IndexPeriod(2025, 12), 200m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), second.Id, new IndexPeriod(2026, 6), 240m));
        if (withJanuaryValues)
        {
            context.IndexValues.Add(new IndexValue(Guid.NewGuid(), first.Id, new IndexPeriod(2026, 12), 121m));
            context.IndexValues.Add(new IndexValue(Guid.NewGuid(), second.Id, new IndexPeriod(2026, 12), 288m));
        }

        await context.SaveChangesAsync();

        return new Seeded(contract.Id, account.Id, user, first.Id, second.Id);
    }

    private static async Task<AdjustmentPreview> PreviewAsync(AdjustmentDesk desk, Guid contract)
    {
        var result = await desk.PreviewAsync(contract);
        Assert.Null(result.Refusal);
        return result.Preview!;
    }

    private static async Task<(decimal MonthlyRent, List<RentAdjustment> Rows)> StoredAsync(DeskWorld world, Guid contract)
    {
        await using var read = world.NewContext();
        var stored = await read.Contracts.AsNoTracking().Include(c => c.Adjustments).SingleAsync(c => c.Id == contract);
        return (stored.MonthlyRent, stored.Adjustments.ToList());
    }

    private static async Task<Dictionary<DateOnly, decimal>> AccrualsAsync(DeskWorld world, Guid account)
    {
        await using var read = world.NewContext();
        var stored = await read.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == account);
        return stored.Movements.Where(m => m.Kind == MovementKind.RentAccrual).ToDictionary(m => m.Period!.Value, m => m.Amount);
    }

    // ---- Test 39 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Test39_TheSummary_CarriesEachIndexsLevelsAndPeriods_TheCoefficient_TheDueDate_AndAppliedFrom()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, twoIndices: true);

        var preview = await PreviewAsync(Desk(world, new DateOnly(2026, 7, 12), seeded.User), seeded.Contract);

        Assert.Equal(seeded.Contract, preview.ContractId);
        Assert.Equal("Maria Prueba", preview.TenantName);
        Assert.Equal(100_000m, preview.PreviousCanon);
        Assert.Equal(CombinationRule.Average, preview.Combination);
        Assert.Equal(2, preview.Indices.Count);
        Assert.All(preview.Indices, line =>
        {
            Assert.Equal(new IndexPeriod(2025, 12), line.BasePeriod);
            Assert.Equal(new IndexPeriod(2026, 6), line.EndPeriod);
        });
        Assert.Equal([(100m, 110m, 10m), (200m, 240m, 20m)], preview.Indices.Select(l => (l.BaseLevel, l.EndLevel, l.Variation)));
        Assert.StartsWith("IPC-", preview.Indices[0].IndexName);
        Assert.StartsWith("RIPTE-", preview.Indices[1].IndexName);
        Assert.Equal(15m, preview.Coefficient);
        Assert.Equal(115_000m, preview.NewCanon);
        Assert.Equal(July, preview.DueDate);
        Assert.Equal(August, preview.AppliesFrom);
        Assert.True(preview.IsLate);
    }

    [SkippableFact]
    public async Task APreview_WritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        await PreviewAsync(Desk(world, new DateOnly(2026, 7, 12), seeded.User), seeded.Contract);

        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        Assert.Equal(100_000m, rent);
        Assert.Empty(rows);
    }

    [SkippableFact]
    public async Task AMissingIndexValue_IsRefused_NamingTheIndexAndMonth()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, twoIndices: true, withJanuaryValues: false);

        // July has its values and is confirmed; January then lacks December's.
        var first = await Desk(world, new DateOnly(2027, 1, 12), seeded.User).PreviewAsync(seeded.Contract);
        Assert.NotNull(first.Preview);
        Assert.True((await Desk(world, new DateOnly(2027, 1, 12), seeded.User).ConfirmAsync(first.Preview!)).Succeeded);

        var second = await Desk(world, new DateOnly(2027, 1, 12), seeded.User).PreviewAsync(seeded.Contract);

        Assert.Equal(AdjustmentRefusal.IndexValuesMissing, second.Refusal);
        Assert.Null(second.Preview);
        Assert.Equal(
            [(seeded.FirstIndex, new IndexPeriod(2026, 12)), (seeded.SecondIndex, new IndexPeriod(2026, 12))],
            second.Missing.Select(m => (m.EconomicIndexId, m.Period)));
    }

    [SkippableFact]
    public async Task BeforeItsDueDate_OrForAnUnknownContract_ThereIsNothingToConfirm()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var desk = Desk(world, new DateOnly(2026, 6, 30), seeded.User);

        Assert.Equal(AdjustmentRefusal.NotDue, (await desk.PreviewAsync(seeded.Contract)).Refusal);
        Assert.Equal(AdjustmentRefusal.ContractNotFound, (await desk.PreviewAsync(Guid.NewGuid())).Refusal);
    }

    // ---- Test 41 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Test41_TwoIntervalsBehind_TheOldestShows_ThenTheNext_PricedFromTheNewCanon()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2027, 1, 12);

        var oldest = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        Assert.Equal(July, oldest.DueDate);
        Assert.Equal(100_000m, oldest.PreviousCanon);
        Assert.Equal(110_000m, oldest.NewCanon);

        Assert.True((await Desk(world, today, seeded.User).ConfirmAsync(oldest)).Succeeded);

        var next = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        Assert.Equal(January, next.DueDate);
        Assert.Equal(110_000m, next.PreviousCanon);
        Assert.Equal(121_000m, next.NewCanon);
    }

    // ---- Test 42 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Test42_ASecondConfirmationOfTheSameSummary_IsRefused_AndTheCanonMovesOnce()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2026, 7, 12);
        var preview = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);

        Assert.True((await Desk(world, today, seeded.User).ConfirmAsync(preview)).Succeeded);
        var again = await Desk(world, today, seeded.User).ConfirmAsync(preview);

        Assert.Equal(AdjustmentRefusal.AlreadyConfirmed, again.Refusal);
        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        Assert.Equal(110_000m, rent);
        Assert.Single(rows);
    }

    [SkippableFact]
    public async Task Test42_TwoOperatorsConfirmAtOnce_OneWins_TheOtherIsRefused_AndTheCanonMovesOnce()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2026, 7, 12);
        var first = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        var second = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);

        // Both sessions stop at the account's lock until both have reached it, so the two transactions truly overlap.
        var barrier = new SessionBarrier(2);
        var results = await Task.WhenAll(
            Task.Run(() => Desk(world, today, seeded.User, world.NewContext(CommandHook.WaitAtFirst(CommandHook.LockAccount, barrier))).ConfirmAsync(first)),
            Task.Run(() => Desk(world, today, seeded.User, world.NewContext(CommandHook.WaitAtFirst(CommandHook.LockAccount, barrier))).ConfirmAsync(second)));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Refusal == AdjustmentRefusal.AlreadyConfirmed);
        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        Assert.Equal(110_000m, rent);
        Assert.Single(rows);
    }

    [SkippableFact]
    public async Task AFigureChangedSinceThePreview_IsRefusedAsStale_AndNothingIsWritten()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2026, 7, 12);
        var preview = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);

        await using (var change = world.NewContext())
        {
            await change.Database.ExecuteSqlAsync(
                $"UPDATE index_values SET level = 112 WHERE economic_index_id = {seeded.FirstIndex} AND period = '2026-06-01'");
        }

        var result = await Desk(world, today, seeded.User).ConfirmAsync(preview);

        Assert.Equal(AdjustmentRefusal.PreviewStale, result.Refusal);
        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        Assert.Equal(100_000m, rent);
        Assert.Empty(rows);
    }

    [SkippableFact]
    public async Task AMonthChargedSinceThePreview_MovesAppliedFrom_SoTheConfirmationIsStale()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2026, 7, 5);
        var preview = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        Assert.Equal(July, preview.AppliesFrom);

        await world.MaterialiseAsync(seeded.Account, new DateOnly(2026, 7, 31));

        Assert.Equal(AdjustmentRefusal.PreviewStale, (await Desk(world, today, seeded.User).ConfirmAsync(preview)).Refusal);
        var again = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        Assert.Equal(August, again.AppliesFrom);
    }

    // ---- Test 43 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Test43_Confirmation_RecordsWhoConfirmed_AndReturnsTheFigureAndTheFirstPeriod()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2026, 7, 12);
        var preview = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);

        var result = await Desk(world, today, seeded.User).ConfirmAsync(preview);

        Assert.True(result.Succeeded);
        Assert.Equal(110_000m, result.Confirmed!.NewCanon);
        Assert.Equal(August, result.Confirmed.AppliesFrom);
        Assert.Equal(July, result.Confirmed.DueDate);
        Assert.NotNull(result.Confirmed.ConfirmedBy);

        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        var row = Assert.Single(rows);
        Assert.Equal(110_000m, rent);
        Assert.Equal(seeded.User, row.ConfirmedBy);
        Assert.Equal(August, row.AppliesFrom);
        Assert.Equal(July, row.EffectiveDate);
        Assert.Equal(AdjustmentKind.Regular, row.Kind);
    }

    [SkippableFact]
    public async Task TheHistory_ListsEachConfirmedAdjustment_TheLatestFirst_WithWhoConfirmedIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var today = new DateOnly(2027, 1, 12);
        Assert.True((await Desk(world, today, seeded.User).ConfirmAsync(await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract))).Succeeded);
        Assert.True((await Desk(world, today, seeded.User).ConfirmAsync(await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract))).Succeeded);

        var history = await Desk(world, today, seeded.User).HistoryAsync(seeded.Contract);

        Assert.Equal([January, July], history.Select(h => h.DueDate));
        Assert.Equal([121_000m, 110_000m], history.Select(h => h.NewCanon));
        Assert.Equal([February, February], history.Select(h => h.AppliesFrom));
        Assert.All(history, h => Assert.StartsWith("ajuste-", h.ConfirmedBy));
        Assert.All(history, h => Assert.Equal(new IndexPeriod(h.DueDate.AddMonths(-7).Year, h.DueDate.AddMonths(-7).Month), Assert.Single(h.Indices).BasePeriod));
    }

    private static readonly DateOnly February = new(2027, 2, 1);

    [SkippableFact]
    public async Task AContractUnderNotice_CanStillBeConfirmed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using (var notice = world.NewContext())
        {
            var contract = await notice.Contracts.SingleAsync(c => c.Id == seeded.Contract);
            contract.GiveNotice(new DateOnly(2026, 6, 1), new DateOnly(2026, 11, 30));
            await notice.SaveChangesAsync();
        }

        var today = new DateOnly(2026, 7, 12);
        var result = await Desk(world, today, seeded.User).ConfirmAsync(await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract));

        Assert.True(result.Succeeded);
    }

    [SkippableFact]
    public async Task AnEmpleado_CanConfirm()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, "inmobiliaria_empleado");
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!) { Username = role, Password = password };
        await using var asEmpleado = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);
        var today = new DateOnly(2026, 7, 12);

        var preview = await PreviewAsync(Desk(world, today, seeded.User, asEmpleado), seeded.Contract);
        var result = await Desk(world, today, seeded.User, asEmpleado).ConfirmAsync(preview);

        Assert.True(result.Succeeded, result.Refusal?.ToString());
        Assert.Equal(110_000m, (await StoredAsync(world, seeded.Contract)).MonthlyRent);
    }

    // ---- Tests 49 and 50: the difference, offered and chosen --------------------------------------------------------------

    private static readonly DateOnly LateDay = new(2026, 10, 12);
    private static readonly DateOnly November = new(2026, 11, 1);

    private static async Task<List<AccountMovement>> DifferencesAsync(DeskWorld world, Guid account)
    {
        await using var read = world.NewContext();
        var stored = await read.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == account);
        return stored.Movements.Where(m => m.Kind == MovementKind.AdjustmentDifference).ToList();
    }

    [SkippableFact]
    public async Task ALateSummary_OffersTheDifference_WithTheMonthsItCovers()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        var preview = await PreviewAsync(Desk(world, LateDay, seeded.User), seeded.Contract);

        Assert.Equal(40_000m, preview.Difference!.Amount);
        Assert.Equal([July, August, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], preview.Difference.Months);
    }

    [SkippableFact]
    public async Task Test53_ASummaryConfirmedBeforeItsDueDay_OffersNoDifference()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        Assert.Null((await PreviewAsync(Desk(world, new DateOnly(2026, 7, 5), seeded.User), seeded.Contract)).Difference);
    }

    [SkippableFact]
    public async Task Test49_TheDefault_WritesNoCharge_AndRecordsTheOfferDeclined()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var preview = await PreviewAsync(Desk(world, LateDay, seeded.User), seeded.Contract);

        var result = await Desk(world, LateDay, seeded.User).ConfirmAsync(preview);

        Assert.True(result.Succeeded);
        Assert.Empty(await DifferencesAsync(world, seeded.Account));
        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        var row = Assert.Single(rows);
        Assert.Equal(110_000m, rent);
        Assert.Equal(40_000m, row.DifferenceAmount);
        Assert.Null(row.DifferenceMovementId);
        Assert.Equal(40_000m, result.Confirmed!.DifferenceAmount);
        Assert.False(result.Confirmed.DifferenceCharged);
    }

    [SkippableFact]
    public async Task Test50_ChoosingYes_WritesOneChargeOnTheAppliedFromPeriod_LinkedFromTheAdjustment()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var preview = await PreviewAsync(Desk(world, LateDay, seeded.User), seeded.Contract);

        var result = await Desk(world, LateDay, seeded.User).ConfirmAsync(preview, chargeDifference: true);

        Assert.True(result.Succeeded, result.Refusal?.ToString());
        var charge = Assert.Single(await DifferencesAsync(world, seeded.Account));
        Assert.Equal(40_000m, charge.Amount);
        Assert.Equal(November, charge.Period);
        Assert.Equal(new DateOnly(2026, 11, 10), charge.OccurredOn);
        var row = Assert.Single((await StoredAsync(world, seeded.Contract)).Rows);
        Assert.Equal(40_000m, row.DifferenceAmount);
        Assert.Equal(charge.Id, row.DifferenceMovementId);
        Assert.True(result.Confirmed!.DifferenceCharged);

        var history = Assert.Single(await Desk(world, LateDay, seeded.User).HistoryAsync(seeded.Contract));
        Assert.Equal(40_000m, history.DifferenceAmount);
        Assert.True(history.DifferenceCharged);
    }

    /// <summary>Failing on either row's insert, the adjustment's or the charge's, proves they share one transaction.</summary>
    [SkippableTheory]
    [InlineData("rent_adjustments")]
    [InlineData("account_movements")]
    public async Task Test50_AFailureWhileWriting_LeavesNeitherTheAdjustmentNorTheCharge(string table)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var preview = await PreviewAsync(Desk(world, LateDay, seeded.User), seeded.Contract);
        var probe = new FailureProbe();
        var failing = world.NewContext(CommandHook.FailOnNth(new System.Text.RegularExpressions.Regex($@"INSERT INTO ""?{table}""?\s"), 1, probe));

        await Assert.ThrowsAnyAsync<Exception>(() => Desk(world, LateDay, seeded.User, failing).ConfirmAsync(preview, chargeDifference: true));

        Assert.True(probe.Fired);
        Assert.Empty(await DifferencesAsync(world, seeded.Account));
        var (rent, rows) = await StoredAsync(world, seeded.Contract);
        Assert.Equal(100_000m, rent);
        Assert.Empty(rows);
    }

    [SkippableFact]
    public async Task ChoosingToChargeWhenNothingIsOffered_IsRefused_AndNothingIsWritten()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var onTime = new DateOnly(2026, 7, 5);
        var preview = await PreviewAsync(Desk(world, onTime, seeded.User), seeded.Contract);

        var result = await Desk(world, onTime, seeded.User).ConfirmAsync(preview, chargeDifference: true);

        Assert.Equal(AdjustmentRefusal.NoDifferenceToCharge, result.Refusal);
        Assert.Empty((await StoredAsync(world, seeded.Contract)).Rows);
        Assert.Empty(await DifferencesAsync(world, seeded.Account));
    }

    // ---- Tests 45 and 46 ------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Tests45And46_ALateConfirmation_LeavesWrittenAndUnwrittenMonthsAtTheOldCanon()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        // July and August are on the ledger; September and October are not, when the July adjustment is confirmed on 12 October.
        await world.MaterialiseAsync(seeded.Account, new DateOnly(2026, 8, 31));
        var today = new DateOnly(2026, 10, 12);
        var preview = await PreviewAsync(Desk(world, today, seeded.User), seeded.Contract);
        Assert.Equal(new DateOnly(2026, 11, 1), preview.AppliesFrom);
        Assert.True((await Desk(world, today, seeded.User).ConfirmAsync(preview)).Succeeded);

        await world.MaterialiseAsync(seeded.Account, new DateOnly(2026, 11, 30));
        var accruals = await AccrualsAsync(world, seeded.Account);

        Assert.Equal(100_000m, accruals[July]);
        Assert.Equal(100_000m, accruals[August]);
        Assert.Equal(100_000m, accruals[new DateOnly(2026, 9, 1)]);
        Assert.Equal(100_000m, accruals[new DateOnly(2026, 10, 1)]);
        Assert.Equal(110_000m, accruals[new DateOnly(2026, 11, 1)]);
    }
}
