using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Indices;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 25, Tests 82 to 85, 87 and 88, on the button's logic, with every port faked.</summary>
public class IndicesViewModelSourceTests
{
    private static readonly Guid IclId = Guid.NewGuid();
    private static readonly Guid IpcId = Guid.NewGuid();
    private static readonly Guid RipteId = Guid.NewGuid();
    private static readonly IndexPeriod June = new(2026, 6);
    private static readonly IndexPeriod July = new(2026, 7);
    private static readonly IndexPeriod August = new(2026, 8);
    private static readonly IndexValueListing StoredJune = new(Guid.NewGuid(), June, 33m);

    private sealed record World(
        IndicesViewModel ViewModel, FakeIndexRegistry Port, FakeIndexValueSource Source, FakeDueAdjustmentQuery Worklist)
    {
        public void Miss(Guid indexId, string name, IndexPeriod period) =>
            Worklist.Due.Add(new DueAdjustment(
                Guid.NewGuid(), new DateOnly(2026, 11, 1), 100_000m, period, period, [new MissingIndexValue(indexId, name, period)], null, null));
    }

    private static async Task<World> LoadedAsync(string index = "ICL")
    {
        var port = new FakeIndexRegistry
        {
            Indices =
            [
                new IndexListing(IclId, "ICL", null, [StoredJune]),
                new IndexListing(IpcId, "IPC", null, []),
                new IndexListing(RipteId, "RIPTE", null, []),
            ],
        };
        var source = new FakeIndexValueSource();
        var worklist = new FakeDueAdjustmentQuery();
        var viewModel = new IndicesViewModel(port, source, new FakeClock(new DateOnly(2026, 11, 7)), worklist);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectedIndex = viewModel.Indices.Single(i => i.Name == index);
        return new World(viewModel, port, source, worklist);
    }

    // ---- Test 88: RIPTE, and nothing requested unless pressed --------------------------------------------------------------

    [Theory]
    [InlineData("ICL", true)]
    [InlineData("IPC", true)]
    [InlineData("RIPTE", false)]
    public async Task TheButton_IsOfferedOnlyForAnIndexWithASource(string index, bool expected)
    {
        var world = await LoadedAsync(index);

        Assert.Equal(expected, world.ViewModel.CanBringFromSource);
        Assert.Equal(expected, world.ViewModel.BringFromSourceCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChoosingAnIndex_ChangesWhetherTheButtonIsOffered_AndNotifies()
    {
        var world = await LoadedAsync();
        var notified = new List<string?>();
        world.ViewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        world.ViewModel.SelectedIndex = world.ViewModel.Indices.Single(i => i.Name == "RIPTE");

        Assert.Contains(nameof(IndicesViewModel.CanBringFromSource), notified);
        Assert.False(world.ViewModel.BringFromSourceCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadingTypingSavingAndChoosing_RequestNothingOfTheSource_NorOfTheWorklist()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", July);

        world.ViewModel.SelectedIndex = world.ViewModel.Indices.Single(i => i.Name == "IPC");
        world.ViewModel.PeriodText = "08/2026";
        world.ViewModel.LevelText = "12.276,766";
        await world.ViewModel.SaveCommand.ExecuteAsync(null);
        await world.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(world.Source.Asked);
        Assert.Empty(world.Worklist.Calls);
    }

    // ---- Test 82: the missing months, a named one, nothing stored before confirming -----------------------------------------

    [Fact]
    public async Task WithNoMonthTyped_ItAsksForWhatTheWorklistMissesOfThatIndex_OldestFirst_AndEachOnce()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", August);
        world.Miss(IclId, "ICL", July);
        world.Miss(IclId, "ICL", July);
        world.Miss(IpcId, "IPC", June);
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.Source.Answers[("ICL", August)] = new IndexFetch.Found(34.9m);

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal([("ICL", July), ("ICL", August)], world.Source.Asked);
        Assert.Equal(["07/2026", "08/2026"], world.ViewModel.Fetched.Select(row => row.PeriodText));
        Assert.Equal(["34,24", "34,9"], world.ViewModel.Fetched.Select(row => row.LevelText));
        Assert.Empty(world.Port.Added);
    }

    [Fact]
    public async Task AMonthTyped_IsTheOneAsked_InsteadOfTheWorklists()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", August);
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.ViewModel.PeriodText = "07/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal([("ICL", July)], world.Source.Asked);
        Assert.Equal("34,24", Assert.Single(world.ViewModel.Fetched).LevelText);
        Assert.Empty(world.Worklist.Calls);
        Assert.Empty(world.Port.Added);
    }

    [Fact]
    public async Task NothingMissingAndNoMonthTyped_SaysSo_AndRequestsNothing()
    {
        var world = await LoadedAsync();
        world.Miss(IpcId, "IPC", July);

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Empty(world.Source.Asked);
        Assert.Empty(world.ViewModel.Fetched);
        Assert.Contains("Escriba un mes", world.ViewModel.RefusalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFutureMonthOnTheWorklist_IsNotRequested_SinceTheSourceRefusesTheDate()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", new IndexPeriod(2026, 12));
        world.Miss(IclId, "ICL", new IndexPeriod(2026, 11));

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal([("ICL", new IndexPeriod(2026, 11))], world.Source.Asked);
    }

    [Theory]
    [InlineData("12/2026", "futuro")]
    [InlineData("julio", "mes y año")]
    public async Task ATypedMonthTheFormWouldRefuse_RequestsNothing_AndShowsItsText(string period, string expected)
    {
        var world = await LoadedAsync();
        world.ViewModel.PeriodText = period;

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Empty(world.Source.Asked);
        Assert.Contains(expected, world.ViewModel.RefusalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PressingAgain_ReplacesWhatTheLastPressShowed()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.Source.Answers[("ICL", August)] = new IndexFetch.Found(34.9m);
        world.ViewModel.PeriodText = "07/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        world.ViewModel.PeriodText = "08/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal("08/2026", Assert.Single(world.ViewModel.Fetched).PeriodText);
    }

    [Fact]
    public async Task ChoosingAnotherIndex_ClearsWhatWasFound_SoAValueIsNeverConfirmedOnTheWrongIndex()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.ViewModel.PeriodText = "07/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        world.ViewModel.SelectedIndex = world.ViewModel.Indices.Single(i => i.Name == "IPC");

        Assert.Empty(world.ViewModel.Fetched);
    }

    [Fact]
    public async Task ASlowAnswer_ArrivingAfterChoosingAnotherIndex_DoesNotLandOnIt()
    {
        var world = await LoadedAsync();
        world.Source.Gate = new TaskCompletionSource();
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.ViewModel.PeriodText = "07/2026";
        var pressing = world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        world.ViewModel.SelectedIndex = world.ViewModel.Indices.Single(i => i.Name == "IPC");
        world.Source.Gate.SetResult();
        await pressing;

        Assert.Empty(world.ViewModel.Fetched);
    }

    [Fact]
    public async Task WhileTheSourceAnswers_TheCommandsWait_SoAPressCannotBeDoubled()
    {
        var world = await LoadedAsync();
        world.Source.Gate = new TaskCompletionSource();
        world.ViewModel.PeriodText = "07/2026";
        var pressing = world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.True(world.ViewModel.Runner.IsBusy);
        Assert.False(world.ViewModel.BringFromSourceCommand.CanExecute(null));

        world.Source.Gate.SetResult();
        await pressing;

        Assert.True(world.ViewModel.BringFromSourceCommand.CanExecute(null));
    }

    // ---- Test 83: a confirmed value takes the typed path ----------------------------------------------------------------------

    [Fact]
    public async Task Confirming_StoresThroughTheSamePort_WithoutAcceptingAFarLevel()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.24m);
        world.ViewModel.PeriodText = "07/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(world.ViewModel.Fetched.Single());

        Assert.Equal([(IclId, July, 34.24m, false)], world.Port.Added);
    }

    [Fact]
    public async Task AFarLevelFromTheSource_WarnsAsATypedOneDoes_AndStoresOnlyOnTheSecondConfirmation()
    {
        var world = await LoadedAsync();
        world.Port.OnAdd = (_, _, _, accepted) => accepted
            ? IndexEntryResult.Saved(new IndexValueListing(Guid.NewGuid(), July, 340m))
            : IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, StoredJune);
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(340m);
        world.ViewModel.PeriodText = "07/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(world.ViewModel.Fetched.Single());

        Assert.Contains("06/2026", world.ViewModel.FarLevelWarning, StringComparison.Ordinal);
        Assert.Equal([(IclId, July, 340m, false)], world.Port.Added);

        await world.ViewModel.AcceptFarLevelCommand.ExecuteAsync(null);

        Assert.Equal([(IclId, July, 340m, false), (IclId, July, 340m, true)], world.Port.Added);
    }

    [Fact]
    public async Task AFarLevelWithThreeDecimals_ReachesTheSecondConfirmationUnrounded()
    {
        var world = await LoadedAsync();
        world.Port.OnAdd = (_, _, level, accepted) => accepted
            ? IndexEntryResult.Saved(new IndexValueListing(Guid.NewGuid(), July, level))
            : IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, StoredJune);
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(12276.766m);
        world.ViewModel.PeriodText = "07/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(world.ViewModel.Fetched.Single());
        await world.ViewModel.AcceptFarLevelCommand.ExecuteAsync(null);

        Assert.Equal((IclId, July, 12276.766m, true), world.Port.Added[^1]);
    }

    [Fact]
    public async Task ARefusalOnConfirming_ShowsItsOwnText_AndKeepsTheOtherMonthsFound()
    {
        var world = await LoadedAsync();
        world.Port.OnAdd = (_, _, _, _) => IndexEntryResult.Refused(IndexRefusal.FuturePeriod);
        world.Miss(IclId, "ICL", July);
        world.Miss(IclId, "ICL", August);
        world.Source.Otherwise = new IndexFetch.Found(34.24m);
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(world.ViewModel.Fetched[0]);

        Assert.Equal(IndexRefusal.FuturePeriod.OperatorText(), world.ViewModel.RefusalMessage);
        Assert.Equal(2, world.ViewModel.Fetched.Count);
    }

    [Fact]
    public async Task AfterConfirming_TheRowReadsAsStored_AndCannotBeStoredAgain()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", July);
        world.Miss(IclId, "ICL", August);
        world.Source.Otherwise = new IndexFetch.Found(34.24m);
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);
        world.Port.OnAdd = (_, period, level, _) =>
        {
            var stored = new IndexValueListing(Guid.NewGuid(), period, level);
            world.Port.Indices[0] = new IndexListing(IclId, "ICL", null, [stored, StoredJune]);
            return IndexEntryResult.Saved(stored);
        };

        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(world.ViewModel.Fetched[0]);

        Assert.False(world.ViewModel.Fetched[0].CanStore);
        Assert.True(world.ViewModel.Fetched[1].CanStore);
        Assert.Single(world.Port.Added);
    }

    [Fact]
    public async Task ALevelTheTypedChecksRefuse_IsShownAsARefusal_AndOfferedToNobody()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", July)] = new IndexFetch.Found(34.1234567m);
        world.ViewModel.PeriodText = "07/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        var row = Assert.Single(world.ViewModel.Fetched);
        Assert.False(row.CanStore);
        Assert.Contains(IndexRefusal.TooManyDecimals.OperatorText(), row.Message, StringComparison.Ordinal);
    }

    // ---- Test 84 and 85: each outcome has its own message, and manual entry still works ------------------------------------------

    public static TheoryData<IndexFetch, string> Failures => new()
    {
        { new IndexFetch.Unreachable(TimedOut: false), "No se pudo conectar" },
        { new IndexFetch.Unreachable(TimedOut: true), "tardó demasiado" },
        { new IndexFetch.SourceFailed(ServerError: true), "respondió con un error" },
        { new IndexFetch.SourceFailed(ServerError: false), "no se reconoce" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EachFailure_ShowsItsOwnMessage_OffersToType_AndLeavesManualEntryWorking(IndexFetch fetch, string expected)
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", July)] = fetch;
        world.ViewModel.PeriodText = "07/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        var row = Assert.Single(world.ViewModel.Fetched);
        Assert.Contains(expected, row.Message, StringComparison.Ordinal);
        Assert.Contains("a mano", row.Message, StringComparison.Ordinal);
        Assert.True(row.IsFailure);
        Assert.False(row.CanStore);
        Assert.Null(world.ViewModel.Runner.ErrorMessage);

        world.ViewModel.LevelText = "34,24";
        await world.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal([(IclId, July, 34.24m, false)], world.Port.Added);
    }

    [Fact]
    public async Task TheFourFailureMessages_AreAllDifferent()
    {
        var world = await LoadedAsync();
        var months = new[] { June, July, August, new IndexPeriod(2026, 9) };
        var failures = Failures.Select(row => (IndexFetch)row[0]).ToArray();
        for (var i = 0; i < months.Length; i++)
        {
            world.Source.Answers[("ICL", months[i])] = failures[i];
            world.Miss(IclId, "ICL", months[i]);
        }

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal(4, world.ViewModel.Fetched.Select(row => row.Message).Distinct().Count());
    }

    [Fact]
    public async Task IpcNotThereYet_SaysItMayExistAtIndec_AndDoesNotReadAsAnError()
    {
        var world = await LoadedAsync("IPC");
        world.ViewModel.PeriodText = "09/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        var row = Assert.Single(world.ViewModel.Fetched);
        Assert.Contains("todavía no", row.Message, StringComparison.Ordinal);
        Assert.Contains("INDEC", row.Message, StringComparison.Ordinal);
        Assert.False(row.IsFailure);
        Assert.False(row.CanStore);
    }

    [Fact]
    public async Task IclNotThereYet_NamesItsOwnPublisher()
    {
        var world = await LoadedAsync();
        world.ViewModel.PeriodText = "09/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Contains("BCRA", Assert.Single(world.ViewModel.Fetched).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneMonthFailing_StillShowsTheOthers()
    {
        var world = await LoadedAsync();
        world.Miss(IclId, "ICL", July);
        world.Miss(IclId, "ICL", August);
        world.Source.Answers[("ICL", July)] = new IndexFetch.Unreachable(TimedOut: false);
        world.Source.Answers[("ICL", August)] = new IndexFetch.Found(34.9m);

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        Assert.Equal([false, true], world.ViewModel.Fetched.Select(row => row.CanStore));
    }

    // ---- Test 87: a stored level is never overwritten -------------------------------------------------------------------------

    [Fact]
    public async Task AStoredLevelThatDiffers_ShowsBoth_PointsToCorrecting_AndWritesNothing()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", June)] = new IndexFetch.Found(33.5m);
        world.ViewModel.PeriodText = "06/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        var row = Assert.Single(world.ViewModel.Fetched);
        Assert.Equal("33,5", row.LevelText);
        Assert.Equal("33", row.StoredLevelText);
        Assert.Contains("corr", row.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.CanStore);
        Assert.True(row.CanCorrect);
        Assert.False(row.IsFailure);
        Assert.False(world.ViewModel.ConfirmFetchedCommand.CanExecute(row));
        await world.ViewModel.ConfirmFetchedCommand.ExecuteAsync(row);
        Assert.Empty(world.Port.Added);
        Assert.Empty(world.Port.Corrected);
    }

    [Fact]
    public async Task AStoredLevelThatMatches_SaysSo_AndOffersNothing()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", June)] = new IndexFetch.Found(33m);
        world.ViewModel.PeriodText = "06/2026";

        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        var row = Assert.Single(world.ViewModel.Fetched);
        Assert.False(row.CanStore);
        Assert.False(row.CanCorrect);
        Assert.False(row.IsFailure);
        Assert.Contains("Ya está cargado", row.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectingFromADifferingRow_OpensTheCorrectionWithTheSourcesLevel_AndWritesNothingYet()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", June)] = new IndexFetch.Found(33.5m);
        world.ViewModel.PeriodText = "06/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.CorrectFromFetchedCommand.ExecuteAsync(world.ViewModel.Fetched.Single());

        Assert.Equal(StoredJune.Id, world.ViewModel.Editing!.Id);
        Assert.Equal("33,5", world.ViewModel.LevelText);
        Assert.Empty(world.Port.Corrected);
    }

    [Fact]
    public async Task ACorrectionFromTheSource_ReachesTheConfirmationUnrounded()
    {
        var world = await LoadedAsync();
        world.Source.Answers[("ICL", June)] = new IndexFetch.Found(12076.3937m);
        world.ViewModel.PeriodText = "06/2026";
        await world.ViewModel.BringFromSourceCommand.ExecuteAsync(null);

        await world.ViewModel.CorrectFromFetchedCommand.ExecuteAsync(world.ViewModel.Fetched.Single());
        await world.ViewModel.SaveCommand.ExecuteAsync(null);
        await world.ViewModel.ConfirmCorrectionCommand.ExecuteAsync(null);

        Assert.Equal([(StoredJune.Id, 12076.3937m, false)], world.Port.Corrected);
    }
}
