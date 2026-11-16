using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirements 16 and 18, Tests 58 to 60, and task 12.3d: the contract detail's adjustment history,
/// with every figure the Ajustes history had, is the one place an adjustment is corrected.
/// </summary>
public class ContractAdjustmentsViewModelTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid LatestId = Guid.NewGuid();
    private static readonly DateOnly July = new(2026, 7, 1);

    private sealed record World(ContractDetailViewModel Detail, ContractAdjustmentsViewModel ViewModel, FakeAdjustmentDesk Desk, FakeContractRegistry Port);

    private static ContractDetail Detail(EndReason? reason = null) => new(
        ContractId, reason is null ? ContractStatus.Active : ContractStatus.Ended, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), false,
        null, null, null, reason, null, 110_000m, 10, 8m, null, [], [], null, []);

    private static async Task<World> LoadedAsync(EndReason? reason = null)
    {
        var desk = new FakeAdjustmentDesk
        {
            History =
            [
                new AdjustmentHistoryEntry(LatestId, July, new DateOnly(2026, 11, 1), 100_000m, 110_000m, 10m, AdjustmentKind.Regular, DateTimeOffset.UtcNow, "Tomás", [])
                {
                    DifferenceAmount = 40_000m,
                    DifferenceCharged = true,
                },
                new AdjustmentHistoryEntry(Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 90_000m, 100_000m, 11.111111m, AdjustmentKind.Regular, DateTimeOffset.UtcNow.AddMonths(-6), null, []),
            ],
        };
        var port = new FakeContractRegistry { Detail = Detail(reason) };
        var detail = new ContractDetailViewModel(port, desk, ContractId);
        await detail.LoadCommand.ExecuteAsync(null);
        return new World(detail, detail.Adjustments, desk, port);
    }

    private static AdjustmentPreview Correction(decimal? reversed = null, decimal? paidStays = null, DifferenceOffer? offer = null) =>
        new AdjustmentPreview(ContractId, "Maria Prueba", July, 100_000m, [], CombinationRule.Single, 12m, 112_000m, 112_000m, new DateOnly(2026, 12, 1), offer)
        {
            CorrectsAdjustmentId = LatestId,
            ReversedDifference = reversed,
            PaidDifferenceStays = paidStays,
        };

    private static AdjustmentHistoryEntry Written(AdjustmentPreview preview) =>
        new(Guid.NewGuid(), preview.DueDate, preview.AppliesFrom, preview.PreviousCanon, preview.NewCanon, preview.Coefficient,
            AdjustmentKind.Correction, DateTimeOffset.UtcNow, "Tomás", preview.Indices);

    private static async Task<World> CorrectingAsync(AdjustmentPreview correction)
    {
        var world = await LoadedAsync();
        world.Desk.CorrectionPreviews.Enqueue(AdjustmentPreviewResult.Ready(correction));
        await world.ViewModel.CorrectCommand.ExecuteAsync(world.ViewModel.History[0]);
        return world;
    }

    /// <summary>Task 12.3d: the detail's history carries every figure the Ajustes history had.</summary>
    [Fact]
    public async Task LoadingTheDetail_ListsItsAdjustments_WithEveryFigure()
    {
        var world = await LoadedAsync();

        Assert.Equal([ContractId], world.Desk.HistoryAsked);
        Assert.Equal(2, world.ViewModel.History.Count);
        var latest = world.ViewModel.History[0];
        Assert.Equal("01/07/2026", latest.DueDateText);
        Assert.Equal("11/2026", latest.AppliesFromText);
        Assert.Equal("$ 100.000", latest.PreviousCanonText);
        Assert.Equal("$ 110.000", latest.NewCanonText);
        Assert.Equal("10", latest.CoefficientText);
        Assert.Equal("Tomás", latest.ConfirmedByText);
        Assert.Equal("Diferencia de $ 40.000 cobrada", latest.DifferenceText);
        Assert.Equal("Sin registro", world.ViewModel.History[1].ConfirmedByText);
        Assert.False(world.ViewModel.HasNoAdjustments);
    }

    [Fact]
    public async Task OnlyTheLatestAdjustment_CanBeCorrected()
    {
        var world = await LoadedAsync();

        Assert.Equal([true, false], world.ViewModel.History.Select(h => h.CanCorrect));
        Assert.Equal(LatestId, world.ViewModel.History[0].AdjustmentId);
        Assert.True(world.ViewModel.CorrectCommand.CanExecute(world.ViewModel.History[0]));
        Assert.False(world.ViewModel.CorrectCommand.CanExecute(world.ViewModel.History[1]));
    }

    /// <summary>A cancelled contract keeps its history to read, and nothing in it is corrected.</summary>
    [Fact]
    public async Task ACancelledContract_CorrectsNothing()
    {
        var world = await LoadedAsync(EndReason.LoadedByMistake);

        Assert.Equal(2, world.ViewModel.History.Count);
        Assert.All(world.ViewModel.History, h => Assert.False(h.CanCorrect));
    }

    [Fact]
    public async Task Correcting_OpensTheCorrectionsSummary()
    {
        var world = await CorrectingAsync(Correction());

        Assert.Equal([(ContractId, LatestId)], world.Desk.CorrectionsOpened);
        Assert.Equal("$ 112.000", world.ViewModel.NewCanonText);
        Assert.Equal("$ 100.000", world.ViewModel.PreviousCanonText);
        Assert.Equal("Corrección del ajuste vencido el 01/07/2026. Se agrega como un ajuste nuevo; el anterior queda como está.", world.ViewModel.CorrectionText);
        Assert.True(world.ViewModel.IsSummaryOpen);
    }

    [Fact]
    public async Task ACorrectionsRefusal_IsShownInItsOwnWords()
    {
        var world = await LoadedAsync();
        world.Desk.CorrectionPreviews.Enqueue(AdjustmentPreviewResult.Refused(AdjustmentRefusal.NotLatest));

        await world.ViewModel.CorrectCommand.ExecuteAsync(world.ViewModel.History[0]);

        Assert.Equal("Solo se puede corregir el último ajuste del contrato. Este ajuste tiene otro posterior.", world.ViewModel.RefusalMessage);
        Assert.Null(world.ViewModel.Summary);
    }

    /// <summary>Spec Test 60.</summary>
    [Fact]
    public async Task TheSummarySaysAnUnpaidDifferenceIsReversed()
    {
        var world = await CorrectingAsync(Correction(reversed: 40_000m));

        Assert.Equal("La diferencia de $ 40.000 que cobró el ajuste corregido no se pagó: se revierte al confirmar.", world.ViewModel.DifferenceOutcomeText);
    }

    /// <summary>Spec Test 60.</summary>
    [Fact]
    public async Task TheSummarySaysAPaidDifferenceStays_AndMustBeSettledByHand()
    {
        var world = await CorrectingAsync(Correction(paidStays: 40_000m));

        Assert.Equal("La diferencia de $ 40.000 ya se pagó: queda como está y se resuelve a mano.", world.ViewModel.DifferenceOutcomeText);
        Assert.False(world.ViewModel.HasDifference);
    }

    [Fact]
    public async Task ACorrectionOfferingADifference_ShowsItWithTheChoiceAtNo()
    {
        var world = await CorrectingAsync(Correction(offer: new DifferenceOffer(50_000m, [July])));

        Assert.True(world.ViewModel.HasDifference);
        Assert.Equal("Diferencia de $ 50.000 por el mes 07/2026, ya cobrado con el canon anterior.", world.ViewModel.DifferenceText);
        Assert.False(world.ViewModel.ChargeDifference);
    }

    /// <summary>The correction goes to the desk as a correction, with the choice, and the contract and its history are read again.</summary>
    [Fact]
    public async Task ConfirmingACorrection_GoesToTheDeskAsACorrection_AndReadsTheContractAgain()
    {
        var correction = Correction(reversed: 40_000m, offer: new DifferenceOffer(50_000m, [July]));
        var world = await CorrectingAsync(correction);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(Written(correction));
        world.ViewModel.ChargeDifference = true;
        world.Port.Detail = Detail() with { MonthlyRent = 112_000m };

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal([(correction, true)], world.Desk.CorrectionsConfirmed);
        Assert.Empty(world.Desk.Confirmed);
        Assert.False(world.ViewModel.IsSummaryOpen);
        Assert.Equal("Ajuste corregido: el canon pasa a $ 112.000 desde 12/2026.", world.ViewModel.ConfirmedMessage);
        Assert.Equal("Avise a Maria Prueba del nuevo canon. El sistema no le envía nada.", world.ViewModel.NoticeReminder);
        Assert.Equal(2, world.Desk.HistoryAsked.Count);
        Assert.Equal(112_000m, world.Detail.Detail!.MonthlyRent);
    }

    /// <summary>The S11 review's R3-002: a stale correction reopens as a correction, so the next confirm goes to the same method.</summary>
    [Fact]
    public async Task AStaleCorrection_ReopensWithTheCurrentFigures()
    {
        var world = await CorrectingAsync(Correction());
        var current = Correction(reversed: 40_000m);
        world.Desk.CorrectionPreviews.Enqueue(AdjustmentPreviewResult.Ready(current));
        world.Desk.NextConfirm = AdjustmentConfirmResult.Refused(AdjustmentRefusal.PreviewStale);

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Same(current, world.ViewModel.Summary);
        Assert.Equal(2, world.Desk.CorrectionsOpened.Count);
        Assert.Empty(world.Desk.Previewed);
        Assert.Equal("Algo cambió desde que abrió el resumen. Revise el resumen actualizado antes de confirmar.", world.ViewModel.RefusalMessage);
    }

    [Fact]
    public async Task AnotherRefusalOnConfirming_ClosesTheSummary_AndSaysWhy()
    {
        var world = await CorrectingAsync(Correction());
        world.Desk.NextConfirm = AdjustmentConfirmResult.Refused(AdjustmentRefusal.NotLatest);

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.False(world.ViewModel.IsSummaryOpen);
        Assert.Equal(AdjustmentRefusal.NotLatest.OperatorText(), world.ViewModel.RefusalMessage);
    }

    [Fact]
    public async Task BackingOut_ClosesTheSummary_AndWritesNothing()
    {
        var world = await CorrectingAsync(Correction());

        world.ViewModel.CloseCommand.Execute(null);

        Assert.False(world.ViewModel.IsSummaryOpen);
        Assert.Empty(world.Desk.CorrectionsConfirmed);
    }

    [Fact]
    public async Task AContractWithoutAdjustments_SaysSo()
    {
        var desk = new FakeAdjustmentDesk();
        var detail = new ContractDetailViewModel(new FakeContractRegistry { Detail = Detail() }, desk, ContractId);

        await detail.LoadCommand.ExecuteAsync(null);

        Assert.True(detail.Adjustments.HasNoAdjustments);
    }
}
