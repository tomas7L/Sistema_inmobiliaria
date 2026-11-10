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
/// since nothing is sent (spec Decision P13). The list is read again after every confirmation, refused or not. Both roles;
/// the view arrives in slice S12.
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
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>Every contract with an adjustment due today, the oldest first as the worklist gives them.</summary>
    public ObservableCollection<DueAdjustmentRow> Worklist { get; } = [];

    /// <summary>Each index of the open summary: its base and end month and level, and its variation.</summary>
    public ObservableCollection<AdjustmentIndexRow> SummaryLines { get; } = [];

    /// <summary>The open contract's confirmed adjustments, the most recently confirmed first.</summary>
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
    public string? DifferenceText => Summary?.Difference is { } offer
        ? $"Diferencia de {Formats.Money(offer.Amount)} por los meses {Formats.Period(offer.Months[0])} a {Formats.Period(offer.Months[^1])}, ya cobrados con el canon anterior."
        : null;

    public string? TenantText => Summary?.TenantName;

    public string? PreviousCanonText => Summary is { } s ? Formats.Money(s.PreviousCanon) : null;

    public string? CoefficientText => Summary is { } s ? Formats.Level(s.Coefficient) : null;

    public string? NewCanonText => Summary is { } s ? Formats.Money(s.NewCanon) : null;

    public string? DueDateText => Summary is { } s ? Formats.Date(s.DueDate) : null;

    public string? AppliesFromText => Summary is { } s ? Formats.Period(s.AppliesFrom) : null;

    /// <summary>Set only when the adjustment prices from a later month than the one it fell due in.</summary>
    public string? LateText => Summary is { IsLate: true } s
        ? $"Se confirma tarde: el nuevo canon rige desde {Formats.Period(s.AppliesFrom)}. Los meses anteriores quedan con el canon anterior."
        : null;

    private bool CanAct() => !Runner.IsBusy;

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
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

        var tenants = outcome.Value.Contracts.ToDictionary(c => c.Id, c => c.Tenants);
        var selected = SelectedRow?.ContractId;

        Worklist.Clear();
        foreach (var due in outcome.Value.Due)
        {
            Worklist.Add(new DueAdjustmentRow(
                due.ContractId,
                tenants.GetValueOrDefault(due.ContractId, string.Empty),
                Formats.Date(due.DueDate),
                Formats.Money(due.CurrentCanon),
                due.Missing.Count == 0,
                due.Missing.Count == 0 ? "Listo para confirmar" : MissingText(due.Missing)));
        }

        SelectedRow = Worklist.FirstOrDefault(r => r.ContractId == selected);
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
        await ShowSummaryAsync(row.ContractId);
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
            History.Add(new AdjustmentHistoryRow(
                Formats.Date(entry.DueDate),
                Formats.Period(entry.AppliesFrom ?? entry.DueDate),
                Formats.Money(entry.PreviousCanon),
                Formats.Money(entry.NewCanon),
                Formats.Level(entry.Coefficient),
                entry.ConfirmedBy ?? "Sin registro",
                entry.DifferenceAmount is { } offered
                    ? $"Diferencia de {Formats.Money(offered)} {(entry.DifferenceCharged ? "cobrada" : "no cobrada")}"
                    : string.Empty));
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
            var from = Formats.Period(confirmed.AppliesFrom ?? confirmed.DueDate);
            ConfirmedMessage = confirmed is { DifferenceCharged: true, DifferenceAmount: { } charged }
                ? $"Ajuste confirmado: el canon pasa a {Formats.Money(confirmed.NewCanon)} desde {from}. Se cobra una diferencia de {Formats.Money(charged)} con {from}."
                : $"Ajuste confirmado: el canon pasa a {Formats.Money(confirmed.NewCanon)} desde {from}.";
            NoticeReminder = $"Avise a {preview.TenantName ?? "el inquilino"} del nuevo canon. El sistema no le envía nada.";
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
            SummaryLines.Add(new AdjustmentIndexRow(
                line.IndexName,
                Formats.Period(line.BasePeriod),
                Formats.Level(line.BaseLevel),
                Formats.Period(line.EndPeriod),
                Formats.Level(line.EndLevel),
                Formats.Level(line.Variation)));
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
public sealed record DueAdjustmentRow(Guid ContractId, string Tenants, string DueDateText, string CurrentCanonText, bool IsReady, string StatusText);

public sealed record AdjustmentIndexRow(
    string IndexName, string BasePeriodText, string BaseLevelText, string EndPeriodText, string EndLevelText, string VariationText);

/// <param name="AppliesFromText">The first period it priced; for a row from before this was recorded, its due month.</param>
/// <param name="ConfirmedByText">Who confirmed it, or "Sin registro" on rows from before that was recorded.</param>
public sealed record AdjustmentHistoryRow(
    string DueDateText, string AppliesFromText, string PreviousCanonText, string NewCanonText, string CoefficientText, string ConfirmedByText,
    string DifferenceText = "");
