using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// The Índices screen (gestion-de-alquileres spec Requirements 11, 12 and 18, design §6): each index with its values, a form to
/// type a level or correct one, the warning for a far level, the adjustments a correction affects, and the record corrections
/// leave, and the button that brings ICL and IPC from their official source (Requirement 25). The list is read again after every
/// write, refused or not. Both roles; the view arrives in slice S12.
/// </summary>
public sealed partial class IndicesViewModel : ObservableObject
{
    private const string LevelFormatMessage = "Escriba el nivel como se publica, por ejemplo 1.915.878,76.";
    private const string PeriodFormatMessage = "Escriba el período como mes y año, por ejemplo 08/2026.";
    private const string NoIndexMessage = "Elija un índice.";

    private static readonly string[] PeriodFormats = ["MM/yyyy", "M/yyyy"];

    private readonly IIndexRegistry _indices;
    private readonly IIndexValueSource _source;
    private readonly IAgencyClock _clock;
    private readonly IDueAdjustmentQuery _worklist;
    private bool _reloading;
    private decimal? _pendingLevel;

    public IndicesViewModel(IIndexRegistry indices, IIndexValueSource source, IAgencyClock clock, IDueAdjustmentQuery worklist)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(worklist);

        _indices = indices;
        _source = source;
        _clock = clock;
        _worklist = worklist;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        ConfirmCorrectionCommand = new AsyncRelayCommand(ConfirmCorrectionAsync, () => CanAct() && CorrectionSummary is not null);
        AcceptFarLevelCommand = new AsyncRelayCommand(AcceptFarLevelAsync, () => CanAct() && FarLevelWarning is not null);
        CorrectExistingCommand = new AsyncRelayCommand(CorrectExistingAsync, () => CanAct() && DuplicateOffer is not null);
        EditCommand = new AsyncRelayCommand<IndexValueRow>(EditAsync);
        ClearFormCommand = new RelayCommand(ClearForm);
        BringFromSourceCommand = new AsyncRelayCommand(BringFromSourceAsync, () => CanAct() && CanBringFromSource);
        ConfirmFetchedCommand = new AsyncRelayCommand<FetchedValueRow>(ConfirmFetchedAsync, row => CanAct() && row is { CanStore: true });
        CorrectFromFetchedCommand = new AsyncRelayCommand<FetchedValueRow>(CorrectFromFetchedAsync, row => CanAct() && row is { CanCorrect: true });
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>What the last press of the source button found, one row per month. Nothing here is stored until confirmed.</summary>
    public ObservableCollection<FetchedValueRow> Fetched { get; } = [];

    public ObservableCollection<IndexRow> Indices { get; } = [];

    /// <summary>The values of the selected index, the latest month first.</summary>
    public ObservableCollection<IndexValueRow> Values { get; } = [];

    /// <summary>The confirmed adjustments the pending correction would leave with a different level (spec Requirement 18).</summary>
    public ObservableCollection<AffectedAdjustmentRow> AffectedAdjustments { get; } = [];

    /// <summary>The record of the value being corrected, the most recent first (spec Decision 42).</summary>
    public ObservableCollection<IndexCorrectionRow> Corrections { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Stores a new value, or, while correcting, shows what the correction would do without writing anything.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ConfirmCorrectionCommand { get; }

    public IAsyncRelayCommand AcceptFarLevelCommand { get; }

    public IAsyncRelayCommand CorrectExistingCommand { get; }

    public IAsyncRelayCommand<IndexValueRow> EditCommand { get; }

    public IRelayCommand ClearFormCommand { get; }

    /// <summary>Asks the source for the month typed or, with none typed, for what the worklist misses of this index. Stores nothing.</summary>
    public IAsyncRelayCommand BringFromSourceCommand { get; }

    /// <summary>Stores a month the source brought, through the same checks and port as a typed one.</summary>
    public IAsyncRelayCommand<FetchedValueRow> ConfirmFetchedCommand { get; }

    /// <summary>Opens the correction of a stored level that differs from the source's. Writes nothing by itself.</summary>
    public IAsyncRelayCommand<FetchedValueRow> CorrectFromFetchedCommand { get; }

    /// <summary>False for an index with no source, which is what leaves RIPTE typed.</summary>
    public bool CanBringFromSource => SelectedIndex is { } index && _source.Supports(index.Name);

    /// <summary>A correction keeps its month: changing the month is a different value.</summary>
    public bool CanChoosePeriod => Editing is null;

    public string GuidanceText => SelectedIndex is { } index ? IndexGuidance.For(index.Name) : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuidanceText))]
    [NotifyPropertyChangedFor(nameof(CanBringFromSource))]
    [NotifyCanExecuteChangedFor(nameof(BringFromSourceCommand))]
    private IndexRow? _selectedIndex;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private string _levelText = string.Empty;

    /// <summary>The value the form is correcting, or null while it types a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoosePeriod))]
    private IndexValueRow? _editing;

    /// <summary>The old and new level of a correction waiting to be confirmed, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCorrectionCommand))]
    private string? _correctionSummary;

    /// <summary>The warning naming the earlier level, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptFarLevelCommand))]
    private string? _farLevelWarning;

    /// <summary>The value already there for the month typed, which the operator is offered to correct, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CorrectExistingCommand))]
    private IndexValueListing? _duplicateOffer;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    partial void OnSelectedIndexChanged(IndexRow? value)
    {
        Values.Clear();
        foreach (var listing in value?.Listing.Values ?? [])
        {
            Values.Add(new IndexValueRow(listing));
        }

        if (!_reloading)
        {
            Fetched.Clear();
            ClearForm();
        }
    }

    partial void OnPeriodTextChanged(string value) => WithdrawPending();

    partial void OnLevelTextChanged(string value) => WithdrawPending();

    /// <summary>A confirmation covers only what it named, so editing either field means saving again.</summary>
    private void WithdrawPending()
    {
        FarLevelWarning = null;
        DuplicateOffer = null;
        CorrectionSummary = null;
        _pendingLevel = null;
        AffectedAdjustments.Clear();
    }

    private bool CanAct() => !Runner.IsBusy;

    private void ClearForm()
    {
        PeriodText = string.Empty;
        LevelText = string.Empty;
        Editing = null;
        RefusalMessage = null;
        Corrections.Clear();
        WithdrawPending();
    }

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(() => _indices.ListAsync());
        if (!outcome.Completed)
        {
            return;
        }

        var selected = SelectedIndex?.Id;
        _reloading = true;
        try
        {
            Indices.Clear();
            foreach (var listing in outcome.Value!)
            {
                Indices.Add(new IndexRow(listing));
            }

            SelectedIndex = Indices.FirstOrDefault(index => index.Id == selected) ?? Indices.FirstOrDefault();
        }
        finally
        {
            _reloading = false;
        }

        RefreshFetched();
    }

    /// <summary>A month stored since the press stops offering to be stored, and a row can never outlive its index.</summary>
    private void RefreshFetched()
    {
        for (var i = Fetched.Count - 1; i >= 0; i--)
        {
            var row = Fetched[i];
            if (SelectedIndex?.Name != row.IndexName)
            {
                Fetched.RemoveAt(i);
                continue;
            }

            Fetched[i] = new FetchedValueRow(row.IndexName, row.Period, row.Fetch, StoredOf(SelectedIndex, row.Period));
        }
    }

    private static IndexValueListing? StoredOf(IndexRow index, IndexPeriod period) =>
        index.Listing.Values.FirstOrDefault(value => value.Period == period);

    private async Task BringFromSourceAsync()
    {
        RefusalMessage = null;
        if (SelectedIndex is not { } index || !CanBringFromSource)
        {
            return;
        }

        Fetched.Clear();
        var months = await MonthsToAskAsync(index);
        foreach (var month in months)
        {
            var outcome = await Runner.RunAsync(() => _source.FetchAsync(index.Name, month));

            // The list stays selectable while a slow source answers, and a late answer must not land on another index.
            if (!outcome.Completed || SelectedIndex?.Id != index.Id)
            {
                return;
            }

            Fetched.Add(new FetchedValueRow(index.Name, month, outcome.Value!, StoredOf(index, month)));
        }
    }

    /// <summary>
    /// The month typed or the worklist's, none of them future: that is refused when typed, and the BCRA answers such a date with an
    /// error rather than with no value. Leaves the reason on screen when there is nothing to ask.
    /// </summary>
    private async Task<IReadOnlyList<IndexPeriod>> MonthsToAskAsync(IndexRow index)
    {
        var today = _clock.Today;
        if (!string.IsNullOrWhiteSpace(PeriodText))
        {
            if (!TryParsePeriod(PeriodText, out var typed))
            {
                RefusalMessage = PeriodFormatMessage;
                return [];
            }

            if (IndexEntryRules.CheckPeriod(typed, today) is { } refusal)
            {
                RefusalMessage = refusal.OperatorText();
                return [];
            }

            return [typed];
        }

        var due = await Runner.RunAsync(() => _worklist.GetDueAsync(today));
        if (!due.Completed)
        {
            return [];
        }

        var months = due.Value!
            .SelectMany(adjustment => adjustment.Missing)
            .Where(missing => missing.EconomicIndexId == index.Id && IndexEntryRules.CheckPeriod(missing.Period, today) is null)
            .Select(missing => missing.Period)
            .Distinct()
            .Order()
            .ToList();

        if (months.Count == 0)
        {
            RefusalMessage = $"La lista de ajustes no tiene meses de {index.Name} para traer. Escriba un mes para pedirlo.";
        }

        return months;
    }

    /// <summary>
    /// The form holds what is being stored, so a far level's second confirmation reads it exactly as it reads a typed one.
    /// </summary>
    private async Task ConfirmFetchedAsync(FetchedValueRow? row)
    {
        if (row is not { CanStore: true, Level: { } level } || SelectedIndex is not { } index || row.IndexName != index.Name)
        {
            return;
        }

        RefusalMessage = null;
        Editing = null;
        Corrections.Clear();
        PeriodText = row.PeriodText;
        LevelText = row.LevelText!;
        WithdrawPending();

        await SubmitNewAsync(index.Id, row.Period, level, acceptFarLevel: false);
    }

    private Task CorrectFromFetchedAsync(FetchedValueRow? row) =>
        row is { CanCorrect: true, Stored: { } stored, LevelText: { } level }
            ? OpenAsync(new IndexValueRow(stored), level)
            : Task.CompletedTask;

    private async Task SaveAsync()
    {
        RefusalMessage = null;
        WithdrawPending();

        if (!IndexLevelParser.TryParse(LevelText, out var level))
        {
            RefusalMessage = LevelFormatMessage;
            return;
        }

        if (Editing is { } editing)
        {
            await PrepareCorrectionAsync(editing, level);
            return;
        }

        if (SelectedIndex is not { } index)
        {
            RefusalMessage = NoIndexMessage;
            return;
        }

        if (!TryParsePeriod(PeriodText, out var period))
        {
            RefusalMessage = PeriodFormatMessage;
            return;
        }

        await SubmitNewAsync(index.Id, period, level, acceptFarLevel: false);
    }

    private async Task PrepareCorrectionAsync(IndexValueRow editing, decimal level)
    {
        if (IndexEntryRules.CheckLevel(level) is { } refusal)
        {
            RefusalMessage = refusal.OperatorText();
            return;
        }

        var outcome = await Runner.RunAsync(() => _indices.ListAffectedAdjustmentsAsync(editing.Id, level));
        if (!outcome.Completed)
        {
            return;
        }

        foreach (var affected in outcome.Value!)
        {
            AffectedAdjustments.Add(new AffectedAdjustmentRow(affected));
        }

        _pendingLevel = level;
        CorrectionSummary = $"{SelectedIndex?.Name} {editing.PeriodText}: de {editing.LevelText} a {Formats.Level(level)}.";
    }

    private Task ConfirmCorrectionAsync()
    {
        RefusalMessage = null;
        return Editing is { } editing && _pendingLevel is { } level
            ? SubmitCorrectionAsync(editing.Id, level, acceptFarLevel: false)
            : Task.CompletedTask;
    }

    private async Task AcceptFarLevelAsync()
    {
        RefusalMessage = null;
        FarLevelWarning = null;

        if (Editing is { } editing && _pendingLevel is { } correction)
        {
            await SubmitCorrectionAsync(editing.Id, correction, acceptFarLevel: true);
            return;
        }

        if (SelectedIndex is { } index
            && TryParsePeriod(PeriodText, out var period)
            && IndexLevelParser.TryParse(LevelText, out var level))
        {
            await SubmitNewAsync(index.Id, period, level, acceptFarLevel: true);
        }
    }

    private async Task SubmitNewAsync(Guid indexId, IndexPeriod period, decimal level, bool acceptFarLevel)
    {
        var outcome = await Runner.RunAsync(() => _indices.AddAsync(indexId, period, level, acceptFarLevel));
        await FinishAsync(outcome);
    }

    private async Task SubmitCorrectionAsync(Guid valueId, decimal level, bool acceptFarLevel)
    {
        var outcome = await Runner.RunAsync(() => _indices.CorrectAsync(valueId, level, acceptFarLevel));
        await FinishAsync(outcome);
    }

    /// <summary>
    /// The list again, refused or not. A failure leaves the generic message on screen and the form as it was. Only a success
    /// clears the form, so a refused level is not typed twice.
    /// </summary>
    private async Task FinishAsync(OperationOutcome<IndexEntryResult> outcome)
    {
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        await ReloadAsync();

        if (result.Succeeded)
        {
            ClearForm();
            return;
        }

        var refusal = result.Refusal!.Value;
        switch (refusal)
        {
            case IndexRefusal.FarLevelNotAccepted:
                FarLevelWarning = result.Existing is { } earlier
                    ? $"{refusal.OperatorText()} El de {Formats.Period(earlier.Period)} es {Formats.Level(earlier.Level)}."
                    : refusal.OperatorText();
                return;
            case IndexRefusal.DuplicatePeriod:
                RefusalMessage = refusal.OperatorText();
                DuplicateOffer = result.Existing;
                return;
            default:
                RefusalMessage = refusal.OperatorText();
                return;
        }
    }

    /// <summary>Keeps the level already typed: it is the one the operator wants, and only the month was taken.</summary>
    private Task CorrectExistingAsync() =>
        DuplicateOffer is { } existing ? OpenAsync(new IndexValueRow(existing), LevelText) : Task.CompletedTask;

    private Task EditAsync(IndexValueRow? row) => row is null ? Task.CompletedTask : OpenAsync(row, row.LevelText);

    private async Task OpenAsync(IndexValueRow row, string levelText)
    {
        RefusalMessage = null;
        Editing = row;
        PeriodText = row.PeriodText;
        LevelText = levelText;
        WithdrawPending();
        Corrections.Clear();

        var outcome = await Runner.RunAsync(() => _indices.ListCorrectionsAsync(row.Id));
        if (!outcome.Completed)
        {
            return;
        }

        foreach (var record in outcome.Value!)
        {
            Corrections.Add(new IndexCorrectionRow(record));
        }
    }

    private static bool TryParsePeriod(string text, out IndexPeriod period)
    {
        period = default;
        if (!DateOnly.TryParseExact(text.Trim(), PeriodFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        period = new IndexPeriod(date.Year, date.Month);
        return true;
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            ConfirmCorrectionCommand.NotifyCanExecuteChanged();
            AcceptFarLevelCommand.NotifyCanExecuteChanged();
            CorrectExistingCommand.NotifyCanExecuteChanged();
            BringFromSourceCommand.NotifyCanExecuteChanged();
            ConfirmFetchedCommand.NotifyCanExecuteChanged();
            CorrectFromFetchedCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One index on the Índices screen.</summary>
public sealed class IndexRow(IndexListing listing)
{
    public IndexListing Listing { get; } = listing ?? throw new ArgumentNullException(nameof(listing));

    public Guid Id => Listing.Id;

    public string Name => Listing.Name;
}

/// <summary>One stored value of an index.</summary>
public sealed class IndexValueRow(IndexValueListing listing)
{
    public IndexValueListing Listing { get; } = listing ?? throw new ArgumentNullException(nameof(listing));

    public Guid Id => Listing.Id;

    public string PeriodText => Formats.Period(Listing.Period);

    public string LevelText => Formats.Level(Listing.Level);
}

/// <summary>A confirmed adjustment a correction affects.</summary>
public sealed class AffectedAdjustmentRow(AffectedAdjustment adjustment)
{
    public AffectedAdjustment Adjustment { get; } = adjustment ?? throw new ArgumentNullException(nameof(adjustment));

    public string TenantText => Adjustment.TenantName ?? "Sin inquilino";

    public string EffectiveDateText => Formats.Date(Adjustment.EffectiveDate);

    public string UsedLevelText => Formats.Level(Adjustment.UsedLevel);
}

/// <summary>One line of a value's correction record.</summary>
public sealed class IndexCorrectionRow(IndexCorrectionRecord record)
{
    public IndexCorrectionRecord Record { get; } = record ?? throw new ArgumentNullException(nameof(record));

    public string PreviousLevelText => Formats.Level(Record.PreviousLevel);

    public string NewLevelText => Formats.Level(Record.NewLevel);

    public string RecordedBy => Record.RecordedBy;

    public string RecordedAtText => Record.RecordedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
