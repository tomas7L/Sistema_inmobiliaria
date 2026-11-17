using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The contract detail's Resumen, as the designer's canvas "Detalle del contrato" shows it (task 12.3d): the title, the state's
/// note, and each card's lines, with the port faked.
/// </summary>
public class ContractDetailSummaryTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid Ipc = Guid.NewGuid();

    private static ContractDetail Detail(
        ContractStatus status = ContractStatus.Active,
        EndReason? endReason = null,
        ContractClauseLine? clause = null,
        IReadOnlyList<ContractAdjustmentLine>? adjustments = null,
        IReadOnlyList<ContractPartyLine>? parties = null,
        decimal? honorarios = 8m,
        ContractualFeePlan? feePlan = null) => new(
        ContractId, status, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), false, null, null,
        status == ContractStatus.Ended ? new DateOnly(2026, 11, 30) : null, endReason, null, 428_025m, 10, honorarios, feePlan,
        parties ??
        [
            new ContractPartyLine(Guid.NewGuid(), "Ocampo, Martín", PartyRole.Tenant),
            new ContractPartyLine(Guid.NewGuid(), "Ruiz, Elena", PartyRole.Lessor),
            new ContractPartyLine(Guid.NewGuid(), "Molina, Sofía", PartyRole.Codebtor),
        ],
        [new ContractUnitLine(Guid.NewGuid(), "Pellegrini 722", 100m)],
        clause,
        adjustments ?? []);

    private static ContractClauseLine Clause(int offset = 2) =>
        new(["IPC"], 6, RoundingRule.TruncateToWholePeso, [Ipc], offset);

    private static async Task<ContractDetailViewModel> LoadedAsync(ContractDetail detail)
    {
        var viewModel = new ContractDetailViewModel(new FakeContractRegistry { Detail = detail }, new FakeAdjustmentDesk(), ContractId);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static string Lines(IEnumerable<DetailLine> lines) => string.Join(" | ", lines.Select(l => $"{l.Label}: {l.Value}"));

    [Fact]
    public async Task TheTitle_NamesTheTenant_AndTheSubtitle_TheUnitsAndDates()
    {
        var viewModel = await LoadedAsync(Detail());

        Assert.Equal("Contrato de Ocampo, Martín", viewModel.Title);
        Assert.Equal("Pellegrini 722 · desde 01/03/2025 hasta 28/02/2027", viewModel.Subtitle);
    }

    [Fact]
    public async Task ThePartiesCard_NamesEachByItsRole_TenantFirst()
    {
        var viewModel = await LoadedAsync(Detail(parties:
        [
            new ContractPartyLine(Guid.NewGuid(), "Molina, Sofía", PartyRole.Codebtor),
            new ContractPartyLine(Guid.NewGuid(), "Ruiz, Elena", PartyRole.Lessor),
            new ContractPartyLine(Guid.NewGuid(), "Ocampo, Martín", PartyRole.Tenant),
        ]));

        Assert.Equal("Inquilino: Ocampo, Martín | Propietario: Ruiz, Elena | Codeudor: Molina, Sofía", Lines(viewModel.PartyLines));
    }

    [Fact]
    public async Task ThePropertyCard_GivesEachUnitItsShare()
    {
        var viewModel = await LoadedAsync(Detail());

        Assert.Equal("Pellegrini 722: 100 % del alquiler", Lines(viewModel.UnitLines));
    }

    [Fact]
    public async Task TheAmountsCard_ShowsRentDueDayFeesAndDates()
    {
        var viewModel = await LoadedAsync(Detail(feePlan: new ContractualFeePlan(20_000m, 3)));

        Assert.Equal(
            "Alquiler actual: $ 428.025 | Día de vencimiento: 10 | Honorarios de administración: 8 % | "
            + "Honorarios contractuales: 3 cuotas de $ 20.000 | Inicio: 01/03/2025 | Fin: 28/02/2027",
            Lines(viewModel.AmountLines));
    }

    [Fact]
    public async Task FeesNotEntered_SaySo()
    {
        var viewModel = await LoadedAsync(Detail(honorarios: null));

        Assert.Contains("Honorarios de administración: Sin cargar", Lines(viewModel.AmountLines));
        Assert.Contains("Honorarios contractuales: Sin plan", Lines(viewModel.AmountLines));
    }

    [Fact]
    public async Task TheClauseCard_ShowsItsIndexIntervalAndWindow_AndTheNextAdjustment()
    {
        var viewModel = await LoadedAsync(Detail(clause: Clause()));

        Assert.Equal("Índice: IPC | Se ajusta cada: 6 meses | Lectura termina: 2 meses antes del vencimiento", Lines(viewModel.ClauseLines));
        Assert.Equal("El próximo ajuste vence el 01/09/2025 y lee de 01/2025 a 07/2025.", viewModel.NextAdjustmentText);
    }

    [Fact]
    public async Task TheNextAdjustment_CountsFromTheLastConfirmed()
    {
        var last = new ContractAdjustmentLine(new DateOnly(2026, 9, 1), 395_000m, 428_025m, 1.083608m, AdjustmentKind.Regular);

        var viewModel = await LoadedAsync(Detail(clause: Clause(), adjustments: [last]));

        Assert.Equal("El próximo ajuste vence el 01/03/2027 y lee de 07/2026 a 01/2027.", viewModel.NextAdjustmentText);
    }

    [Fact]
    public async Task AWindowEndingOnTheDueMonth_ReadsSo()
    {
        var viewModel = await LoadedAsync(Detail(clause: Clause(offset: 0)));

        Assert.Contains("Lectura termina: en el mes del vencimiento", Lines(viewModel.ClauseLines));
    }

    [Fact]
    public async Task AContractWithoutClause_SaysItHasNone()
    {
        var viewModel = await LoadedAsync(Detail());

        Assert.Empty(viewModel.ClauseLines);
        Assert.Equal("Este contrato no tiene cláusula de ajuste.", viewModel.NextAdjustmentText);
    }

    [Theory]
    [InlineData(ContractStatus.Active, null, "")]
    [InlineData(ContractStatus.PendingTermination, null, "Con aviso de rescisión: sigue vigente hasta que se lo termine.")]
    [InlineData(ContractStatus.Ended, EndReason.Expiry, "Para editar un contrato finalizado: solo honorarios y codeudores.")]
    [InlineData(ContractStatus.Ended, EndReason.LoadedByMistake, "La cancelación no se deshace.")]
    public async Task EachState_HasItsNote(ContractStatus status, EndReason? reason, string note)
    {
        var viewModel = await LoadedAsync(Detail(status, reason));

        Assert.Equal(note, viewModel.StateNote ?? string.Empty);
    }

    [Theory]
    [InlineData(EndReason.Expiry, "El contrato terminó: su canon ya no se ajusta ni se corrige.")]
    [InlineData(EndReason.LoadedByMistake, "Contrato cancelado: no tiene ajustes.")]
    public async Task AClosedContract_HasNoNextAdjustment(EndReason reason, string text)
    {
        var viewModel = await LoadedAsync(Detail(ContractStatus.Ended, reason, Clause()));

        Assert.Equal(text, viewModel.NextAdjustmentText);
        Assert.Contains("Fin: 30/11/2026", Lines(viewModel.AmountLines));
    }

    [Fact]
    public async Task TheSummaryTab_IsShownFirst_AndTheOthersCanBeChosen()
    {
        var viewModel = await LoadedAsync(Detail());

        Assert.Equal(ContractDetailTab.Summary, viewModel.Tab);
        viewModel.Tab = ContractDetailTab.Events;
        Assert.Equal(ContractDetailTab.Events, viewModel.Tab);
    }
}
