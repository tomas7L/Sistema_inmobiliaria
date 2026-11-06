using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Presentation.Indices;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 12 and Tests 31 to 33 and 94 on the screen's logic, with the port faked.</summary>
public class IndicesViewModelTests
{
    private static readonly Guid IpcId = Guid.NewGuid();
    private static readonly Guid RipteId = Guid.NewGuid();
    private static readonly IndexValueListing August = new(Guid.NewGuid(), new IndexPeriod(2026, 8), 9_440m);

    private static FakeIndexRegistry Port() => new()
    {
        Indices =
        [
            new IndexListing(IpcId, "IPC", null, [August]),
            new IndexListing(RipteId, "RIPTE", null, []),
        ],
    };

    private static async Task<IndicesViewModel> LoadedAsync(FakeIndexRegistry port)
    {
        var viewModel = new IndicesViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static async Task<IndicesViewModel> CorrectingAsync(FakeIndexRegistry port)
    {
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(viewModel.Values.Single());
        await viewModel.EditCommand.ExecutionTask!;
        viewModel.LevelText = "9.400";
        return viewModel;
    }

    [Fact]
    public async Task Loading_ListsTheIndices_AndSelectsTheFirstWithItsValues()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.Equal(["IPC", "RIPTE"], viewModel.Indices.Select(i => i.Name));
        Assert.Equal(IpcId, viewModel.SelectedIndex!.Id);
        var value = Assert.Single(viewModel.Values);
        Assert.Equal("08/2026", value.PeriodText);
        Assert.Equal("9.440", value.LevelText);
    }

    [Theory]
    [InlineData("IPC", "INDEC")]
    [InlineData("RIPTE", "pesos")]
    [InlineData("ICL", "BCRA")]
    public void TheForm_StatesEachIndexsScaleAndSource_AndThatTheLevelIsThatPeriods(string index, string expected)
    {
        var text = IndexGuidance.For(index);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("exactamente", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChoosingAnotherIndex_ShowsItsGuidanceAndValues_AndClearsTheForm()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.LevelText = "123";

        viewModel.SelectedIndex = viewModel.Indices.Single(i => i.Id == RipteId);

        Assert.Empty(viewModel.Values);
        Assert.Equal(string.Empty, viewModel.LevelText);
        Assert.Contains("pesos", viewModel.GuidanceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingANewLevel_ReadsTheAgencysFormat_AndClearsTheFormAndReadsAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.SelectedIndex = viewModel.Indices.Single(i => i.Id == RipteId);
        viewModel.PeriodText = "06/2026";
        viewModel.LevelText = "1.915.878,76";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal((RipteId, new IndexPeriod(2026, 6), 1_915_878.76m, false), Assert.Single(port.Added));
        Assert.Equal(string.Empty, viewModel.LevelText);
        Assert.Equal(string.Empty, viewModel.PeriodText);
        Assert.Equal(2, port.Lists);
        Assert.Equal(RipteId, viewModel.SelectedIndex!.Id);
    }

    [Theory]
    [InlineData("1915878.76")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task ALevelNotInTheAgencysFormat_CallsNothing_AndKeepsWhatWasTyped(string level)
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = "09/2026";
        viewModel.LevelText = level;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Added);
        Assert.Contains("1.915.878,76", viewModel.RefusalMessage, StringComparison.Ordinal);
        Assert.Equal(level, viewModel.LevelText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-08")]
    [InlineData("13/2026")]
    public async Task APeriodThatIsNotAMonth_CallsNothing(string period)
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = period;
        viewModel.LevelText = "9.440";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Added);
        Assert.Contains("08/2026", viewModel.RefusalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusal_ShowsItsOwnText_AndKeepsWhatWasTyped()
    {
        var port = Port();
        port.OnAdd = (_, _, _, _) => IndexEntryResult.Refused(IndexRefusal.FuturePeriod);
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = "12/2030";
        viewModel.LevelText = "9.440";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(IndexRefusal.FuturePeriod.OperatorText(), viewModel.RefusalMessage);
        Assert.Equal("12/2030", viewModel.PeriodText);
        Assert.Equal("9.440", viewModel.LevelText);
    }

    [Fact]
    public async Task ADuplicate_OffersToCorrectTheExistingValue()
    {
        var port = Port();
        port.OnAdd = (_, _, _, _) => IndexEntryResult.Refused(IndexRefusal.DuplicatePeriod, August);
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = "08/2026";
        viewModel.LevelText = "9.400";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(IndexRefusal.DuplicatePeriod.OperatorText(), viewModel.RefusalMessage);
        Assert.True(viewModel.CorrectExistingCommand.CanExecute(null));

        await viewModel.CorrectExistingCommand.ExecuteAsync(null);

        Assert.Equal(August.Id, viewModel.Editing!.Id);
        Assert.Equal("9.400", viewModel.LevelText);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal(August.Id, Assert.Single(port.CorrectionsAsked));
    }

    [Fact]
    public async Task AFarLevel_WarnsNamingTheEarlierValue_AndStoresOnlyOnSecondConfirmation()
    {
        var port = Port();
        port.OnAdd = (_, _, _, accepted) => accepted
            ? IndexEntryResult.Saved(new IndexValueListing(Guid.NewGuid(), new IndexPeriod(2026, 9), 90_000m))
            : IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, August);
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = "09/2026";
        viewModel.LevelText = "90.000";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("9.440", viewModel.FarLevelWarning, StringComparison.Ordinal);
        Assert.Contains("08/2026", viewModel.FarLevelWarning, StringComparison.Ordinal);
        Assert.Equal("90.000", viewModel.LevelText);
        Assert.True(viewModel.AcceptFarLevelCommand.CanExecute(null));

        await viewModel.AcceptFarLevelCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Added.Select(a => a.Accepted));
        Assert.Null(viewModel.FarLevelWarning);
        Assert.Equal(string.Empty, viewModel.LevelText);
    }

    [Fact]
    public async Task EditingTheLevel_WithdrawsAFarWarning_SoAConfirmationCoversOnlyTheLevelItNamed()
    {
        var port = Port();
        port.OnAdd = (_, _, _, _) => IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, August);
        var viewModel = await LoadedAsync(port);
        viewModel.PeriodText = "09/2026";
        viewModel.LevelText = "90.000";
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.LevelText = "900.000";

        Assert.Null(viewModel.FarLevelWarning);
        Assert.False(viewModel.AcceptFarLevelCommand.CanExecute(null));
    }

    // ---- Correcting -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OpeningAValue_FillsTheForm_LocksThePeriod_AndShowsItsRecord()
    {
        var port = Port();
        port.Corrections =
        [
            new IndexCorrectionRecord(9_500m, 9_440m, "tomas", new DateTimeOffset(2026, 11, 6, 13, 30, 0, TimeSpan.FromHours(-3))),
        ];
        var viewModel = await LoadedAsync(port);

        viewModel.EditCommand.Execute(viewModel.Values.Single());
        await viewModel.EditCommand.ExecutionTask!;

        Assert.Equal("08/2026", viewModel.PeriodText);
        Assert.Equal("9.440", viewModel.LevelText);
        Assert.False(viewModel.CanChoosePeriod);
        var record = Assert.Single(viewModel.Corrections);
        Assert.Equal("9.500", record.PreviousLevelText);
        Assert.Equal("9.440", record.NewLevelText);
        Assert.Equal("tomas", record.RecordedBy);
        Assert.Equal("06/11/2026 13:30", record.RecordedAtText);
    }

    [Fact]
    public async Task SavingACorrection_ShowsOldAndNewAndWhoItAffects_BeforeWritingAnything()
    {
        var port = Port();
        port.Affected =
        [
            new AffectedAdjustment(Guid.NewGuid(), Guid.NewGuid(), "Maria Prueba", new DateOnly(2026, 9, 1), 9_440m),
            new AffectedAdjustment(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 8, 1), 9_440m),
        ];
        var viewModel = await CorrectingAsync(port);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Corrected);
        Assert.Equal((August.Id, 9_400m), Assert.Single(port.AffectedAsked));
        Assert.Contains("9.440", viewModel.CorrectionSummary, StringComparison.Ordinal);
        Assert.Contains("9.400", viewModel.CorrectionSummary, StringComparison.Ordinal);
        Assert.Equal(["Maria Prueba", "Sin inquilino"], viewModel.AffectedAdjustments.Select(a => a.TenantText));
        Assert.Equal("01/09/2026", viewModel.AffectedAdjustments[0].EffectiveDateText);
        Assert.Equal("9.440", viewModel.AffectedAdjustments[0].UsedLevelText);
        Assert.True(viewModel.ConfirmCorrectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmingACorrection_WritesIt_AndClearsTheFormAndReadsAgain()
    {
        var port = Port();
        var viewModel = await CorrectingAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);

        await viewModel.ConfirmCorrectionCommand.ExecuteAsync(null);

        Assert.Equal((August.Id, 9_400m, false), Assert.Single(port.Corrected));
        Assert.Null(viewModel.Editing);
        Assert.Null(viewModel.CorrectionSummary);
        Assert.Empty(viewModel.AffectedAdjustments);
        Assert.Empty(viewModel.Corrections);
        Assert.Equal(string.Empty, viewModel.LevelText);
        Assert.Equal(2, port.Lists);
    }

    [Fact]
    public async Task AFarCorrection_WarnsAfterConfirming_AndWritesOnlyOnTheSecondConfirmation()
    {
        var port = Port();
        port.OnCorrect = (id, level, accepted) => accepted
            ? IndexEntryResult.Saved(new IndexValueListing(id, August.Period, level), 9_440m)
            : IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, August);
        var viewModel = await CorrectingAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);

        await viewModel.ConfirmCorrectionCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.FarLevelWarning);
        Assert.NotNull(viewModel.Editing);

        await viewModel.AcceptFarLevelCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Corrected.Select(c => c.Accepted));
        Assert.Null(viewModel.Editing);
    }

    [Fact]
    public async Task EditingTheLevel_WithdrawsAPendingCorrection_SoNothingOldIsConfirmed()
    {
        var port = Port();
        var viewModel = await CorrectingAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.LevelText = "9.410";

        Assert.Null(viewModel.CorrectionSummary);
        Assert.False(viewModel.ConfirmCorrectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ACorrectionARuleRefuses_CallsNothing_AndShowsItsOwnText()
    {
        var port = Port();
        var viewModel = await CorrectingAsync(port);
        viewModel.LevelText = "0";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.AffectedAsked);
        Assert.Equal(IndexRefusal.LevelNotPositive.OperatorText(), viewModel.RefusalMessage);
        Assert.False(viewModel.ConfirmCorrectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearingTheForm_LeavesCorrectingBehind()
    {
        var port = Port();
        var viewModel = await CorrectingAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.ClearFormCommand.Execute(null);

        Assert.Null(viewModel.Editing);
        Assert.True(viewModel.CanChoosePeriod);
        Assert.Equal(string.Empty, viewModel.PeriodText);
        Assert.Null(viewModel.CorrectionSummary);
        Assert.Empty(viewModel.Corrections);
    }
}
