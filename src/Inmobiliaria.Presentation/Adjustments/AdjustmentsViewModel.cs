using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Adjustments;

/// <summary>
/// The Ajustes screen (gestion-de-alquileres spec Requirements 14 and 15, design §3.4): the contracts with an adjustment due,
/// naming the index and month a waiting one still lacks; the summary of the one chosen, with every figure; confirming exactly
/// that summary; and, once confirmed, the new canon, the first period it applies to and the reminder to tell the tenant,
/// since nothing is sent (spec Decision P13). The list is read again after every confirmation, refused or not. The contract's
/// history is shown to read: correcting an adjustment is the contract detail's (task 12.3d). Both roles.
/// </summary>
public sealed partial class AdjustmentsViewModel : ObservableObject
{
    private readonly IDueAdjustmentQuery _worklist;
    private readonly IAdjustmentDesk _desk;
    private readonly IContractRegistry _contracts;
    private readonly IAgencyClock _clock;

    public AdjustmentsViewModel(IDueAdjustmentQuery worklist, IAdjustmentDesk desk, IContractRegistry contracts, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(worklist);
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(clock);

        _worklist = worklist;
        _desk = desk;
        _contracts = contracts;
        _clock = clock;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => CanAct() && SelectedRow is not null);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && Summary is not null);
        BackCommand = new RelayCommand(Back, CanAct);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    /// <summary>"Cancelar" and "Volver a Ajustes" on the confirming screen: the list again, forgetting what the screen showed.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Every row read, whatever the filter: the list shows the ones the filter lets through.</summary>
    private IReadOnlyList<DueAdjustmentRow> _rows = [];

    /// <summary>The row whose summary is open, which heads the confirming screen.</summary>
    private DueAdjustmentRow? _openRow;

    /// <summary>The canvas's "Por confirmar", "Listos" and "Esperando un índice"; changing it asks the database nothing.</summary>
    [ObservableProperty]
    private AdjustmentFilter _filter = AdjustmentFilter.All;

    partial void OnFilterChanged(AdjustmentFilter value) => Show();

    /// <summary>"1 contrato", "2 contratos": the rows the filter shows.</summary>
    public string CountText => Worklist.Count == 1 ? "1 contrato" : $"{Worklist.Count} contratos";

    /// <summary>
    /// Over the list, each index and month still missing with how many contracts wait on it (canvas "Faltan valores de índices");
    /// null when none is.
    /// </summary>
    [ObservableProperty]
    private string? _missingBannerText;

    /// <summary>The confirming screen (canvas "Confirmar ajuste"), from a summary until going back.</summary>
    [ObservableProperty]
    private bool _isConfirmOpen;

    public bool IsSummaryOpen => Summary is not null;

    /// <summary>The open contract's state, beside its name.</summary>
    public string? OpenStatusText => _openRow?.ContractStatusText;

    /// <summary>How late the open adjustment is.</summary>
    public string? OpenLateText => _openRow?.LateText;

    /// <summary>Under the name: the unit, the contract's short id and its clause (canvas "Confirmar ajuste").</summary>
    public string? OpenHeaderText => _openRow is { } row
        ? $"{row.UnitsText} · Contrato {row.ContractId.ToString("N")[..8]} · {row.ClauseText}, {row.EveryText.ToLowerInvariant()}"
        : null;

    /// <summary>The coefficient as a percentage, as the canvas shows it.</summary>
    public string? CoefficientPercentText => Summary is { } s ? AdjustmentTexts.Percent(s.Coefficient) : null;

    /// <summary>The canon before truncating, with its cents (canvas "Resultado exacto").</summary>
    public string? ExactCanonText => Summary is { } s ? $"$ {s.UntruncatedNewCanon.ToString("#,##0.00", Formats.Agency)}" : null;

    /// <summary>How the coefficient came out, worked out.</summary>
    public string? CoefficientExplanation => AdjustmentTexts.CoefficientExplanation(Summary);

    public OperationRunner Runner { get; } = new();

    /// <summary>Every contract with an adjustment due today, the oldest first as the worklist gives them.</summary>
    public ObservableCollection<DueAdjustmentRow> Worklist { get; } = [];

    /// <summary>Each index of the open summary: its base and end month and level, and its variation.</summary>
    public ObservableCollection<AdjustmentIndexRow> SummaryLines { get; } = [];

    /// <summary>The open contract's confirmed adjustments, the most recently confirmed first, to read only.</summary>
    public ObservableCollection<AdjustmentHistoryRow> History { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Opens the summary of the selected contract's oldest due adjustment. Writes nothing.</summary>
    public IAsyncRelayCommand OpenCommand { get; }

    /// <summary>Confirms the open summary, exactly as shown.</summary>
    public IAsyncRelayCommand ConfirmCommand { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private DueAdjustmentRow? _selectedRow;

    /// <summary>The summary waiting to be confirmed, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(TenantText), nameof(PreviousCanonText), nameof(CoefficientText), nameof(NewCanonText))]
    [NotifyPropertyChangedFor(nameof(DueDateText), nameof(AppliesFromText), nameof(LateText))]
    [NotifyPropertyChangedFor(nameof(HasDifference), nameof(DifferenceText))]
    [NotifyPropertyChangedFor(nameof(IsSummaryOpen), nameof(CoefficientPercentText), nameof(ExactCanonText), nameof(CoefficientExplanation))]
    private AdjustmentPreview? _summary;

    /// <summary>After a confirmation: the new canon and the first period it applies to.</summary>
    [ObservableProperty]
    private string? _confirmedMessage;

    /// <summary>After a confirmation: tell the tenant, since the system sends nothing.</summary>
    [ObservableProperty]
    private string? _noticeReminder;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>The operator's choice on the difference offered: no until they say yes, and no again for every new summary.</summary>
    [ObservableProperty]
    private bool _chargeDifference;

    /// <summary>The open summary offers a difference, so the choice is shown.</summary>
    public bool HasDifference => Summary?.Difference is not null;

    /// <summary>The difference offered and the months it covers, or null when none is offered.</summary>
    public string? DifferenceText => AdjustmentTexts.Difference(Summary);

    public string? TenantText => Summary?.TenantName;

    public string? PreviousCanonText => Summary is { } s ? Formats.Money(s.PreviousCanon) : null;

    public string? CoefficientText => Summary is { } s ? Formats.Level(s.Coefficient) : null;

    public string? NewCanonText => Summary is { } s ? Formats.Money(s.NewCanon) : null;

    public string? DueDateText => Summary is { } s ? Formats.Date(s.DueDate) : null;

    public string? AppliesFromText => Summary is { } s ? Formats.Period(s.AppliesFrom) : null;

    /// <summary>Set only when the adjustment prices from a later month than the one it fell due in.</summary>
    public string? LateText => AdjustmentTexts.Late(Summary);

    private bool CanAct() => !Runner.IsBusy;

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
        }
    }

    private void Back()
    {
        SetSummary(null);
        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;
        IsConfirmOpen = false;
        _openRow = null;
        OnPropertyChanged(nameof(OpenStatusText));
        OnPropertyChanged(nameof(OpenLateText));
        OnPropertyChanged(nameof(OpenHeaderText));
    }

    private void Show()
    {
        var selected = SelectedRow?.ContractId;
        Worklist.Clear();
        foreach (var row in _rows.Where(r => Filter switch
        {
            AdjustmentFilter.Ready => r.IsReady,
            AdjustmentFilter.Waiting => !r.IsReady,
            _ => true,
        }))
        {
            Worklist.Add(row);
        }

        SelectedRow = Worklist.FirstOrDefault(r => r.ContractId == selected);
        OnPropertyChanged(nameof(CountText));
    }

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var today = _clock.Today;
        var outcome = await Runner.RunAsync(async () =>
            (Due: await _worklist.GetDueAsync(today), Contracts: await _contracts.ListAsync(null, null)));
        if (!outcome.Completed)
        {
            return;
        }

        var listings = outcome.Value.Contracts.ToDictionary(c => c.Id);

        _rows = outcome.Value.Due
            .Select(due =>
            {
                var listing = listings.GetValueOrDefault(due.ContractId);
                var ready = due.Missing.Count == 0;
                return new DueAdjustmentRow(
                    due.ContractId,
                    listing?.Tenants ?? string.Empty,
                    Formats.Date(due.DueDate),
                    Formats.Money(due.CurrentCanon),
                    ready,
                    ready ? "Listo para confirmar" : MissingText(due.Missing))
                {
                    UnitsText = listing?.Units ?? string.Empty,
                    ClauseText = AdjustmentTexts.Clause(due.IndexNames, due.Combination),
                    EveryText = AdjustmentTexts.Every(due.IntervalMonths),
                    LateText = AdjustmentTexts.Late(due.DueDate, today),
                    ProposalText = (ready, due.ProposedCanon, due.Coefficient) switch
                    {
                        (false, _, _) => "falta el índice",
                        (true, { } canon, { } coefficient) => $"quedaría {Formats.Money(canon)} · {AdjustmentTexts.Percent(coefficient, signed: true)}",
                        _ => string.Empty,
                    },
                    IsPendingTermination = listing?.Status == ContractStatus.PendingTermination,
                    ContractStatusText = listing is null ? string.Empty : Formats.Status(listing.Status),
                };
            })
            .ToList();

        // Each index and month missing once, with how many contracts wait on it, the earliest month first.
        var waiting = outcome.Value.Due
            .SelectMany(due => due.Missing.Select(m => (m.IndexName, m.Period, due.ContractId)))
            .GroupBy(m => (m.IndexName, m.Period))
            .OrderBy(g => g.Key.Period)
            .ThenBy(g => g.Key.IndexName, StringComparer.Ordinal)
            .Select(g => (g.Key.IndexName, g.Key.Period, Count: g.Select(m => m.ContractId).Distinct().Count()))
            .Select(w => w.Count == 1
                ? $"1 contrato espera el {w.IndexName} de {Formats.Period(w.Period)}."
                : $"{w.Count} contratos esperan el {w.IndexName} de {Formats.Period(w.Period)}.")
            .ToList();
        MissingBannerText = waiting.Count == 0 ? null : $"{string.Join(" ", waiting)} Mientras falte, sigue valiendo el alquiler actual.";

        Show();
    }

    private async Task OpenAsync()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;
        _openRow = row;
        await ShowSummaryAsync(row.ContractId);

        // A refusal on opening stays on the list; only a summary opens the confirming screen.
        IsConfirmOpen = Summary is not null;
        OnPropertyChanged(nameof(OpenStatusText));
        OnPropertyChanged(nameof(OpenLateText));
        OnPropertyChanged(nameof(OpenHeaderText));
    }

    private async Task ShowSummaryAsync(Guid contractId)
    {
        SetSummary(null);
        History.Clear();

        var outcome = await Runner.RunAsync(async () =>
            (Preview: await _desk.PreviewAsync(contractId), History: await _desk.HistoryAsync(contractId)));
        if (!outcome.Completed)
        {
            return;
        }

        foreach (var entry in outcome.Value.History)
        {
            History.Add(AdjustmentTexts.HistoryRow(entry, canCorrect: false));
        }

        var result = outcome.Value.Preview;
        if (result.Refusal is { } refusal)
        {
            RefusalMessage = refusal == AdjustmentRefusal.IndexValuesMissing && result.Missing.Count > 0
                ? $"{refusal.OperatorText()} {MissingText(result.Missing)}."
                : refusal.OperatorText();
            return;
        }

        SetSummary(result.Preview);
    }

    private async Task ConfirmAsync()
    {
        if (Summary is not { } preview)
        {
            return;
        }

        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;

        var charge = ChargeDifference && preview.Difference is not null;
        var outcome = await Runner.RunAsync(() => _desk.ConfirmAsync(preview, charge), OperationKind.Commit);
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        if (result.Confirmed is { } confirmed)
        {
            SetSummary(null);
            ConfirmedMessage = AdjustmentTexts.Confirmed(confirmed, "Ajuste confirmado");
            NoticeReminder = AdjustmentTexts.Reminder(preview.TenantName);
            await ReloadAsync();
            return;
        }

        var refusal = result.Refusal!.Value;
        if (refusal == AdjustmentRefusal.PreviewStale)
        {
            // The summary is opened again with what is true now; the operator confirms that one or nothing.
            await ShowSummaryAsync(preview.ContractId);
        }
        else
        {
            SetSummary(null);
            await ReloadAsync();
        }

        RefusalMessage = refusal.OperatorText();
    }

    private void SetSummary(AdjustmentPreview? preview)
    {
        // A choice covers only the summary it was made on.
        ChargeDifference = false;
        Summary = preview;
        SummaryLines.Clear();
        foreach (var line in preview?.Indices ?? [])
        {
            SummaryLines.Add(AdjustmentTexts.IndexRow(line));
        }
    }

    private static string MissingText(IReadOnlyList<MissingIndexValue> missing) =>
        string.Join(", ", missing.Select(m => $"Falta {m.IndexName} de {Formats.Period(m.Period)}"));
}

/// <param name="ContractId">The contract.</param>
/// <param name="Tenants">Its tenant, as the contract list names them.</param>
/// <param name="DueDateText">The adjustment due date.</param>
/// <param name="CurrentCanonText">The canon the adjustment starts from.</param>
/// <param name="IsReady">Every index value the window needs is published.</param>
/// <param name="StatusText">Ready, or which index and month it still lacks.</param>
public sealed record DueAdjustmentRow(Guid ContractId, string Tenants, string DueDateText, string CurrentCanonText, bool IsReady, string StatusText)
{
    /// <summary>Its units, under the tenant.</summary>
    public string UnitsText { get; init; } = string.Empty;

    /// <summary>"ICL", or "Promedio de IPC y RIPTE".</summary>
    public string ClauseText { get; init; } = string.Empty;

    /// <summary>"Cada 3 meses", under the clause.</summary>
    public string EveryText { get; init; } = string.Empty;

    /// <summary>"Vence este mes" or "Atrasado N meses", under the due date.</summary>
    public string LateText { get; init; } = string.Empty;

    /// <summary>"quedaría $ X · +Y %" when ready; "falta el índice" while waiting.</summary>
    public string ProposalText { get; init; } = string.Empty;

    /// <summary>A contract with notice given, marked as such on its row.</summary>
    public bool IsPendingTermination { get; init; }

    /// <summary>The contract's state, as the confirming screen shows it beside the name.</summary>
    public string ContractStatusText { get; init; } = string.Empty;

    /// <summary>The state's chip.</summary>
    public string ChipText => IsReady ? "Listo para confirmar" : "Esperando un índice";

    /// <summary>The row's button: the summary when ready, Índices while waiting.</summary>
    public string ActionText => IsReady ? "Revisar" : "Ir a Índices";
}

/// <summary>The Ajustes list's filters (canvas "Por confirmar", "Listos", "Esperando un índice").</summary>
public enum AdjustmentFilter
{
    All,
    Ready,
    Waiting,
}

public sealed record AdjustmentIndexRow(
    string IndexName, string BasePeriodText, string BaseLevelText, string EndPeriodText, string EndLevelText, string VariationText)
{
    /// <summary>The variation as the canvas shows it, "+8,37 %".</summary>
    public string VariationPercentText { get; init; } = string.Empty;
}

/// <param name="AppliesFromText">The first period it priced; for a row from before this was recorded, its due month.</param>
/// <param name="ConfirmedByText">Who confirmed it, or "Sin registro" on rows from before that was recorded.</param>
/// <param name="CanCorrect">Only the latest adjustment, and only on the contract detail (spec Decision 26, task 12.3d).</param>
public sealed record AdjustmentHistoryRow(
    string DueDateText, string AppliesFromText, string PreviousCanonText, string NewCanonText, string CoefficientText, string ConfirmedByText,
    string DifferenceText = "")
{
    public Guid AdjustmentId { get; init; }

    public bool CanCorrect { get; init; }

    /// <summary>Under "Corregir", why this row is not corrected (canvas "Detalle del contrato"); null on the row that is.</summary>
    public string? CorrectNote { get; init; }
}
