using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Adjustments;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The Ajustes screen as the designer drew it (canvas "Ajustes" and "Confirmar ajuste"): every row names its unit, clause, how
/// late it is and what it would leave; the list filters by state; the indices still missing are said over it; and the summary
/// heads its contract and explains its coefficient.
/// </summary>
public class AdjustmentsScreenTests
{
    private static readonly DateOnly Today = new(2026, 7, 12);
    private static readonly Guid Ready = Guid.NewGuid();
    private static readonly Guid Waiting = Guid.NewGuid();
    private static readonly Guid AlsoWaiting = Guid.NewGuid();

    private sealed record World(AdjustmentsViewModel ViewModel, FakeAdjustmentDesk Desk);

    /// <summary>ICL every three months, due in May: two months late, and ready.</summary>
    private static DueAdjustment DueReady() =>
        new(Ready, new DateOnly(2026, 5, 1), 280_000m, new IndexPeriod(2026, 2), new IndexPeriod(2026, 5), [], 5.28m, 294_784m)
        {
            IndexNames = ["ICL"],
            IntervalMonths = 3,
        };

    /// <summary>The average of IPC and RIPTE every six months, due this month, still waiting on IPC.</summary>
    private static DueAdjustment DueWaiting(Guid contract) =>
        new(contract, new DateOnly(2026, 7, 1), 300_000m, new IndexPeriod(2026, 1), new IndexPeriod(2026, 7),
            [new MissingIndexValue(Guid.NewGuid(), "IPC", new IndexPeriod(2026, 7))], null, null)
        {
            IndexNames = ["IPC", "RIPTE"],
            IntervalMonths = 6,
            Combination = CombinationRule.Average,
        };

    private static ContractListing Listing(Guid id, string tenant, string units, ContractStatus status) =>
        new(id, tenant, "Gomez, Raúl", units, status, new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), false);

    private static async Task<World> LoadedAsync(params DueAdjustment[] due)
    {
        var worklist = new FakeDueAdjustmentQuery { Due = due.Length == 0 ? [DueReady(), DueWaiting(Waiting)] : [.. due] };
        var contracts = new FakeContractRegistry
        {
            Contracts =
            [
                Listing(Ready, "Vera, Marta", "Av. Independencia 90", ContractStatus.Active),
                Listing(Waiting, "Paz, Lucía", "Pellegrini 455", ContractStatus.PendingTermination),
                Listing(AlsoWaiting, "Sosa, Ramiro", "Rivadavia 640", ContractStatus.Active),
            ],
        };
        var desk = new FakeAdjustmentDesk();
        var viewModel = new AdjustmentsViewModel(worklist, desk, contracts, new FakeClock(Today));
        await viewModel.LoadCommand.ExecuteAsync(null);
        return new World(viewModel, desk);
    }

    /// <summary>The summary names the tenant first name first, as the port reads it; the screen heads it as the list does.</summary>
    private static AdjustmentPreview SingleIcl() =>
        new(Ready, "Marta Vera", new DateOnly(2026, 5, 1), 280_000m,
            [new AdjustmentIndexLine("ICL", new IndexPeriod(2026, 2), 20.1561m, new IndexPeriod(2026, 5), 21.2194m, 5.275326m)],
            CombinationRule.Single, 5.275326m, 294_770.91m, 294_770m, new DateOnly(2026, 8, 1));

    private static AdjustmentPreview AverageOfTwo() =>
        new(Ready, "Vera, Marta", new DateOnly(2026, 5, 1), 380_000m,
            [
                new AdjustmentIndexLine("IPC", new IndexPeriod(2026, 2), 8_049.12m, new IndexPeriod(2026, 5), 8_722.9m, 8.37m),
                new AdjustmentIndexLine("RIPTE", new IndexPeriod(2026, 2), 1_712_340.18m, new IndexPeriod(2026, 5), 1_958_644.31m, 14.38m),
            ],
            CombinationRule.Average, 11.375m, 423_225m, 423_225m, new DateOnly(2026, 8, 1));

    private static async Task<World> OpenedAsync(AdjustmentPreview preview)
    {
        var world = await LoadedAsync();
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(preview));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        await world.ViewModel.OpenCommand.ExecuteAsync(null);
        return world;
    }

    /// <summary>Canvas "Ajustes": a ready row's "Revisar" opens its summary; a waiting row's "Ir a Índices" goes to Índices.</summary>
    [Fact]
    public async Task EachRowsButton_DoesWhatItSays()
    {
        var wentToIndices = 0;
        var worklist = new FakeDueAdjustmentQuery { Due = [DueReady(), DueWaiting(Waiting)] };
        var desk = new FakeAdjustmentDesk();
        var viewModel = new AdjustmentsViewModel(
            worklist, desk, new FakeContractRegistry { Contracts = [Listing(Ready, "Vera, Marta", "Av. Independencia 90", ContractStatus.Active)] },
            new FakeClock(Today), () => wentToIndices++);
        await viewModel.LoadCommand.ExecuteAsync(null);
        desk.Previews.Enqueue(AdjustmentPreviewResult.Ready(SingleIcl()));

        await viewModel.ActCommand.ExecuteAsync(viewModel.Worklist.Single(r => r.ContractId == Waiting));

        Assert.Equal(1, wentToIndices);
        Assert.False(viewModel.IsConfirmOpen);

        await viewModel.ActCommand.ExecuteAsync(viewModel.Worklist.Single(r => r.ContractId == Ready));

        Assert.True(viewModel.IsConfirmOpen);
        Assert.Equal(1, wentToIndices);
    }

    [Fact]
    public async Task EachRow_ReadsAsTheCanvas()
    {
        var world = await LoadedAsync();

        var ready = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);
        Assert.Equal(
            ("Av. Independencia 90", "ICL", "Cada 3 meses", "Atrasado 2 meses", "quedaría $ 294.784 · +5,28 %"),
            (ready.UnitsText, ready.ClauseText, ready.EveryText, ready.LateText, ready.ProposalText));
        Assert.Equal(("Listo para confirmar", "Revisar", false), (ready.ChipText, ready.ActionText, ready.IsPendingTermination));

        var waiting = world.ViewModel.Worklist.Single(r => r.ContractId == Waiting);
        Assert.Equal(
            ("Pellegrini 455", "Promedio de IPC y RIPTE", "Cada 6 meses", "Vence este mes", "falta el índice"),
            (waiting.UnitsText, waiting.ClauseText, waiting.EveryText, waiting.LateText, waiting.ProposalText));
        Assert.Equal(("Esperando un índice", "Ir a Índices", true), (waiting.ChipText, waiting.ActionText, waiting.IsPendingTermination));
        Assert.Equal("Falta IPC de 07/2026", waiting.StatusText);
    }

    [Fact]
    public async Task OneMonthLate_IsSaidInTheSingular()
    {
        var world = await LoadedAsync(DueReady() with { DueDate = new DateOnly(2026, 6, 1) });

        Assert.Equal("Atrasado 1 mes", Assert.Single(world.ViewModel.Worklist).LateText);
    }

    /// <summary>Canvas "Ajustes": "Por confirmar" shows every row, "Listos" the ready ones, "Esperando un índice" the rest.</summary>
    [Fact]
    public async Task TheFilters_ShowTheirRows_AndCountThem()
    {
        var world = await LoadedAsync();
        Assert.Equal(AdjustmentFilter.All, world.ViewModel.Filter);
        Assert.Equal("2 contratos", world.ViewModel.CountText);

        world.ViewModel.Filter = AdjustmentFilter.Ready;
        Assert.Equal([Ready], world.ViewModel.Worklist.Select(r => r.ContractId));
        Assert.Equal("1 contrato", world.ViewModel.CountText);

        world.ViewModel.Filter = AdjustmentFilter.Waiting;
        Assert.Equal([Waiting], world.ViewModel.Worklist.Select(r => r.ContractId));

        world.ViewModel.Filter = AdjustmentFilter.All;
        Assert.Equal(2, world.ViewModel.Worklist.Count);
    }

    /// <summary>Canvas "Ajustes": the indices still missing are said over the list, once per index and month, with how many wait on it.</summary>
    [Fact]
    public async Task TheMissingIndices_AreSaidOverTheList()
    {
        var two = await LoadedAsync(DueReady(), DueWaiting(Waiting), DueWaiting(AlsoWaiting));
        Assert.Equal("2 contratos esperan el IPC de 07/2026. Mientras falte, sigue valiendo el alquiler actual.", two.ViewModel.MissingBannerText);

        var one = await LoadedAsync(DueReady(), DueWaiting(Waiting));
        Assert.Equal("1 contrato espera el IPC de 07/2026. Mientras falte, sigue valiendo el alquiler actual.", one.ViewModel.MissingBannerText);

        var none = await LoadedAsync(DueReady());
        Assert.Null(none.ViewModel.MissingBannerText);
    }

    /// <summary>Canvas "Confirmar ajuste": its own screen, headed by the contract, until going back, which forgets it.</summary>
    [Fact]
    public async Task OpeningARow_HeadsItsContract_AndBackClosesIt()
    {
        var world = await OpenedAsync(SingleIcl());

        Assert.True(world.ViewModel.IsConfirmOpen);
        Assert.Equal("Vera, Marta", world.ViewModel.OpenTenantsText);
        Assert.Equal("Vigente", world.ViewModel.OpenStatusText);
        Assert.Equal("Atrasado 2 meses", world.ViewModel.OpenLateText);
        Assert.Equal($"Av. Independencia 90 · Contrato {Ready.ToString("N")[..8]} · ICL, cada 3 meses", world.ViewModel.OpenHeaderText);

        world.ViewModel.BackCommand.Execute(null);

        Assert.False(world.ViewModel.IsConfirmOpen);
        Assert.False(world.ViewModel.IsSummaryOpen);
    }

    /// <summary>A summary refused on opening stays on the list, saying why.</summary>
    [Fact]
    public async Task ARefusedSummary_StaysOnTheList()
    {
        var world = await LoadedAsync();
        world.Desk.Previews.Enqueue(AdjustmentPreviewResult.Refused(AdjustmentRefusal.ContractEnded));
        world.ViewModel.SelectedRow = world.ViewModel.Worklist.Single(r => r.ContractId == Ready);

        await world.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(world.ViewModel.IsConfirmOpen);
        Assert.NotNull(world.ViewModel.RefusalMessage);
    }

    /// <summary>Once confirmed, the screen stays with its notice until going back.</summary>
    [Fact]
    public async Task AConfirmedAdjustment_StaysOnItsScreen_UntilGoingBack()
    {
        var preview = SingleIcl();
        var world = await OpenedAsync(preview);
        world.Desk.NextConfirm = AdjustmentConfirmResult.Done(
            new AdjustmentHistoryEntry(Guid.NewGuid(), preview.DueDate, preview.AppliesFrom, preview.PreviousCanon, preview.NewCanon,
                preview.Coefficient, AdjustmentKind.Regular, DateTimeOffset.UtcNow, "Tomás", preview.Indices));

        await world.ViewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.True(world.ViewModel.IsConfirmOpen);
        Assert.NotNull(world.ViewModel.ConfirmedMessage);
        Assert.Equal("Avise a Vera, Marta del nuevo canon. El sistema no le envía nada.", world.ViewModel.NoticeReminder);

        world.ViewModel.BackCommand.Execute(null);

        Assert.False(world.ViewModel.IsConfirmOpen);
        Assert.Null(world.ViewModel.ConfirmedMessage);
    }

    /// <summary>Canvas "Confirmar ajuste": one index's coefficient is its variation, worked out; the ICL is the 1st's value.</summary>
    [Fact]
    public async Task OneIndex_ExplainsItsVariation()
    {
        var world = await OpenedAsync(SingleIcl());

        Assert.Equal(
            "El coeficiente es la variación del ICL entre el valor base y el valor final: 21,2194 ÷ 20,1561 − 1 = 5,28 %. " +
            "El ICL es el valor del día 1 del mes. El cálculo usa todos los decimales.",
            world.ViewModel.CoefficientExplanation);
        Assert.Equal("5,28 %", world.ViewModel.CoefficientPercentText);
        Assert.Equal("$ 294.770,91", world.ViewModel.ExactCanonText);
        Assert.Equal(["+5,28 %"], world.ViewModel.SummaryLines.Select(l => l.VariationPercentText));
    }

    /// <summary>Canvas "Confirmar ajuste": an average is of the variations, never of the values, worked out.</summary>
    [Fact]
    public async Task AnAverage_ExplainsItIsOfTheVariations()
    {
        var world = await OpenedAsync(AverageOfTwo());

        Assert.Equal(
            "El coeficiente es el promedio de las variaciones de los dos índices, no de sus valores: (8,37 % + 14,38 %) ÷ 2 = 11,38 %. " +
            "El cálculo usa todos los decimales.",
            world.ViewModel.CoefficientExplanation);
    }
}
