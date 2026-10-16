using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Task 6.8: the database's refusals, read by SQLSTATE and constraint name (design section 2), come
/// back as the planners' own refusals, and anything unmapped is not turned into one. The table is
/// asserted whole, and two refusals are then driven through a real database end to end: a counter set
/// back (the unique index on the number) and a voiding the database refuses although the application
/// let it through.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionDeskErrorMappingTests
{
    private readonly PostgresFixture _fixture;

    public CollectionDeskErrorMappingTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string, string?, CollectionRefusal?> CollectionCases => new()
    {
        { "23505", "ux_collections_live_attempt", CollectionRefusal.ConcurrentChange },
        { "23505", "ux_account_movements_period_charge", CollectionRefusal.ConcurrentChange },
        { "23505", "ux_account_movements_instalment_period", CollectionRefusal.ConcurrentChange },
        { "23505", "ux_recibos_number", CollectionRefusal.ConcurrentChange },
        { "40P01", null, CollectionRefusal.ConcurrentChange },
        { "40P01", "anything", CollectionRefusal.ConcurrentChange },
        { "P0001", "collections_received_on_future", CollectionRefusal.ReceivedOnInFuture },
        { "P0001", "collections_received_on_bound", CollectionRefusal.ReceivedOnBeyondBound },
        { "P0001", "collections_attempt_rule", CollectionRefusal.PeriodAlreadySettled },

        // Not ours: surfaces as an error, never as a refusal.
        { "23505", "ux_something_else", null },
        { "23505", null, null },
        { "P0001", "recibo_annulments_admin_only", null },
        { "42501", null, null },
        { "23503", "fk_collections_account_id", null },
    };

    public static TheoryData<string, string?, VoidingRefusal?> VoidingCases => new()
    {
        { "23505", "ux_recibo_annulments_recibo", VoidingRefusal.AlreadyAnnulled },
        { "P0001", "recibo_annulments_admin_only", VoidingRefusal.AdminOnly },
        { "P0001", "recibo_annulments_window_closed", VoidingRefusal.WindowClosed },
        { "23505", "ux_recibos_number", null },
        { "P0001", "collections_attempt_rule", null },
        { "42501", null, null },
    };

    [Theory]
    [MemberData(nameof(CollectionCases))]
    public void ACommitError_IsMappedBySqlStateAndConstraintName(string sqlState, string? constraint, CollectionRefusal? expected) =>
        Assert.Equal(expected, DeskErrorMap.ForCollection(sqlState, constraint));

    [Theory]
    [MemberData(nameof(VoidingCases))]
    public void AVoidingError_IsMappedBySqlStateAndConstraintName(string sqlState, string? constraint, VoidingRefusal? expected) =>
        Assert.Equal(expected, DeskErrorMap.ForVoiding(sqlState, constraint));

    [SkippableFact]
    public async Task ACounterSetBack_RefusesTheNextPayment_AsAConcurrentChange_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var desk = world.NewDesk();

        Assert.True((await DeskWorld.PayAsync(desk, await world.SeedAccountAsync())).Succeeded);

        // Set back by hand with the triggers off, as a restore or a slip would (spec Test 66).
        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await ReciboTestSupport.ExecAsync(connection, transaction, "SET LOCAL session_replication_role = replica");
            await ReciboTestSupport.ExecAsync(
                connection, transaction, "UPDATE recibo_numbering SET next_number = @n WHERE id = 1", ("n", DeskWorld.StartingNumber));
            await transaction.CommitAsync();
        }

        var before = await world.CountsAsync();
        var result = await DeskWorld.PayAsync(desk, await world.SeedAccountAsync());

        Assert.Equal(CollectionRefusal.ConcurrentChange, result.Refusal);

        // Only the second payment's own accounts and nothing of the refused one: no collection, no recibo.
        var after = await world.CountsAsync();
        Assert.Equal(before.Collections, after.Collections);
        Assert.Equal(before.Recibos, after.Recibos);
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(before.NextNumber, after.NextNumber);
    }

    [SkippableFact]
    public async Task AVoiding_TheDatabaseRefuses_IsMappedToItsRefusal_AndTheCorrectionsRollBack()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var username = $"e{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, UserRole.Empleado);

        await using var asEmpleado = AccessTestSupport.BuildDbContextAs(world.Database.Context, username);
        var desk = world.NewDesk(context: asEmpleado);
        var accountId = await world.SeedAccountAsync();

        // The Empleado login itself collects: the grants of migration B reach everything the desk writes.
        var paid = await DeskWorld.PayAsync(desk, accountId);
        Assert.True(paid.Succeeded, $"Refused: {paid.Refusal}");
        Assert.Equal(
            1L,
            await world.CountAsync(
                "SELECT count(*) FROM collections WHERE account_id = @a AND recorded_by LIKE @u", ("a", accountId), ("u", $"{username}%")));

        // Age the payment to day 8 with the triggers off. Nothing in the schema lets an application do this.
        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await ReciboTestSupport.ExecAsync(connection, transaction, "SET LOCAL session_replication_role = replica");
            await ReciboTestSupport.ExecAsync(
                connection, transaction, "UPDATE collections SET recorded_at = recorded_at - interval '8 days' WHERE account_id = @a", ("a", accountId));
            await transaction.CommitAsync();
        }

        var reciboId = await world.LatestReciboIdAsync(accountId);

        // The application knows an Empleado may not void on day 8 ...
        var refusedByTheApplication = await desk.PreviewVoidAsync(reciboId, "Error", UserRole.Empleado);
        Assert.Equal(VoidingRefusal.AdminOnly, refusedByTheApplication.Refusal);

        // ... and when it is told the wrong role, the trigger says it instead.
        var preview = await desk.PreviewVoidAsync(reciboId, "Error", UserRole.Admin);
        Assert.True(preview.CanCommit);

        var before = await world.CountsAsync();
        var result = await desk.VoidAsync(preview);

        Assert.Equal(VoidingRefusal.AdminOnly, result.Refusal);
        Assert.Equal(before, await world.CountsAsync());
        Assert.Equal(0L, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'Correction'"));
    }
}
