using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Adjustments;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// The contract detail's adjustment history (gestion-de-alquileres spec Requirements 16 and 18, task 12.3d): every figure of each
/// confirmed adjustment, and the one place the latest is corrected. A correction opens its summary, which the operator confirms
/// exactly as shown, choosing whether to charge a difference it offers; a stale one opens again with what is true now. After
/// confirming, the contract is read again, since its canon changed.
/// </summary>
public sealed partial class ContractAdjustmentsViewModel : ObservableObject
{
    /// <summary>Under "Corregir" on every row but the latest (canvas "Detalle del contrato").</summary>
    public const string OnlyTheLatest = "Solo se puede corregir el último ajuste del contrato. Este ajuste tiene otro posterior.";

    private readonly IAdjustmentDesk _desk;
    private readonly Func<Task> _readContractAgain;
    private readonly Func<string?>? _tenants;
    private Guid? _contractId;

    /// <param name="desk">Where adjustments are read and corrected.</param>
    /// <param name="runner">The detail's, so one action runs at a time on the whole screen.</param>
    /// <param name="readContractAgain">Reads the contract and this history again after a correction.</param>
    /// <param name="tenants">The contract's tenants as it names them, "Vera, Marta", for the reminder.</param>
    public ContractAdjustmentsViewModel(IAdjustmentDesk desk, OperationRunner runner, Func<Task> readContractAgain, Func<string?>? tenants = null)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(readContractAgain);

        _desk = desk;
        Runner = runner;
        _readContractAgain = readContractAgain;
        _tenants = tenants;
        CorrectCommand = new AsyncRelayCommand<AdjustmentHistoryRow>(CorrectAsync, row => CanAct() && row is { CanCorrect: true });
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && Summary is not null);
        CloseCommand = new RelayCommand(() => SetSummary(null), CanAct);
        CloseCorrectionCommand = new RelayCommand(CloseCorrection, CanAct);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; }

    /// <summary>The contract's confirmed adjustments, the most recently confirmed first.</summary>
    public ObservableCollection<AdjustmentHistoryRow> History { get; } = [];

    /// <summary>Each index of the open correction: its base and end month and level, and its variation.</summary>
    public ObservableCollection<AdjustmentIndexRow> SummaryLines { get; } = [];

    public bool HasNoAdjustments => History.Count == 0;

    /// <summary>Opens the correction of the latest adjustment (spec Requirement 18). Writes nothing.</summary>
    public IAsyncRelayCommand<AdjustmentHistoryRow> CorrectCommand { get; }

    /// <summary>Confirms the open correction, exactly as shown.</summary>
    public IAsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Closes the correction without writing anything.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>"Cancelar" and "Volver al contrato" on the correction's screen: back to the detail, forgetting what it showed.</summary>
    public IRelayCommand CloseCorrectionCommand { get; }

    /// <summary>The correction's own screen, in place of the detail (canvas "Corregir ajuste"): open from a summary until going back.</summary>
    [ObservableProperty]
    private bool _isCorrectionOpen;

    /// <summary>The correction waiting to be confirmed, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(IsSummaryOpen), nameof(PreviousCanonText), nameof(NewCanonText), nameof(CoefficientText))]
    [NotifyPropertyChangedFor(nameof(DueDateText), nameof(AppliesFromText), nameof(HasDifference), nameof(DifferenceText))]
    [NotifyPropertyChangedFor(nameof(CorrectionText), nameof(DifferenceOutcomeText), nameof(ExactCanonText), nameof(CoefficientPercentText))]
    private AdjustmentPreview? _summary;

    /// <summary>The operator's choice on the difference offered: no until they say yes, and no again for every new summary.</summary>
    [ObservableProperty]
    private bool _chargeDifference;

    /// <summary>After a correction: the new canon and the first period it applies to.</summary>
    [ObservableProperty]
    private string? _confirmedMessage;

    /// <summary>After a correction: tell the tenant, since the system sends nothing.</summary>
    [ObservableProperty]
    private string? _noticeReminder;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    public bool IsSummaryOpen => Summary is not null;

    public string? PreviousCanonText => Summary is { } s ? Formats.Money(s.PreviousCanon) : null;

    public string? NewCanonText => Summary is { } s ? Formats.Money(s.NewCanon) : null;

    /// <summary>The canon before truncating, with its cents (canvas "Resultado exacto"): the operator sees what was cut.</summary>
    public string? ExactCanonText => Summary is { } s ? $"$ {s.UntruncatedNewCanon.ToString("#,##0.00", Formats.Agency)}" : null;

    public string? CoefficientText => Summary is { } s ? Formats.Level(s.Coefficient) : null;

    /// <summary>The coefficient as a percentage, "12,00 %", as Ajustes and the canvas show it.</summary>
    public string? CoefficientPercentText => Summary is { } s ? AdjustmentTexts.Percent(s.Coefficient) : null;

    public string? DueDateText => Summary is { } s ? Formats.Date(s.DueDate) : null;

    public string? AppliesFromText => Summary is { } s ? Formats.Period(s.AppliesFrom) : null;

    public bool HasDifference => Summary?.Difference is not null;

    public string? DifferenceText => AdjustmentTexts.Difference(Summary);

    public string? CorrectionText => AdjustmentTexts.Correction(Summary);

    public string? DifferenceOutcomeText => AdjustmentTexts.DifferenceOutcome(Summary);

    /// <summary>
    /// Reads the contract's history. Only the latest row is offered for correcting, and none on a contract that cannot be; every
    /// row not offered says why: <paramref name="whyNot"/> on the latest, <see cref="OnlyTheLatest"/> on the rest.
    /// </summary>
    internal async Task ShowAsync(Guid contractId, bool canCorrect, string? whyNot = null)
    {
        _contractId = contractId;
        var outcome = await Runner.RunAsync(() => _desk.HistoryAsync(contractId));
        if (!outcome.Completed)
        {
            return;
        }

        History.Clear();
        var latest = true;
        foreach (var entry in outcome.Value!)
        {
            // The history comes the most recently confirmed first, and only that one can be corrected (spec Decision 26).
            History.Add(AdjustmentTexts.HistoryRow(entry, canCorrect && latest) with
            {
                CorrectNote = latest ? (canCorrect ? null : whyNot) : OnlyTheLatest,
            });
            latest = false;
        }

        OnPropertyChanged(nameof(HasNoAdjustments));
        CorrectCommand.NotifyCanExecuteChanged();
    }

    private bool CanAct() => !Runner.IsBusy;

    private async Task CorrectAsync(AdjustmentHistoryRow? row)
    {
        if (row is not { CanCorrect: true } || _contractId is not { } contractId)
        {
            return;
        }

        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;
        await OpenCorrectionAsync(contractId, row.AdjustmentId);

        // A refusal on opening stays on the detail, under the history; only a summary opens the screen.
        IsCorrectionOpen = Summary is not null;
    }

    private void CloseCorrection()
    {
        SetSummary(null);
        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;
        IsCorrectionOpen = false;
    }

    private async Task OpenCorrectionAsync(Guid contractId, Guid adjustmentId)
    {
        SetSummary(null);

        var outcome = await Runner.RunAsync(() => _desk.PreviewCorrectionAsync(contractId, adjustmentId));
        if (!outcome.Completed)
        {
            return;
        }

        if (outcome.Value!.Refusal is { } refusal)
        {
            RefusalMessage = refusal.OperatorText();
            return;
        }

        SetSummary(outcome.Value.Preview);
    }

    private async Task ConfirmAsync()
    {
        if (Summary is not { CorrectsAdjustmentId: { } corrected } preview)
        {
            return;
        }

        RefusalMessage = null;
        ConfirmedMessage = null;
        NoticeReminder = null;

        var charge = ChargeDifference && preview.Difference is not null;
        var outcome = await Runner.RunAsync(() => _desk.ConfirmCorrectionAsync(preview, charge), OperationKind.Commit);
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        if (result.Confirmed is { } confirmed)
        {
            SetSummary(null);
            ConfirmedMessage = AdjustmentTexts.Confirmed(confirmed, "Ajuste corregido");
            NoticeReminder = AdjustmentTexts.Reminder(_tenants?.Invoke() is { Length: > 0 } tenants ? tenants : preview.TenantName);
            await _readContractAgain();
            return;
        }

        var refusal = result.Refusal!.Value;
        if (refusal == AdjustmentRefusal.PreviewStale)
        {
            // Opened again as a correction with what is true now; the operator confirms that one or nothing.
            await OpenCorrectionAsync(preview.ContractId, corrected);
        }
        else
        {
            SetSummary(null);
            await _readContractAgain();
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

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            CorrectCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
            CloseCorrectionCommand.NotifyCanExecuteChanged();
        }
    }
}
