using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Adjustments;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 14 and Tests 39, 41 to 43 on the Ajustes screen's logic, with the ports faked.</summary>
public class AdjustmentsViewModelTests
{
    private static readonly DateOnly Today = new(2026, 7, 12);
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly Guid Ready = Guid.NewGuid();
    private static readonly Guid Waiting = Guid.NewGuid();
    private static readonly Guid IpcId = Guid.NewGuid();

    private sealed record World(AdjustmentsViewModel ViewModel, FakeDueAdjustmentQuery Worklist, FakeAdjustmentDesk Desk);

    private static DueAdjustment DueReady(Guid contract, DateOnly due, decimal canon) =>
        new(contract, due, canon, new IndexPeriod(2025, 12), new IndexPeriod(2026, 6), [], 10m, canon * 1.1m);

    private static DueAdjustment DueWaiting() =>
        new(Waiting, July, 200_000m, new IndexPeriod(2025, 12), new IndexPeriod(2026, 6),
            [new MissingIndexValue(IpcId, "IPC", new IndexPeriod(2026, 6))], null, null);

    private static AdjustmentPreview Preview(DateOnly appliesFrom, decimal previous = 100_000m, DateOnly? due = null) =>
        new(Ready, "Maria Prueba", due ?? July, previous,
            [
                new AdjustmentIndexLine("IPC", new IndexPeriod(2025, 12), 100m, new IndexPeriod(2026, 6), 110m, 10m),
                new AdjustmentIndexLine("RIPTE", new IndexPeriod(2025, 12), 200m, new IndexPeriod(2026, 6), 240m, 20m),
            ],
            CombinationRule.Average, 15m, previous * 1.15m + 0.5m, previous * 1.15m, appliesFrom);

    private static AdjustmentHistoryEntry Written(AdjustmentPreview preview) =>
        new(Guid.NewGuid(), preview.DueDate, preview.AppliesFrom, preview.PreviousCanon, preview.NewCanon, preview.Coefficient,
            AdjustmentKind.Regular, DateTimeOffset.UtcNow, "Tomás", preview.Indices);

    private static async Task<World> LoadedAsync()
    {
        var worklist = new FakeDueAdjustmentQuery { Due = [DueReady(Ready, July, 100_000m), DueWaiting()] };
        var contracts = new FakeContractRegistry
        {
            Contracts =
            [
                new ContractListing(Ready, "Maria Prueba", "Juan Prueba", "Alem 1", ContractStatus.Active, new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), false),
                new ContractListing(Waiting, "Ana Prueba", "Luis Prueba", "Alem 2", ContractStatus.PendingTermination, new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), false),
            ],
        };
        var desk = new FakeAdjustmentDesk();
        var viewModel = new AdjustmentsViewModel(worklist, desk, contracts, new FakeClock(Today));
        await viewModel.LoadCommand.ExecuteAsync(null);
        return new World(viewModel, worklist, desk);
    }

    private static async Task<World> OpenedAsync(AdjustmentPreview preview)
    {
        var world = await LoadedAsync();
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(preview));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        await world.ViewModel.OpenCommand.ExecuteAsync(null);
        return world;
    }

    // ---- The worklist -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheWorklist_ListsEveryDueContract_ByTenant_ForToday()
    {
        var world = await LoadedAsync();

        Assert.Equal([Today], world.Worklist.Calls);
        Assert.Equal(["Maria Prueba", "Ana Prueba"], world.ViewModel.Worklist.Select(r => r.Tenants));
        Assert.Equal(["01/07/2026", "01/07/2026"], world.ViewModel.Worklist.Select(r => r.DueDateText));
        Assert.Equal(["100.000", "200.000"], world.ViewModel.Worklist.Select(r => r.CurrentCanonText));
    }

    [Fact]
    public async Task TheWorklist_NamesTheMissingIndexAndPeriod_AndMarksTheReadyOnes()
    {
        var world = await LoadedAsync();

        var ready = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        var waiting = world.ViewModel.Worklist.Single(r => r.ContractId == Waiting);
        Assert.True(ready.IsReady);
        Assert.Equal("Listo para confirmar", ready.StatusText);
        Assert.False(waiting.IsReady);
        Assert.Equal("Falta IPC de 06/2026", waiting.StatusText);
    }

    // ---- Test 39 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test39_TheSummary_ShowsEveryFigure_BeforeConfirming()
    {
        var world = await OpenedAsync(Preview(August));
        var viewModel = world.ViewModel;

        Assert.Equal([Ready], world.Desk.Previewed);
        Assert.Equal("Maria Prueba", viewModel.TenantText);
        Assert.Equal("100.000", viewModel.PreviousCanonText);
        Assert.Equal("15", viewModel.CoefficientText);
        Assert.Equal("115.000", viewModel.NewCanonText);
        Assert.Equal("01/07/2026", viewModel.DueDateText);
        Assert.Equal("08/2026", viewModel.AppliesFromText);
        Assert.Equal(
            [("IPC", "12/2025", "100", "06/2026", "110", "10"), ("RIPTE", "12/2025", "200", "06/2026", "240", "20")],
            viewModel.SummaryLines.Select(l => (l.IndexName, l.BasePeriodText, l.BaseLevelText, l.EndPeriodText, l.EndLevelText, l.VariationText)));
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ALateSummary_SaysTheEarlierMonthsKeepThePreviousCanon()
    {
        var late = await OpenedAsync(Preview(August));
        var onTime = await OpenedAsync(Preview(July));

        Assert.Equal(
            "Se confirma tarde: el nuevo canon rige desde 08/2026. Los meses anteriores quedan con el canon anterior.",
            late.ViewModel.LateText);
        Assert.Null(onTime.ViewModel.LateText);
    }

    [Fact]
    public async Task OpeningAContractWithMissingValues_SaysWhichAreMissing_AndOffersNothingToConfirm()
    {
        var world = await LoadedAsync();
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Refused(
            AdjustmentRefusal.IndexValuesMissing, [new MissingIndexValue(IpcId, "IPC", new IndexPeriod(2026, 6))]));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Waiting);

        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal("Faltan valores de índice para calcular este ajuste. Falta IPC de 06/2026.", world.ViewModel.RefusalMessage);
        Assert.Null(world.ViewModel.Summary);
        Assert.False(world.ViewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task NothingIsConfirmable_UntilASummaryIsOpen()
    {
        var world = await LoadedAsync();

        Assert.False(world.ViewModel.ConfirmCommand.CanExecute(null));
    }

    // ---- Tests 41 and 43 ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test43_AfterConfirming_TheScreenShowsTheFigureAndFirstPeriod_AndRemindsToNotifyTheTenant()
    {
        var preview = Preview(August);
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(preview));

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Same(preview, Assert.Single(world.Desk.Confirmed));
        Assert.Equal("Ajuste confirmado: el canon pasa a 115.000 desde 08/2026.", world.ViewModel.ConfirmedMessage);
        Assert.Equal(
            "Avise a Maria Prueba del nuevo canon. El sistema no le envía nada.",
            world.ViewModel.NoticeReminder);
        Assert.Null(world.ViewModel.Summary);
        Assert.Null(world.ViewModel.RefusalMessage);
    }

    [Fact]
    public async Task Test41_AfterConfirmingTheOldest_TheWorklistIsReadAgain_AndShowsTheNext()
    {
        var preview = Preview(August);
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(preview));
        world.Worklist.Due = [DueReady(Ready, new DateOnly(2027, 1, 1), 115_000m), DueWaiting()];

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal([Today, Today], world.Worklist.Calls);
        var next = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        Assert.Equal("01/01/2027", next.DueDateText);
        Assert.Equal("115.000", next.CurrentCanonText);
    }

    [Fact]
    public async Task OpeningAnotherContract_ClearsTheLastConfirmationsMessages()
    {
        var preview = Preview(August);
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(preview));
        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Null(world.ViewModel.ConfirmedMessage);
        Assert.Null(world.ViewModel.NoticeReminder);
    }

    // ---- Test 42 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test42_AConfirmationAlreadyMade_IsReportedInItsOwnWords_AndTheListIsReadAgain()
    {
        var world = await OpenedAsync(Preview(August));
        world.Desk.NextConfirm = AdjustmentConfirmResult.Refused(AdjustmentRefusal.AlreadyConfirmed);

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Este ajuste ya fue confirmado. La lista se actualizó.", world.ViewModel.RefusalMessage);
        Assert.Null(world.ViewModel.Summary);
        Assert.Null(world.ViewModel.ConfirmedMessage);
        Assert.Null(world.ViewModel.NoticeReminder);
        Assert.Equal(2, world.Worklist.Calls.Count);
    }

    [Fact]
    public async Task AStaleSummary_IsOpenedAgain_WithTheCurrentFigures_AndNothingIsConfirmed()
    {
        var world = await OpenedAsync(Preview(July));
        world.Desk.NextConfirm = AdjustmentConfirmResult.Refused(AdjustmentRefusal.PreviewStale);
        var current = Preview(August);
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(current));

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Algo cambió desde que abrió el resumen. Revise el resumen actualizado antes de confirmar.", world.ViewModel.RefusalMessage);
        Assert.Same(current, world.ViewModel.Summary);
        Assert.Equal("08/2026", world.ViewModel.AppliesFromText);
        Assert.Null(world.ViewModel.ConfirmedMessage);
    }

    // ---- Tests 49 and 53: the difference and the choice ---------------------------------------------------------------------

    private static AdjustmentPreview WithDifference() =>
        new(Ready, "Maria Prueba", July, 100_000m, [], CombinationRule.Single, 10m, 110_000m, 110_000m, new DateOnly(2026, 11, 1),
            new DifferenceOffer(40_000m, [July, August, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)]));

    [Fact]
    public async Task ALateSummary_ShowsTheDifferenceAndItsMonths_WithTheChoiceAtNo()
    {
        var world = await OpenedAsync(WithDifference());

        Assert.True(world.ViewModel.HasDifference);
        Assert.Equal("Diferencia de 40.000 por los meses 07/2026 a 10/2026, ya cobrados con el canon anterior.", world.ViewModel.DifferenceText);
        Assert.False(world.ViewModel.ChargeDifference);
    }

    [Fact]
    public async Task Test53_ASummaryWithNoDifference_OffersNoChoice()
    {
        var world = await OpenedAsync(Preview(July));

        Assert.False(world.ViewModel.HasDifference);
        Assert.Null(world.ViewModel.DifferenceText);
    }

    [Fact]
    public async Task Test49_ConfirmingWithoutTouchingTheChoice_ChargesNothing()
    {
        var preview = WithDifference();
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(preview));

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal([false], world.Desk.ChargeChoices);
    }

    [Fact]
    public async Task ChoosingYes_IsPassedToTheDesk_AndTheMessageNamesTheCharge()
    {
        var preview = WithDifference();
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(preview) with { DifferenceAmount = 40_000m, DifferenceCharged = true });
        world.ViewModel.ChargeDifference = true;

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal([true], world.Desk.ChargeChoices);
        Assert.Equal(
            "Ajuste confirmado: el canon pasa a 110.000 desde 11/2026. Se cobra una diferencia de 40.000 con 11/2026.",
            world.ViewModel.ConfirmedMessage);
    }

    [Fact]
    public async Task OpeningAnotherSummary_PutsTheChoiceBackAtNo()
    {
        var world = await OpenedAsync(WithDifference());
        world.ViewModel.ChargeDifference = true;
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(WithDifference()));

        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(world.ViewModel.ChargeDifference);
    }

    [Fact]
    public async Task TheHistory_ShowsTheChoiceMadeOnEachDifference()
    {
        var world = await LoadedAsync();
        var entry = new AdjustmentHistoryEntry(Guid.NewGuid(), July, new DateOnly(2026, 11, 1), 100_000m, 110_000m, 10m,
            AdjustmentKind.Regular, DateTimeOffset.UtcNow, "Tomás", []);
        world.Desk.History =
        [
            entry with { DifferenceAmount = 40_000m, DifferenceCharged = true },
            entry with { DifferenceAmount = 40_000m },
            entry,
        ];
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(Preview(August)));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);

        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(["Diferencia de 40.000 cobrada", "Diferencia de 40.000 no cobrada", ""], world.ViewModel.History.Select(h => h.DifferenceText));
    }

    // ---- History ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OpeningASummary_AlsoListsTheContractsConfirmedAdjustments()
    {
        var world = await LoadedAsync();
        world.Desk.History =
        [
            new AdjustmentHistoryEntry(Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 90_000m, 100_000m, 11.111111m,
                AdjustmentKind.Regular, DateTimeOffset.UtcNow, null, []),
        ];
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(Preview(August)));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);

        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal([Ready], world.Desk.HistoryAsked);
        var row = Assert.Single(world.ViewModel.History);
        Assert.Equal("01/01/2026", row.DueDateText);
        Assert.Equal("01/2026", row.AppliesFromText);
        Assert.Equal("90.000", row.PreviousCanonText);
        Assert.Equal("100.000", row.NewCanonText);
        Assert.Equal("Sin registro", row.ConfirmedByText);
    }
}
