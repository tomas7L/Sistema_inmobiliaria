using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 13 and Decisions 15 and 32 on the forms' logic, with the port faked; and Test 40's warning.</summary>
public class ContractClauseViewModelTests
{
    private static readonly Guid Contract = Guid.NewGuid();
    private static readonly Guid Ipc = Guid.NewGuid();
    private static readonly Guid Icl = Guid.NewGuid();
    private static readonly Dictionary<Guid, string> Names = new() { [Ipc] = "IPC", [Icl] = "ICL" };

    private static ContractDetail Detail(
        ContractClauseLine? clause = null,
        ContractStatus status = ContractStatus.Active,
        EndReason? reason = null,
        DateOnly? cutover = null,
        IReadOnlyList<ContractAdjustmentLine>? adjustments = null) =>
        new(
            Contract, status, new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1), false, null, null, null, reason, cutover,
            100_000m, 10, 8m, null, [], [], clause, adjustments ?? []);

    private static readonly ContractClauseLine IpcEverySix = new(["IPC"], 6, RoundingRule.TruncateToWholePeso, [Ipc], 2);

    private static async Task<(ContractClauseViewModel ViewModel, FakeContractRegistry Port)> LoadedAsync(ContractDetail detail)
    {
        var port = new FakeContractRegistry { Detail = detail };
        var viewModel = new ContractClauseViewModel(port, Contract) { IndexNames = Names };
        await viewModel.LoadCommand.ExecuteAsync(null);
        return (viewModel, port);
    }

    // ---- The months it will read ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0", "01/07/2026", "01/2026", "07/2026")]
    [InlineData("1", "01/07/2026", "12/2025", "06/2026")]
    [InlineData("2", "01/07/2026", "11/2025", "05/2026")]
    public async Task TheForm_ShowsTheDueDateAndTheMonthsItWillRead(string offset, string due, string from, string to)
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix));

        viewModel.OffsetText = offset;

        Assert.Contains(due, viewModel.WindowText, StringComparison.Ordinal);
        Assert.Contains($"de {from} a {to}", viewModel.WindowText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMonthsShown_FollowTheCutover()
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix, cutover: new DateOnly(2026, 7, 1)));

        Assert.Contains("01/01/2027", viewModel.WindowText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "2")]
    [InlineData("x", "2")]
    [InlineData("61", "2")]
    [InlineData("6", "4")]
    [InlineData("6", "x")]
    public async Task WhileTheFieldsNameNoMonths_NoneAreShown(string interval, string offset)
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix));

        viewModel.IntervalText = interval;
        viewModel.OffsetText = offset;

        Assert.Null(viewModel.WindowText);
    }

    [Fact]
    public async Task EditingAField_NotifiesTheMonthsShown()
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix));
        var notified = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        viewModel.OffsetText = "0";
        viewModel.IntervalText = "3";
        viewModel.IndexIds.Add(Icl);

        Assert.Equal(3, notified.Count(name => name == nameof(ContractClauseViewModel.WindowText)));
        Assert.Contains(nameof(ContractClauseViewModel.SuggestedOffset), notified);
    }

    // ---- Loading and saving ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Loading_FillsTheFormWithTheClauseThere()
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix));

        Assert.Equal([Ipc], viewModel.IndexIds);
        Assert.Equal("6", viewModel.IntervalText);
        Assert.Equal("2", viewModel.OffsetText);
        Assert.True(viewModel.CanChange);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Saving_SendsWhatWasTyped()
    {
        var (viewModel, port) = await LoadedAsync(Detail(IpcEverySix));
        viewModel.IndexIds.Add(Icl);
        viewModel.IntervalText = "3";
        viewModel.OffsetText = "0";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var call = Assert.Single(port.Calls);
        Assert.Equal("SetClause", call[0]);
        Assert.Equal(Contract, call[1]);
        var clause = Assert.IsType<ClauseDraft>(call[2]);
        Assert.Equal([Ipc, Icl], clause.IndexIds);
        Assert.Equal((3, 0), (clause.IntervalMonths, clause.WindowOffsetMonths));
    }

    [Fact]
    public async Task AnOffsetLeftBlank_TakesTheSuggestionOfTheIndicesChosen_AndTheMonthsSaySo()
    {
        var (viewModel, port) = await LoadedAsync(Detail());
        viewModel.IndexIds.Add(Icl);
        viewModel.IntervalText = "6";

        Assert.Equal(0, viewModel.SuggestedOffset);
        Assert.Contains("de 01/2026 a 07/2026", viewModel.WindowText, StringComparison.Ordinal);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, Assert.IsType<ClauseDraft>(Assert.Single(port.Calls)[2]).WindowOffsetMonths);
    }

    [Fact]
    public async Task AnOffsetLeftBlank_WithNoSuggestion_TakesTodaysWindow()
    {
        var (viewModel, port) = await LoadedAsync(Detail());
        viewModel.IndexIds.Add(Guid.NewGuid());
        viewModel.IntervalText = "6";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, Assert.IsType<ClauseDraft>(Assert.Single(port.Calls)[2]).WindowOffsetMonths);
    }

    [Fact]
    public async Task ATypedOffset_WinsOverTheSuggestion()
    {
        var (viewModel, port) = await LoadedAsync(Detail());
        viewModel.IndexIds.Add(Icl);
        viewModel.IntervalText = "6";
        viewModel.OffsetText = "1";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, Assert.IsType<ClauseDraft>(Assert.Single(port.Calls)[2]).WindowOffsetMonths);
    }

    [Theory]
    [InlineData("x", "2")]
    [InlineData("6", "x")]
    [InlineData("", "")]
    public async Task WhatIsNotANumber_CallsNothing_AndShowsTheClausesText(string interval, string offset)
    {
        var (viewModel, port) = await LoadedAsync(Detail(IpcEverySix));
        viewModel.IntervalText = interval;
        viewModel.OffsetText = offset;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Calls);
        Assert.Equal(ContractRefusal.ClauseInvalid.OperatorText(), viewModel.RefusalMessage);
    }

    [Fact]
    public async Task ARefusal_ShowsItsOwnText()
    {
        var (viewModel, port) = await LoadedAsync(Detail(IpcEverySix));
        port.OnChange = () => ContractResult.Refused(ContractRefusal.AccountHasMovements);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.AccountHasMovements.OperatorText(), viewModel.RefusalMessage);
    }

    // ---- When it cannot change ----------------------------------------------------------------------------------------------

    public static TheoryData<ContractStatus, EndReason?, bool, ContractRefusal> Stopped => new()
    {
        { ContractStatus.Active, null, true, ContractRefusal.AdjustmentConfirmed },
        { ContractStatus.PendingTermination, null, true, ContractRefusal.AdjustmentConfirmed },
        { ContractStatus.Ended, EndReason.Expiry, false, ContractRefusal.ContractEnded },
        { ContractStatus.Ended, EndReason.LoadedByMistake, false, ContractRefusal.ContractCancelled },
    };

    [Theory]
    [MemberData(nameof(Stopped))]
    public async Task AContractThatCannotChange_SaysWhy_AndOffersNoSaving(
        ContractStatus status, EndReason? reason, bool adjusted, ContractRefusal expected)
    {
        var adjustments = adjusted
            ? new[] { new ContractAdjustmentLine(new DateOnly(2026, 7, 1), 100_000m, 110_000m, 1.1m, AdjustmentKind.Regular) }
            : [];

        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix, status, reason, adjustments: adjustments));

        Assert.False(viewModel.CanChange);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Equal(expected.OperatorText(), viewModel.RefusalMessage);
    }

    [Fact]
    public async Task AfterAConfirmedAdjustment_TheMonthsShown_AreTheNextDueDates()
    {
        var adjustment = new ContractAdjustmentLine(new DateOnly(2026, 7, 1), 100_000m, 110_000m, 1.1m, AdjustmentKind.Regular);

        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix, adjustments: [adjustment]));

        Assert.Contains("01/01/2027", viewModel.WindowText, StringComparison.Ordinal);
        Assert.Contains("de 05/2026 a 11/2026", viewModel.WindowText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderNotice_TheClauseCanStillChange()
    {
        var (viewModel, _) = await LoadedAsync(Detail(IpcEverySix, ContractStatus.PendingTermination));

        Assert.True(viewModel.CanChange);
    }

    // ---- The creation form --------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCreationForm_ShowsTheMonths_ForTheOffsetInForce()
    {
        var viewModel = new ContractFormViewModel(new FakeContractRegistry(), false)
        {
            ClauseIndexNames = Names,
            StartDate = new DateOnly(2026, 1, 1),
        };
        var notified = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        viewModel.ClauseIndexIds.Add(Ipc);
        viewModel.ClauseIntervalText = "6";

        Assert.Equal(2, viewModel.SuggestedClauseOffset);
        Assert.Contains("de 11/2025 a 05/2026", viewModel.ClauseWindowText, StringComparison.Ordinal);
        Assert.Contains(nameof(ContractFormViewModel.ClauseWindowText), notified);

        viewModel.ClauseOffsetText = "0";

        Assert.Contains("de 01/2026 a 07/2026", viewModel.ClauseWindowText, StringComparison.Ordinal);
    }

    // ---- Test 40: the desk's warning follows the list ------------------------------------------------------------------------

    [Fact]
    public void TheDesksWarning_DoesNotAskAboutTheStatus_SoAContractUnderNoticeWarns()
    {
        var due = new DueAdjustment(Contract, new DateOnly(2026, 7, 1), 100_000m, default, default, [], 1.1m, 110_000m);

        Assert.Equal([AdjustmentWarning.ReadyToConfirm], AdjustmentWarning.For([due], Contract));
    }
}
