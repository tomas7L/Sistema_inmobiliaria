using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Persistence;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec Tests 17, 31 and 70, with real sessions: each runs on a context and connection of its own, and
/// a barrier holds them at the first INSERT of the step under test so that they reach the database at
/// the same moment rather than one after the other. The previews are taken first, so every session has
/// read the same account when the race starts.
///
/// The rent already due is written before the race (as a read of the account would write it), so the
/// sessions collide on the thing the test is about and not on the accruals they would all insert.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionDeskConcurrencyTests
{
    private readonly PostgresFixture _fixture;

    public CollectionDeskConcurrencyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TwoSessions_PayingTheSamePeriod_OneSucceeds_TheOtherIsRefusedAndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        await world.MaterialiseAsync(accountId, DeskWorld.Today);

        var barrier = new SessionBarrier(2);
        var desks = Enumerable.Range(0, 2)
            .Select(_ => world.NewDesk(context: world.NewContext(CommandHook.WaitAtFirst(CommandHook.InsertCollections, barrier))))
            .ToArray();

        var confirmed = await Task.WhenAll(desks.Select(d => DeskWorld.ConfirmedPreviewAsync(d, accountId, DeskWorld.OldestPeriod)));
        var results = await Task.WhenAll(desks.Select((d, i) => Task.Run(() => d.CommitAsync(confirmed[i].Preview, confirmed[i].Request))));

        var winner = Assert.Single(results, r => r.Succeeded);
        var loser = Assert.Single(results, r => !r.Succeeded);

        Assert.Equal(CollectionRefusal.ConcurrentChange, loser.Refusal);
        Assert.Equal(DeskWorld.StartingNumber, winner.LocatarioNumber);

        // One payment's worth of rows, and the counter moved once.
        Assert.Equal(1L, await world.CountAsync("SELECT count(*) FROM collections"));
        Assert.Equal(1L, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'Payment'"));
        Assert.Equal(2L, await world.CountAsync("SELECT count(*) FROM recibos"));
        Assert.Equal(DeskWorld.StartingNumber + 2, (await world.CountsAsync()).NextNumber);
    }

    [SkippableFact]
    public async Task SeveralSessions_Issuing_AtOnce_FormOneUnbrokenRun_WithEachPairAdjacent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        const int Sessions = 5;

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var barrier = new SessionBarrier(Sessions);
        var tasks = new List<Task<CollectionResult>>();

        // One account each, so the only thing they share is the counter.
        for (var i = 0; i < Sessions; i++)
        {
            var accountId = await world.SeedAccountAsync();
            await world.MaterialiseAsync(accountId, DeskWorld.Today);

            var desk = world.NewDesk(context: world.NewContext(CommandHook.WaitAtFirst(CommandHook.InsertCollections, barrier)));
            var (preview, request) = await DeskWorld.ConfirmedPreviewAsync(desk, accountId, DeskWorld.OldestPeriod);
            tasks.Add(Task.Run(() => desk.CommitAsync(preview, request)));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Succeeded, $"Refused: {r.Refusal}"));

        // 2 * Sessions numbers, no gap, none twice.
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var numbers = await NumbersAsync(connection);

        Assert.Equal(
            Enumerable.Range(DeskWorld.StartingNumber, 2 * Sessions),
            numbers.Select(n => n.Number));

        // Each payment's two recibos are adjacent, the locatario first, on an even offset from the start.
        Assert.All(results, r =>
        {
            Assert.Equal(0, (r.LocatarioNumber!.Value - DeskWorld.StartingNumber) % 2);
            Assert.Equal(r.LocatarioNumber + 1, r.PropietarioNumber);
        });
        Assert.All(
            numbers.Chunk(2),
            pair =>
            {
                Assert.Equal("Locatario", pair[0].Copy);
                Assert.Equal("Propietario", pair[1].Copy);
                Assert.Equal(pair[0].Collection, pair[1].Collection);
            });
        Assert.Equal(DeskWorld.StartingNumber + (2 * Sessions), (await world.CountsAsync()).NextNumber);
    }

    [SkippableFact]
    public async Task AnEarlyPayment_RacingTheLazyAccrual_LeavesOneAccrual_AndTheLoserWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        // A period begun but not yet due needs a due day that has not arrived: the last day of the month
        // (due day 31 clamps), on a day before it.
        var today = DeskWorld.Today;
        var lastDay = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var clockDay = today == lastDay ? today.AddDays(-1) : today;
        var period = DeskWorld.FirstOfMonth(clockDay);

        await using var world = await DeskWorld.CreateAsync(_fixture);

        // Repeated, because which side wins the race is not in the test's hands; each outcome has to hold.
        // Alternate rounds carry an instalment plan and the others none, so that the unique index on the
        // rent charge is the only thing standing between the sessions in the rounds without one: with a
        // plan, the instalment's own index would refuse the loser's batch even if the rent's did not.
        for (var round = 0; round < 6; round++)
        {
            ContractualFeePlan? plan = round % 2 == 0 ? new ContractualFeePlan(10_000m, 3) : null;
            var expectedInstalments = plan is null ? 0L : 1L;
            var accountId = await world.SeedAccountAsync(period.AddMonths(-2), dueDay: 31, plan: plan);
            await world.MaterialiseAsync(accountId, clockDay);

            var barrier = new SessionBarrier(2);
            var payingDesk = world.NewDesk(
                new FixedClock(clockDay), world.NewContext(CommandHook.WaitAtFirst(CommandHook.InsertMovements, barrier)));
            var accruingContext = world.NewContext(CommandHook.WaitAtFirst(CommandHook.InsertMovements, barrier));

            var (preview, request) = await DeskWorld.ConfirmedPreviewAsync(payingDesk, accountId, period);
            Assert.True(preview.PeriodWasEarly, "The period should be begun and not yet due.");

            var paying = Task.Run(() => payingDesk.CommitAsync(preview, request));
            var accruing = Task.Run(() =>
                new AccountMaterialiser(accruingContext).MaterialiseDueAccrualsThroughAsync(accountId, lastDay));

            var payment = await paying;
            var written = await accruing;

            var accruals = await world.CountAsync(
                "SELECT count(*) FROM account_movements WHERE account_id = @a AND period = @p AND kind = 'RentAccrual'",
                ("a", accountId), ("p", period));
            var instalments = await world.CountAsync(
                "SELECT count(*) FROM account_movements WHERE account_id = @a AND period = @p AND kind = 'ContractualFeeInstalment'",
                ("a", accountId), ("p", period));
            var payments = await world.CountAsync(
                "SELECT count(*) FROM account_movements WHERE account_id = @a AND period = @p AND kind = 'Payment'",
                ("a", accountId), ("p", period));
            var collections = await world.CountAsync("SELECT count(*) FROM collections WHERE account_id = @a", ("a", accountId));

            Assert.Equal(1L, accruals);
            Assert.Equal(expectedInstalments, instalments);

            if (payment.Succeeded)
            {
                Assert.Equal(0, written);
                Assert.Equal(1L, payments);
                Assert.Equal(1L, collections);
            }
            else
            {
                Assert.Equal(CollectionRefusal.ConcurrentChange, payment.Refusal);
                Assert.Equal(plan is null ? 1 : 2, written);
                Assert.Equal(0L, payments);
                Assert.Equal(0L, collections);
            }
        }
    }

    private sealed record Issued(int Number, string Copy, Guid Collection);

    private static async Task<List<Issued>> NumbersAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT number, copy, collection_id FROM recibos ORDER BY number", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var issued = new List<Issued>();
        while (await reader.ReadAsync())
        {
            issued.Add(new Issued(reader.GetInt32(0), reader.GetString(1), reader.GetGuid(2)));
        }

        return issued;
    }
}
