using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec Tests 25 and 26 through the desk: a voided number stays and is never reused, and a voided
/// period accepts a new payment while two live payments for one period stay impossible.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionDeskVoidingTests
{
    private readonly PostgresFixture _fixture;

    public CollectionDeskVoidingTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AVoidedNumber_Stays_IsNeverReused_AndTheVoidedPairReadsAsAnnulled()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var desk = world.NewDesk();

        var first = await DeskWorld.PayAsync(desk, accountId);
        Assert.Equal(DeskWorld.StartingNumber, first.LocatarioNumber);

        await VoidAsync(desk, await world.LatestReciboIdAsync(accountId), "Cobrado por error");

        var second = await DeskWorld.PayAsync(desk, accountId);

        Assert.True(second.Succeeded, $"Refused: {second.Refusal}");
        Assert.Equal(DeskWorld.StartingNumber + 2, second.LocatarioNumber);
        Assert.Equal(DeskWorld.StartingNumber + 3, second.PropietarioNumber);

        // All four recibos are still there; the first two are annulled, with a reason and a stamp, the last two are not.
        Assert.Equal(4L, await world.CountAsync("SELECT count(*) FROM recibos"));
        Assert.Equal(
            2L,
            await world.CountAsync(
                """
                SELECT count(*) FROM recibos r JOIN recibo_annulments a ON a.recibo_id = r.id
                WHERE r.number IN (31001, 31002) AND a.reason = 'Cobrado por error' AND a.annulled_by <> ''
                """));
        Assert.Equal(2L, await world.CountAsync("SELECT count(*) FROM recibo_annulments"));
    }

    [SkippableFact]
    public async Task AVoidedPeriod_AcceptsANewPayment_ButTwoLivePaymentsForOnePeriodAreImpossible()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var desk = world.NewDesk();
        var period = DeskWorld.OldestPeriod;

        Assert.True((await DeskWorld.PayAsync(desk, accountId, period)).Succeeded);

        // Live: a second payment for the period is refused by the desk.
        var refused = await desk.PreviewAsync(new CollectionRequest(accountId, period, null, 1m));
        Assert.Equal(CollectionRefusal.PeriodAlreadySettled, refused.Refusal);

        await VoidAsync(desk, await world.LatestReciboIdAsync(accountId), "Error de carga");

        var again = await DeskWorld.PayAsync(desk, accountId, period);
        Assert.True(again.Succeeded, $"Refused: {again.Refusal}");

        Assert.Equal(
            "1,2",
            string.Join(",", await Attempts(world, accountId)));

        // The database says the same about raw SQL: attempt 2 is live, so an attempt 3 is refused.
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var (payment, recargo) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, accountId, period);
        var rejected = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), accountId, period, 3, DeskWorld.Today, payment, recargo));

        Assert.Equal("P0001", rejected.SqlState);
        Assert.Equal("collections_attempt_rule", rejected.ConstraintName);
    }

    private static async Task VoidAsync(ICollectionDesk desk, Guid reciboId, string reason)
    {
        var preview = await desk.PreviewVoidAsync(reciboId, reason, UserRole.Admin);
        Assert.True(preview.CanCommit, $"The voiding was refused: {preview.Refusal}.");
        Assert.Equal(2, preview.Annulments.Count);

        var result = await desk.VoidAsync(preview);
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}");
    }

    private static async Task<List<short>> Attempts(DeskWorld world, Guid accountId)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await using var command = new NpgsqlCommand(
            "SELECT attempt FROM collections WHERE account_id = @account ORDER BY attempt", connection);
        command.Parameters.AddWithValue("account", accountId);
        await using var reader = await command.ExecuteReaderAsync();

        var attempts = new List<short>();
        while (await reader.ReadAsync())
        {
            attempts.Add(reader.GetInt16(0));
        }

        return attempts;
    }
}
