using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// The Índices screen (gestion-de-alquileres spec Requirements 11, 12 and 18, design §6): each index with its values, a form to
/// type a level or correct one, the warning for a far level, the adjustments a correction affects, and the record corrections
/// leave. The list is read again after every write, refused or not. Both roles; the view arrives in slice S12.
/// </summary>
public sealed partial class IndicesViewModel : ObservableObject
{
    private const string LevelFormatMessage = "Escriba el nivel como se publica, por ejemplo 1.915.878,76.";
    private const string PeriodFormatMessage = "Escriba el período como mes y año, por ejemplo 08/2026.";
    private const string NoIndexMessage = "Elija un índice.";

    private static readonly string[] PeriodFormats = ["MM/yyyy", "M/yyyy"];

    private readonly IIndexRegistry _indices;
    private bool _reloading;
    private decimal? _pendingLevel;

    public IndicesViewModel(IIndexRegistry indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        _indices = indices;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        ConfirmCorrectionCommand = new AsyncRelayCommand(ConfirmCorrectionAsync, () => CanAct() && CorrectionSummary is not null);
        AcceptFarLevelCommand = new AsyncRelayCommand(AcceptFarLevelAsync, () => CanAct() && FarLevelWarning is not null);
        CorrectExistingCommand = new AsyncRelayCommand(CorrectExistingAsync, () => CanAct() && DuplicateOffer is not null);
        EditCommand = new AsyncRelayCommand<IndexValueRow>(EditAsync);
        ClearFormCommand = new RelayCommand(ClearForm);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

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

    /// <summary>A correction keeps its month: changing the month is a different value.</summary>
    public bool CanChoosePeriod => Editing is null;

    public string GuidanceText => SelectedIndex is { } index ? IndexGuidance.For(index.Name) : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuidanceText))]
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
    }

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
