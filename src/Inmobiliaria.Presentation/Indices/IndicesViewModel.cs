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
/// The Índices screen (gestion-de-alquileres spec Requirements 11, 12, 18, 25 and 30, design §6): each index with its values, a
/// form to type a level or correct one, the warning for a far level, the adjustments a correction affects, and the record
/// corrections leave, and the panel that brings ICL and IPC from their official source. The list is read again after every
/// write, refused or not. Both roles. As the designer drew it (canvas "Índices"), the form's own errors show beside their
/// fields, all at once (Decision 44), and a write that lands leaves a notice above the form.
/// </summary>
public sealed partial class IndicesViewModel : ObservableObject
{
    public const string LevelFormatMessage = "Escriba el nivel como se publica, por ejemplo 1.915.878,76.";
    public const string PeriodFormatMessage = "Escriba el período como mes y año, por ejemplo 08/2026.";
    public const string UnchangedMessage = "El valor no cambió: no hay nada que guardar.";
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
        BackToEditingCommand = new RelayCommand(WithdrawPending);
        OpenBringCommand = new RelayCommand(OpenBring, () => CanBringFromSource);
        CloseBringCommand = new RelayCommand(CloseBring);
        DismissDoneCommand = new RelayCommand(DismissDone);
        BringFromSourceCommand = new AsyncRelayCommand(BringFromSourceAsync, () => CanAct() && CanBringFromSource);
        ConfirmFetchedCommand = new AsyncRelayCommand<FetchedValueRow>(ConfirmFetchedAsync, row => CanAct() && row is { CanStore: true });
        CorrectFromFetchedCommand = new AsyncRelayCommand<FetchedValueRow>(CorrectFromFetchedAsync, row => CanAct() && row is { CanCorrect: true });
        Runner.PropertyChanged += OnRunnerChanged;
        Fetched.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStorableFetched));
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>What the last press of the source button found, one row per month. Nothing here is stored until confirmed.</summary>
    public ObservableCollection<FetchedValueRow> Fetched { get; } = [];

    public ObservableCollection<IndexRow> Indices { get; } = [];

    /// <summary>The values of the selected index, the latest month first.</summary>
    public ObservableCollection<IndexValueRow> Values { get; } = [];

    /// <summary>The confirmed adjustments the pending correction would leave with a different level (spec Requirement 18).</summary>
    public ObservableCollection<AffectedAdjustmentRow> AffectedAdjustments { get; } = [];

    /// <summary>The adjustments the correction just saved affects, kept on its notice once the form is cleared.</summary>
    public ObservableCollection<AffectedAdjustmentRow> DoneAdjustments { get; } = [];

    /// <summary>The record of the value being corrected, the most recent first (spec Decision 42).</summary>
    public ObservableCollection<IndexCorrectionRow> Corrections { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Stores a new value, or, while correcting, shows what the correction would do without writing anything.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ConfirmCorrectionCommand { get; }

    public IAsyncRelayCommand AcceptFarLevelCommand { get; }

    public IAsyncRelayCommand CorrectExistingCommand { get; }

    public IAsyncRelayCommand<IndexValueRow> EditCommand { get; }

    /// <summary>"Cancelar": the form goes back to typing a new value.</summary>
    public IRelayCommand ClearFormCommand { get; }

    /// <summary>"Revisar" and "Volver a editar": the warning or the summary goes, the form stays as typed.</summary>
    public IRelayCommand BackToEditingCommand { get; }

    /// <summary>"Traer valores": opens the panel that asks the source.</summary>
    public IRelayCommand OpenBringCommand { get; }

    /// <summary>"Cerrar" and "Descartar": closes the panel, storing nothing.</summary>
    public IRelayCommand CloseBringCommand { get; }

    /// <summary>"Listo": closes the notice of a saved correction.</summary>
    public IRelayCommand DismissDoneCommand { get; }

    /// <summary>
    /// Asks the source for the month typed in the panel or, with none typed, for what the worklist misses of this index. Stores
    /// nothing.
    /// </summary>
    public IAsyncRelayCommand BringFromSourceCommand { get; }

    /// <summary>Stores a month the source brought, through the same checks and port as a typed one.</summary>
    public IAsyncRelayCommand<FetchedValueRow> ConfirmFetchedCommand { get; }

    /// <summary>Opens the correction of a stored level that differs from the source's. Writes nothing by itself.</summary>
    public IAsyncRelayCommand<FetchedValueRow> CorrectFromFetchedCommand { get; }

    /// <summary>True while a month the source brought waits to be stored, so "nothing was saved yet" is worth saying.</summary>
    public bool HasStorableFetched => Fetched.Any(row => row.CanStore);

    /// <summary>False for an index with no source, which is what leaves RIPTE typed.</summary>
    public bool CanBringFromSource => SelectedIndex is { } index && _source.Supports(index.Name);

    /// <summary>A correction keeps its month: changing the month is a different value.</summary>
    public bool CanChoosePeriod => Editing is null;

    public bool IsEditing => Editing is not null;

    public string GuidanceText => SelectedIndex is { } index ? IndexGuidance.For(index.Name) : string.Empty;

    /// <summary>The note under the form: what a level is, and which months this index takes (spec Decision 53).</summary>
    public string FormHint => SelectedIndex is { } index ? IndexGuidance.FormHint(index.Name) : string.Empty;

    /// <summary>"$" for an index published in pesos, shown before its level; empty otherwise.</summary>
    public string LevelUnit => SelectedIndex is { } index ? IndexGuidance.Unit(index.Name) : string.Empty;

    public string SourceName => SelectedIndex is { } index ? IndexGuidance.Source(index.Name) : string.Empty;

    /// <summary>What the level field shows before the number, as the canvas: "$" for pesos, "#" for an index number.</summary>
    public string LevelPrefix => LevelUnit.Length == 0 ? "#" : LevelUnit;

    public string FormTitle => Editing is null ? "Cargar un valor" : "Corregir un valor";

    public string SaveText => Editing is null ? "Agregar valor" : "Revisar corrección";

    /// <summary>The foot of the values: how many there are.</summary>
    public string CountText => Values.Count == 1 ? "1 valor" : $"{Values.Count} valores";

    /// <summary>
    /// The level field's example, as the canvas: the index's latest stored level, so each index shows a figure of its own size
    /// (ICL is in the tens, RIPTE in the millions). With no value stored yet, a general one.
    /// </summary>
    public string LevelExample => Values.MaxBy(row => row.Listing.Period) is { } latest ? $"Por ejemplo: {latest.LevelText}" : "Por ejemplo: 1.915.878,76";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuidanceText), nameof(FormHint), nameof(LevelUnit), nameof(LevelPrefix), nameof(SourceName), nameof(CanBringFromSource))]
    [NotifyCanExecuteChangedFor(nameof(BringFromSourceCommand), nameof(OpenBringCommand))]
    private IndexRow? _selectedIndex;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private string _levelText = string.Empty;

    /// <summary>The period's error from the last save, beside the field (spec Decision 44), or null.</summary>
    [ObservableProperty]
    private string? _periodError;

    /// <summary>The level's error from the last save, beside the field (spec Decision 44), or null.</summary>
    [ObservableProperty]
    private string? _levelError;

    /// <summary>True while the panel that asks the source is open.</summary>
    [ObservableProperty]
    private bool _isBringOpen;

    /// <summary>The month the panel asks for, or blank for the worklist's.</summary>
    [ObservableProperty]
    private string _bringPeriodText = string.Empty;

    /// <summary>The value the form is correcting, or null while it types a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoosePeriod), nameof(IsEditing), nameof(FormTitle), nameof(SaveText))]
    private IndexValueRow? _editing;

    /// <summary>The old and new level of a correction waiting to be confirmed, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCorrectionCommand))]
    private string? _correctionSummary;

    /// <summary>"Confirme la corrección de IPC 08/2026", over the summary.</summary>
    [ObservableProperty]
    private string? _correctionTitle;

    [ObservableProperty]
    private string? _correctionOldText;

    [ObservableProperty]
    private string? _correctionNewText;

    /// <summary>The warning naming the earlier level, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptFarLevelCommand))]
    private string? _farLevelWarning;

    /// <summary>"Valor anterior (07/2026)", beside its level, while the far level is asked.</summary>
    [ObservableProperty]
    private string? _farPreviousLabel;

    [ObservableProperty]
    private string? _farPreviousLevel;

    /// <summary>"Valor que escribió (08/2026)", beside its level, while the far level is asked.</summary>
    [ObservableProperty]
    private string? _farTypedLabel;

    [ObservableProperty]
    private string? _farTypedLevel;

    /// <summary>The value already there for the month typed, which the operator is offered to correct, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CorrectExistingCommand))]
    private IndexValueListing? _duplicateOffer;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>What the last new value that landed says, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _savedMessage;

    /// <summary>The notice of the correction just saved, or null until "Listo" or the next action.</summary>
    [ObservableProperty]
    private string? _correctionDone;

    partial void OnSelectedIndexChanged(IndexRow? value)
    {
        Values.Clear();
        var unit = value is null ? string.Empty : IndexGuidance.Unit(value.Name);
        foreach (var listing in value?.Listing.Values ?? [])
        {
            Values.Add(new IndexValueRow(listing, unit));
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(LevelExample));

        if (!_reloading)
        {
            Fetched.Clear();
            IsBringOpen = false;
            BringPeriodText = string.Empty;
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

    private void ClearMessages()
    {
        RefusalMessage = null;
        SavedMessage = null;
        CorrectionDone = null;
        DoneAdjustments.Clear();
    }

    private void ClearForm()
    {
        PeriodText = string.Empty;
        LevelText = string.Empty;
        Editing = null;
        PeriodError = null;
        LevelError = null;
        ClearMessages();
        Corrections.Clear();
        WithdrawPending();
    }

    private void OpenBring()
    {
        ClearMessages();
        IsBringOpen = true;
    }

    private void CloseBring()
    {
        IsBringOpen = false;
        Fetched.Clear();
    }

    private void DismissDone()
    {
        CorrectionDone = null;
        DoneAdjustments.Clear();
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
        ClearMessages();
        if (SelectedIndex is not { } index || !CanBringFromSource)
        {
            return;
        }

        IsBringOpen = true;
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
    /// The month typed in the panel or the worklist's, none that cannot exist yet (spec Decision 53): that is refused when typed,
    /// and the BCRA answers a future date with an error rather than with no value. Leaves the reason on screen when there is
    /// nothing to ask.
    /// </summary>
    private async Task<IReadOnlyList<IndexPeriod>> MonthsToAskAsync(IndexRow index)
    {
        var today = _clock.Today;
        if (!string.IsNullOrWhiteSpace(BringPeriodText))
        {
            if (!TryParsePeriod(BringPeriodText, out var typed))
            {
                RefusalMessage = PeriodFormatMessage;
                return [];
            }

            if (IndexEntryRules.CheckPeriod(typed, today, index.Name) is { } refusal)
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
            .Where(missing => missing.EconomicIndexId == index.Id && IndexEntryRules.CheckPeriod(missing.Period, today, index.Name) is null)
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

        ClearMessages();
        Editing = null;
        Corrections.Clear();
        PeriodError = null;
        LevelError = null;
        PeriodText = row.PeriodText;
        LevelText = row.LevelText!;
        WithdrawPending();

        await SubmitNewAsync(index, row.Period, level, acceptFarLevel: false);
    }

    private Task CorrectFromFetchedAsync(FetchedValueRow? row) =>
        row is { CanCorrect: true, Stored: { } stored, LevelText: { } level }
            ? OpenAsync(new IndexValueRow(stored, LevelUnit), level)
            : Task.CompletedTask;

    /// <summary>
    /// The form's own checks first, every one at once beside its field and nothing sent (spec Decisions 44 and 53); then, for a
    /// correction, what it would change; then the port, whose refusals need the database.
    /// </summary>
    private async Task SaveAsync()
    {
        ClearMessages();
        PeriodError = null;
        LevelError = null;
        WithdrawPending();

        var levelRead = IndexLevelParser.TryParse(LevelText, out var level);
        LevelError = levelRead ? IndexEntryRules.CheckLevel(level)?.OperatorText() : LevelFormatMessage;

        if (Editing is { } editing)
        {
            if (LevelError is null && level == editing.Listing.Level)
            {
                LevelError = UnchangedMessage;
            }

            if (LevelError is null)
            {
                await PrepareCorrectionAsync(editing, level);
            }

            return;
        }

        if (SelectedIndex is not { } index)
        {
            RefusalMessage = NoIndexMessage;
            return;
        }

        PeriodError = !TryParsePeriod(PeriodText, out var period)
            ? PeriodFormatMessage
            : IndexEntryRules.CheckPeriod(period, _clock.Today, index.Name)?.OperatorText();

        if (PeriodError is null && LevelError is null)
        {
            await SubmitNewAsync(index, period, level, acceptFarLevel: false);
        }
    }

    private async Task PrepareCorrectionAsync(IndexValueRow editing, decimal level)
    {
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
        CorrectionTitle = $"Confirme la corrección de {SelectedIndex?.Name} {editing.PeriodText}";
        CorrectionOldText = editing.LevelText;
        CorrectionNewText = Formats.Level(level);
        CorrectionSummary = $"{SelectedIndex?.Name} {editing.PeriodText}: de {editing.LevelText} a {Formats.Level(level)}.";
    }

    private Task ConfirmCorrectionAsync()
    {
        RefusalMessage = null;
        return Editing is { } editing && _pendingLevel is { } level
            ? SubmitCorrectionAsync(editing, level, acceptFarLevel: false)
            : Task.CompletedTask;
    }

    private async Task AcceptFarLevelAsync()
    {
        RefusalMessage = null;
        FarLevelWarning = null;

        if (Editing is { } editing && _pendingLevel is { } correction)
        {
            await SubmitCorrectionAsync(editing, correction, acceptFarLevel: true);
            return;
        }

        if (SelectedIndex is { } index
            && TryParsePeriod(PeriodText, out var period)
            && IndexLevelParser.TryParse(LevelText, out var level))
        {
            await SubmitNewAsync(index, period, level, acceptFarLevel: true);
        }
    }

    private async Task SubmitNewAsync(IndexRow index, IndexPeriod period, decimal level, bool acceptFarLevel)
    {
        var outcome = await Runner.RunAsync(() => _indices.AddAsync(index.Id, period, level, acceptFarLevel));
        if (await FinishAsync(outcome, period, level))
        {
            SavedMessage = $"Valor guardado. Ya está disponible para los contratos que usan {index.Name}.";

            // The panel stays while it still offers a month to store.
            IsBringOpen = IsBringOpen && Fetched.Any(row => row.CanStore);
        }
    }

    private async Task SubmitCorrectionAsync(IndexValueRow editing, decimal level, bool acceptFarLevel)
    {
        var affected = AffectedAdjustments.ToList();
        var name = SelectedIndex?.Name;
        var outcome = await Runner.RunAsync(() => _indices.CorrectAsync(editing.Id, level, acceptFarLevel));
        if (await FinishAsync(outcome, editing.Listing.Period, level))
        {
            CorrectionDone =
                $"Corrección guardada: {name} {editing.PeriodText} pasó de {editing.LevelText} a {Formats.Level(level)}. Quedó registrado quién la hizo y cuándo.";
            foreach (var row in affected)
            {
                DoneAdjustments.Add(row);
            }
        }
    }

    /// <summary>
    /// The list again, refused or not. A failure leaves the generic message on screen and the form as it was. Only a success
    /// clears the form, so a refused level is not typed twice.
    /// </summary>
    /// <returns>True when the write landed.</returns>
    private async Task<bool> FinishAsync(OperationOutcome<IndexEntryResult> outcome, IndexPeriod period, decimal level)
    {
        if (!outcome.Completed)
        {
            return false;
        }

        var result = outcome.Value!;
        await ReloadAsync();

        if (result.Succeeded)
        {
            ClearForm();
            return true;
        }

        var refusal = result.Refusal!.Value;
        switch (refusal)
        {
            case IndexRefusal.FarLevelNotAccepted:
                FarLevelWarning = result.Existing is { } earlier
                    ? $"{refusal.OperatorText()} El de {Formats.Period(earlier.Period)} es {Formats.Level(earlier.Level)}."
                    : refusal.OperatorText();
                FarPreviousLabel = result.Existing is { } previous ? $"Valor anterior ({Formats.Period(previous.Period)})" : null;
                FarPreviousLevel = result.Existing is { } shown ? Formats.Level(shown.Level) : null;
                FarTypedLabel = $"Valor que escribió ({Formats.Period(period)})";
                FarTypedLevel = Formats.Level(level);
                return false;
            case IndexRefusal.DuplicatePeriod:
                RefusalMessage = refusal.OperatorText();
                DuplicateOffer = result.Existing;
                return false;
            default:
                RefusalMessage = refusal.OperatorText();
                return false;
        }
    }

    /// <summary>Keeps the level already typed: it is the one the operator wants, and only the month was taken.</summary>
    private Task CorrectExistingAsync() =>
        DuplicateOffer is { } existing ? OpenAsync(new IndexValueRow(existing, LevelUnit), LevelText) : Task.CompletedTask;

    private Task EditAsync(IndexValueRow? row) => row is null ? Task.CompletedTask : OpenAsync(row, row.LevelText);

    private async Task OpenAsync(IndexValueRow row, string levelText)
    {
        ClearMessages();
        PeriodError = null;
        LevelError = null;
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

    /// <summary>Who publishes it, under its name in the list.</summary>
    public string SourceText => IndexGuidance.Source(Listing.Name);
}

/// <summary>One stored value of an index.</summary>
/// <param name="listing">The value.</param>
/// <param name="unit">"$" for an index in pesos, shown before the level in the list; empty otherwise.</param>
public sealed class IndexValueRow(IndexValueListing listing, string unit = "")
{
    public IndexValueListing Listing { get; } = listing ?? throw new ArgumentNullException(nameof(listing));

    public Guid Id => Listing.Id;

    public string PeriodText => Formats.Period(Listing.Period);

    /// <summary>The level as the form takes it back, with no unit.</summary>
    public string LevelText => Formats.Level(Listing.Level);

    /// <summary>The level as the list shows it, with its unit.</summary>
    public string ShownLevelText => unit.Length == 0 ? LevelText : $"{unit} {LevelText}";

    public bool IsCorrected => Listing.CorrectionCount > 0;

    /// <summary>The list's "Correcciones" column: how many times, or a dash for none.</summary>
    public string CorrectionsText => Listing.CorrectionCount switch
    {
        0 => "—",
        1 => "Corregido 1 vez",
        var times => $"Corregido {times} veces",
    };
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
