using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec Tests 18, 15 and 16: a preview writes nothing, and a commit that fails part-way leaves nothing
/// behind, the counter included. The failure is forced by a test-only interceptor on the INSERT of the
/// recibos (design section 9), after the movements, the collection and the counter have all been
/// written inside the transaction, so a rollback that missed any of them would show.
///
/// Each test has a database of its own: the counter is one row for the whole database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionDeskAtomicityTests
{
    private readonly PostgresFixture _fixture;

    public CollectionDeskAtomicityTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task APreview_WritesNothing_NotEvenTheAccrualsItMaterialises()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var desk = world.NewDesk();
        var before = await world.CountsAsync();

        var (preview, _) = await DeskWorld.ConfirmedPreviewAsync(desk, accountId);

        Assert.Equal(DeskWorld.OldestPeriod, preview.Period);
        Assert.True(preview.LedgerFigure > 450_000m, "The preview should carry the rent and its recargo.");

        // The account had no movement at all: the periods the preview priced exist only in memory.
        var after = await world.CountsAsync();
        Assert.Equal(0L, before.Movements);
        Assert.Equal(before, after);
        Assert.Equal(DeskWorld.StartingNumber, after.NextNumber);
    }

    [SkippableFact]
    public async Task AFailure_AfterTheRecargoAndThePayment_LeavesNothingAndTheCounterUnchanged()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var before = await world.CountsAsync();

        var journal = new InsertJournal();
        var probe = new FailureProbe();
        var failing = world.NewDesk(context: world.NewContext(
            CommandHook.Recording(journal),
            CommandHook.FailOnNth(CommandHook.InsertRecibos, 1, probe)));

        var (preview, request) = await DeskWorld.ConfirmedPreviewAsync(failing, accountId);
        var failure = await Record.ExceptionAsync(() => failing.CommitAsync(preview, request));

        Assert.True(InjectedFailureException.IsIn(failure), $"The commit should have failed on the injection, not: {failure}");
        Assert.True(probe.Fired);

        // The movements and the collection really were written before the failure; they are gone.
        Assert.Contains("account_movements", journal.Tables);
        Assert.Contains("collections", journal.Tables);
        Assert.True(
            journal.Tables.ToList().IndexOf("account_movements") < journal.Tables.ToList().IndexOf("collections"),
            "The movements go in before the collection.");

        var after = await world.CountsAsync();
        Assert.Equal(before, after);
        Assert.Equal(0L, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind IN ('RecargoCharged','Payment')"));
    }

    [SkippableFact]
    public async Task AFailure_WritingThePropietarioRecibo_LeavesNothing_AndTheNextPaymentTakesTheSameNumber()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var before = await world.CountsAsync();

        var journal = new InsertJournal();
        var probe = new FailureProbe();
        var failing = world.NewDesk(context: world.NewContext(
            CommandHook.Recording(journal),
            CommandHook.FailOnNth(CommandHook.InsertRecibos, 2, probe)));

        var (preview, request) = await DeskWorld.ConfirmedPreviewAsync(failing, accountId);
        var failure = await Record.ExceptionAsync(() => failing.CommitAsync(preview, request));

        Assert.True(InjectedFailureException.IsIn(failure), $"The commit should have failed on the injection, not: {failure}");
        Assert.True(probe.Fired);

        // The locatario recibo was written, which needed the number, which the counter UPDATE had already
        // taken: so this is the proof that the raw UPDATE rolled back with the transaction (PV5).
        Assert.Contains("recibos", journal.Tables);
        Assert.Equal(before, await world.CountsAsync());

        var next = await DeskWorld.PayAsync(world.NewDesk(), accountId);

        Assert.True(next.Succeeded, $"Refused: {next.Refusal}");
        Assert.Equal(DeskWorld.StartingNumber, next.LocatarioNumber);
        Assert.Equal(DeskWorld.StartingNumber + 1, next.PropietarioNumber);
    }

    [SkippableFact]
    public async Task APayment_BeforeTheStartingNumberIsSet_IsRefusedAsCutoverNotSet_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var accountId = await world.SeedAccountAsync();
        var before = await world.CountsAsync();

        // The counter is reached last, after the movements and the collection were written; the refusal
        // has to take them back with it.
        var result = await DeskWorld.PayAsync(world.NewDesk(), accountId);

        Assert.Equal(CollectionRefusal.CutoverNotSet, result.Refusal);
        Assert.Equal(before, await world.CountsAsync());
    }
}
