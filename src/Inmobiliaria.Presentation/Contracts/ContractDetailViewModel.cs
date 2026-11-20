using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// One contract, with notice, end, their undoing and cancelling (gestion-de-alquileres spec Requirements 6, 10, 23 and 24, design §6). Each of them opens a summary
/// that the operator confirms, and editing what the summary was built from closes it, so what is confirmed is what was read. The
/// contract is read again after every action. Each state offers its own buttons (task 12.3d); the port refuses the rest anyway.
/// Both roles.
/// </summary>
public sealed partial class ContractDetailViewModel : ObservableObject
{
    private readonly IContractRegistry _contracts;
    private readonly Guid _contractId;
    private readonly IAgencyClock _clock;

    public ContractDetailViewModel(IContractRegistry contracts, IAdjustmentDesk adjustments, Guid contractId, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(adjustments);
        ArgumentNullException.ThrowIfNull(clock);

        _contracts = contracts;
        _contractId = contractId;
        _clock = clock;
        Adjustments = new ContractAdjustmentsViewModel(adjustments, Runner, LoadAsync);
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        ReviewNoticeCommand = new RelayCommand(ReviewNotice, CanAct);
        ReviewEndCommand = new AsyncRelayCommand(ReviewEndAsync, CanAct);
        ReviewUndoNoticeCommand = new RelayCommand(ReviewUndoNotice, CanAct);
        ReviewUndoEndCommand = new AsyncRelayCommand(ReviewUndoEndAsync, CanAct);
        ReviewCancelCommand = new AsyncRelayCommand(ReviewCancelAsync, CanAct);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && PendingChange is not null);
        CancelCommand = new RelayCommand(CloseSummary, CanAct);
        OpenNoticeCommand = new RelayCommand(() => OpenActionScreen(ContractAction.Notice), () => CanAct() && IsNoticeOffered);
        OpenUndoNoticeCommand = new RelayCommand(() => OpenActionScreen(ContractAction.UndoNotice), () => CanAct() && IsUndoNoticeOffered);
        OpenEndCommand = new RelayCommand(() => OpenActionScreen(ContractAction.End), () => CanAct() && IsEndOffered);
        OpenUndoEndCommand = new RelayCommand(() => OpenActionScreen(ContractAction.UndoEnd), () => CanAct() && IsUndoEndOffered);
        OpenCancelCommand = new RelayCommand(() => OpenActionScreen(ContractAction.Cancel), () => CanAct() && IsCancelOffered);
        CloseActionCommand = new RelayCommand(() => OpenActionScreen(null), CanAct);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>The adjustment history, the one place the latest is corrected (task 12.3d).</summary>
    public ContractAdjustmentsViewModel Adjustments { get; }

    public IAsyncRelayCommand LoadCommand { get; }

    public IRelayCommand ReviewNoticeCommand { get; }

    public IAsyncRelayCommand ReviewEndCommand { get; }

    public IRelayCommand ReviewUndoNoticeCommand { get; }

    public IAsyncRelayCommand ReviewUndoEndCommand { get; }

    public IAsyncRelayCommand ReviewCancelCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>The canvas's "Registrar aviso" button: its own screen, in place of the detail.</summary>
    public IRelayCommand OpenNoticeCommand { get; }

    /// <summary>The canvas's "Deshacer aviso" button: its own screen, in place of the detail.</summary>
    public IRelayCommand OpenUndoNoticeCommand { get; }

    /// <summary>The canvas's "Terminar" button: its own screen, in place of the detail.</summary>
    public IRelayCommand OpenEndCommand { get; }

    /// <summary>The canvas's "Deshacer fin" button: its own screen, in place of the detail.</summary>
    public IRelayCommand OpenUndoEndCommand { get; }

    /// <summary>The canvas's "Cancelar por error" button: its own screen, in place of the detail.</summary>
    public IRelayCommand OpenCancelCommand { get; }

    /// <summary>"Volver al contrato": back to the detail, forgetting what the action's screen held.</summary>
    public IRelayCommand CloseActionCommand { get; }

    /// <summary>The action whose screen is shown in place of the detail, or null for the detail itself.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActionOpen))]
    private ContractAction? _openAction;

    public bool IsActionOpen => OpenAction is not null;

    /// <summary>What an action that landed did, shown in place of its form until the operator goes back; or null.</summary>
    [ObservableProperty]
    private ActionDone? _done;

    /// <summary>The dates as typed, read as the contract form reads them; each sets its date.</summary>
    [ObservableProperty]
    private string _noticeDateText = string.Empty;

    [ObservableProperty]
    private string _moveOutDateText = string.Empty;

    /// <summary>Each date's error, under its field; null while it has none.</summary>
    [ObservableProperty]
    private string? _noticeDateError;

    [ObservableProperty]
    private string? _moveOutDateError;

    /// <summary>The end date as typed, read as the contract form reads dates; it sets <see cref="EndDate"/>.</summary>
    [ObservableProperty]
    private string _endDateText = string.Empty;

    /// <summary>The end's reason and date errors, each under its field (canvas "Terminar contrato"); null while none.</summary>
    [ObservableProperty]
    private string? _endReasonError;

    [ObservableProperty]
    private string? _endDateError;

    partial void OnEndDateTextChanged(string value)
    {
        EndDate = DateEntry.Parse(value, DateEntryKind.Day);
        EndDateError = null;
    }

    /// <summary>The reason's error, under its field (canvas "Deshacer aviso"); null while it has none.</summary>
    [ObservableProperty]
    private string? _changeReasonError;

    partial void OnNoticeDateTextChanged(string value)
    {
        NoticeDate = DateEntry.Parse(value, DateEntryKind.Day);
        NoticeDateError = null;
    }

    partial void OnMoveOutDateTextChanged(string value)
    {
        MoveOutDate = DateEntry.Parse(value, DateEntryKind.Day);
        MoveOutDateError = null;
    }

    /// <summary>
    /// The contract an action's screen is for, as the canvas heads it: tenant, unit and current rent, and its term, which the
    /// action's dates are checked against (Tomás, 2026-11-19).
    /// </summary>
    public IReadOnlyList<DetailLine> ActionCardLines => Detail is { } d
        ?
        [
            new DetailLine("Inquilino", Names(d, PartyRole.Tenant)),
            new DetailLine("Propiedad", string.Join(", ", d.Units.Select(u => u.Address))),
            new DetailLine("Vigencia", $"{Formats.Date(d.StartDate)} al {Formats.Date(d.NominalEndDate)}"),
            new DetailLine("Alquiler actual", Formats.Money(d.MonthlyRent)),
            .. NoticeLines(d),
            .. EndLines(d),
        ]
        : [];

    /// <summary>The notice being undone, as the canvas's "Deshacer aviso" heads the contract: its two dates.</summary>
    private IReadOnlyList<DetailLine> NoticeLines(ContractDetail detail) =>
        OpenAction == ContractAction.UndoNotice && detail is { NoticeGivenDate: { } notice, PlannedMoveOutDate: { } moveOut }
            ? [new DetailLine("Fecha del aviso", Formats.Date(notice)), new DetailLine("Fecha de desocupación", Formats.Date(moveOut))]
            : [];

    /// <summary>The end being undone, as the canvas's "Deshacer fin" heads the contract: its date and its reason.</summary>
    private IReadOnlyList<DetailLine> EndLines(ContractDetail detail) =>
        OpenAction == ContractAction.UndoEnd && detail is { ActualEndDate: { } ended, EndReason: { } reason }
            ? [new DetailLine("Fecha de fin", Formats.Date(ended)), new DetailLine("Motivo del fin", Formats.EndReason(reason))]
            : [];

    private void OpenActionScreen(ContractAction? action)
    {
        CloseSummary();
        RefusalMessage = null;
        Done = null;
        NoticeDateText = string.Empty;
        MoveOutDateText = string.Empty;
        NoticeDate = null;
        MoveOutDate = null;
        NoticeDateError = null;
        MoveOutDateError = null;
        ChangeReason = string.Empty;
        ChangeReasonError = null;
        EndReason = null;
        EndDateText = string.Empty;
        EndDate = null;
        EndReasonError = null;
        EndDateError = null;
        OpenAction = action;
        OnPropertyChanged(nameof(ActionCardLines));
    }

    /// <summary>The reasons offered for ending, named. "Cargado por error" never is: only cancelling uses it (spec Requirement 10).</summary>
    public IReadOnlyList<EndReasonOption> EndReasons { get; } = Enum.GetValues<EndReason>()
        .Where(r => r != Domain.Leasing.EndReason.LoadedByMistake)
        .Select(r => new EndReasonOption(r, Formats.EndReason(r)))
        .ToList();

    /// <summary>The contract as last read, or null before that or when it is not there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(EndReasonText), nameof(IsEditOffered), nameof(IsEditLimited))]
    [NotifyPropertyChangedFor(nameof(IsNoticeOffered), nameof(IsUndoNoticeOffered), nameof(IsEndOffered), nameof(IsUndoEndOffered))]
    [NotifyPropertyChangedFor(nameof(IsCancelOffered), nameof(IsAccountOffered), nameof(HasNoEvents))]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(StateNote), nameof(PartyLines), nameof(UnitLines))]
    [NotifyPropertyChangedFor(nameof(ClauseLines), nameof(AmountLines), nameof(NextAdjustmentText))]
    private ContractDetail? _detail;

    /// <summary>The reason, who and when of what was cancelled, undone or removed, oldest first.</summary>
    public ObservableCollection<ContractEventRow> Events { get; } = [];

    public bool HasNoEvents => Events.Count == 0;

    public string? StatusText => Detail is { } d ? Formats.Status(d.Status, d.IsCancelled) : null;

    /// <summary>Why it ended, once it has and was not cancelled.</summary>
    public string? EndReasonText => Detail is { EndReason: { } reason, IsCancelled: false } ? Formats.EndReason(reason) : null;

    private bool IsOpen => Detail is { IsCancelled: false };

    private ContractStatus? Status => Detail?.Status;

    public bool IsEditOffered => IsOpen;

    /// <summary>An ended contract corrects only its honorarios rate and its codeudores (task 12.3d).</summary>
    public bool IsEditLimited => IsOpen && Status == ContractStatus.Ended;

    public bool IsNoticeOffered => IsOpen && Status == ContractStatus.Active;

    public bool IsUndoNoticeOffered => IsOpen && Status == ContractStatus.PendingTermination;

    public bool IsEndOffered => IsOpen && Status is ContractStatus.Active or ContractStatus.PendingTermination;

    public bool IsUndoEndOffered => IsOpen && Status == ContractStatus.Ended;

    public bool IsCancelOffered => IsOpen;

    /// <summary>Every contract read, a cancelled one included, has its account.</summary>
    public bool IsAccountOffered => Detail is not null;

    /// <summary>The tab shown, the Resumen first (canvas "Detalle del contrato").</summary>
    [ObservableProperty]
    private ContractDetailTab _tab = ContractDetailTab.Summary;

    public string? Title => Detail is { } d ? $"Contrato de {Names(d, PartyRole.Tenant)}" : null;

    public string? Subtitle => Detail is { } d
        ? $"{string.Join(", ", d.Units.Select(u => u.Address))} · desde {Formats.Date(d.StartDate)} hasta {Formats.Date(d.NominalEndDate)}"
        : null;

    /// <summary>What the state means for the operator, beside its chip; none for a contract simply in force.</summary>
    public string? StateNote => Detail switch
    {
        null => null,
        { IsCancelled: true } => "La cancelación no se deshace.",
        { Status: ContractStatus.PendingTermination } => "Con aviso de rescisión: sigue vigente hasta que se lo termine.",
        { Status: ContractStatus.Ended } => "Para editar un contrato finalizado: solo honorarios y codeudores.",
        _ => null,
    };

    /// <summary>Each person by role, tenants first, then lessors and codeudores.</summary>
    public IReadOnlyList<DetailLine> PartyLines => Detail is { } d
        ? d.Parties
            .OrderBy(p => p.Role switch { PartyRole.Tenant => 0, PartyRole.Lessor => 1, _ => 2 })
            .Select(p => new DetailLine(Formats.Role(p.Role), p.Name))
            .ToList()
        : [];

    public IReadOnlyList<DetailLine> UnitLines => Detail is { } d
        ? d.Units.Select(u => new DetailLine(u.Address, $"{Formats.Number(u.Percentage)} % del alquiler")).ToList()
        : [];

    /// <summary>The clause's index, interval and window; empty when the contract has none.</summary>
    public IReadOnlyList<DetailLine> ClauseLines => Detail?.Clause is { } c
        ?
        [
            new DetailLine(c.IndexNames.Count == 1 ? "Índice" : "Índices", string.Join(", ", c.IndexNames)),
            new DetailLine("Se ajusta cada", c.IntervalMonths == 1 ? "1 mes" : $"{c.IntervalMonths} meses"),
            new DetailLine("Lectura termina", c.WindowOffsetMonths switch
            {
                0 => "en el mes del vencimiento",
                1 => "1 mes antes del vencimiento",
                var months => $"{months} meses antes del vencimiento",
            }),
        ]
        : [];

    public IReadOnlyList<DetailLine> AmountLines => Detail is { } d
        ?
        [
            new DetailLine("Alquiler actual", Formats.Money(d.MonthlyRent)),
            new DetailLine("Día de vencimiento", d.DueDay.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new DetailLine("Honorarios de administración", d.HonorariosPercentage is { } rate ? $"{Formats.Number(rate)} %" : "Sin cargar"),
            new DetailLine(
                "Honorarios contractuales",
                d.FeePlan is { } plan
                    ? $"{plan.Instalments} {(plan.Instalments == 1 ? "cuota" : "cuotas")} de {Formats.Money(plan.Amount)}"
                    : "Sin plan"),
            new DetailLine("Inicio", Formats.Date(d.StartDate)),
            new DetailLine("Fin", Formats.Date(d.ActualEndDate ?? d.NominalEndDate)),
        ]
        : [];

    /// <summary>When the next adjustment falls due and what it reads; or why there is none.</summary>
    public string? NextAdjustmentText => Detail switch
    {
        null => null,
        { IsCancelled: true } => "Contrato cancelado: no tiene ajustes.",
        { Status: ContractStatus.Ended } => "El contrato terminó: su canon ya no se ajusta ni se corrige.",
        { Clause: null } => "Este contrato no tiene cláusula de ajuste.",
        { Clause: { } c } d => ClauseFormRules.NextAdjustmentText(
            d.StartDate,
            c.IntervalMonths,
            c.WindowOffsetMonths,
            d.Adjustments.Count == 0 ? null : d.Adjustments.Max(a => a.EffectiveDate),
            d.CutoverDate),
    };

    private static string Names(ContractDetail detail, PartyRole role) =>
        string.Join(" y ", detail.Parties.Where(p => p.Role == role).Select(p => p.Name));

    partial void OnDetailChanged(ContractDetail? value)
    {
        Events.Clear();
        foreach (var line in value?.Events ?? [])
        {
            Events.Add(new ContractEventRow(line));
        }

        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(ActionCardLines));
        OpenNoticeCommand.NotifyCanExecuteChanged();
        OpenUndoNoticeCommand.NotifyCanExecuteChanged();
        OpenEndCommand.NotifyCanExecuteChanged();
        OpenUndoEndCommand.NotifyCanExecuteChanged();
        OpenCancelCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private DateOnly? _noticeDate;

    [ObservableProperty]
    private DateOnly? _moveOutDate;

    [ObservableProperty]
    private EndReason? _endReason;

    [ObservableProperty]
    private DateOnly? _endDate;

    /// <summary>Why a cancellation or an undo is being made, in the operator's words. Kept with who did it and when.</summary>
    [ObservableProperty]
    private string _changeReason = string.Empty;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>The summary waiting to be confirmed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummaryOpen))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private PendingContractChange? _pendingChange;

    public bool IsSummaryOpen => PendingChange is not null;

    partial void OnNoticeDateChanged(DateOnly? value) => CloseSummary();

    partial void OnMoveOutDateChanged(DateOnly? value) => CloseSummary();

    partial void OnEndReasonChanged(EndReason? value)
    {
        CloseSummary();
        EndReasonError = null;
    }

    partial void OnEndDateChanged(DateOnly? value) => CloseSummary();

    partial void OnChangeReasonChanged(string value)
    {
        CloseSummary();
        ChangeReasonError = null;
    }

    private bool CanAct() => !Runner.IsBusy;

    private void CloseSummary() => PendingChange = null;

    private async Task LoadAsync()
    {
        var outcome = await Runner.RunAsync(() => _contracts.DetailAsync(_contractId));
        if (!outcome.Completed)
        {
            return;
        }

        Detail = outcome.Value;
        if (Detail is { } detail)
        {
            await Adjustments.ShowAsync(_contractId, canCorrect: !detail.IsCancelled);
        }
    }

    /// <summary>Nothing to fetch: the summary repeats the dates, and the port applies the rules again on confirming.</summary>
    private void ReviewNotice()
    {
        RefusalMessage = null;
        CloseSummary();

        NoticeDateError = NoticeDate is null ? MissingNoticeDates : null;
        MoveOutDateError = MoveOutDate is null ? MissingNoticeDates : null;
        if (NoticeDate is not { } notice || MoveOutDate is not { } moveOut)
        {
            return;
        }

        // The port's own rules (ContractLifecycleRules.CheckNotice), each said under its date and both at once. The notice's
        // are asked with the move-out on the same day, so only the move-out's own rule speaks for that field.
        if (Detail is { } detail)
        {
            var noticeRefusal = ContractLifecycleRules.CheckNotice(detail.Status, detail.StartDate, notice, notice, _clock.Today);
            if (noticeRefusal == ContractRefusal.NoticeNeedsActive)
            {
                RefusalMessage = noticeRefusal.Value.OperatorText();
                return;
            }

            NoticeDateError = noticeRefusal?.OperatorText();
            MoveOutDateError = moveOut < notice ? ContractRefusal.MoveOutBeforeNotice.OperatorText() : null;
            if (NoticeDateError is not null || MoveOutDateError is not null)
            {
                return;
            }
        }

        PendingChange = new PendingNotice(notice, moveOut);
    }

    private async Task ReviewEndAsync()
    {
        RefusalMessage = null;
        CloseSummary();

        // Each field's own errors, both at once and before the port is asked (canvas "Terminar contrato"): the reason, then
        // the date against the port's own rules (ContractLifecycleRules.CheckEnd), asked with a reason so only the date speaks.
        EndReasonError = EndReason is null ? ContractRefusal.EndReasonMissing.OperatorText() : null;
        EndDateError = EndDate is null ? MissingEndDate : null;
        if (EndDate is { } typed && Detail is { } detail)
        {
            var dateRefusal = ContractLifecycleRules.CheckEnd(
                detail.Status, detail.StartDate, EndReason ?? Domain.Leasing.EndReason.Expiry, typed, _clock.Today);
            if (dateRefusal is ContractRefusal.EndAfterToday or ContractRefusal.EndBeforeContractStart)
            {
                EndDateError = dateRefusal.Value.OperatorText();
            }
        }

        if (EndReasonError is not null || EndDateError is not null || EndDate is not { } endDate)
        {
            return;
        }

        var reason = EndReason;
        var outcome = await Runner.RunAsync(() => _contracts.SummarizeEndAsync(_contractId, reason, endDate));
        if (!outcome.Completed)
        {
            return;
        }

        var summary = outcome.Value!;
        if (summary.Refusal is { } refusal)
        {
            RefusalMessage = refusal.OperatorText();
            return;
        }

        // What was typed while this was out is not what the summary describes.
        if (EndReason == reason && EndDate == endDate)
        {
            PendingChange = new PendingEnd(reason!.Value, endDate, summary);
        }
    }

    /// <summary>Nothing to fetch: the summary repeats the dates being cleared, and the port applies the rules again on confirming.</summary>
    private void ReviewUndoNotice()
    {
        RefusalMessage = null;
        CloseSummary();

        if (string.IsNullOrWhiteSpace(ChangeReason))
        {
            ChangeReasonError = ContractRefusal.ReasonMissing.OperatorText();
            return;
        }

        PendingChange = new PendingUndoNotice(ChangeReason, Detail?.NoticeGivenDate, Detail?.PlannedMoveOutDate);
    }

    private async Task ReviewUndoEndAsync()
    {
        RefusalMessage = null;
        CloseSummary();

        // Canvas "Deshacer fin": a missing reason is said under its field, before the port is asked.
        if (string.IsNullOrWhiteSpace(ChangeReason))
        {
            ChangeReasonError = ContractRefusal.ReasonMissing.OperatorText();
            return;
        }

        var reason = ChangeReason;
        var outcome = await Runner.RunAsync(() => _contracts.SummarizeUndoEndAsync(_contractId, reason));
        if (!outcome.Completed)
        {
            return;
        }

        var summary = outcome.Value!;
        if (summary.Refusal is { } refusal)
        {
            RefusalMessage = refusal.OperatorText(summary.CurrentTenant);
        }
        else if (ChangeReason == reason)
        {
            PendingChange = new PendingUndoEnd(reason, summary);
        }
    }

    private async Task ReviewCancelAsync()
    {
        RefusalMessage = null;
        CloseSummary();

        // Canvas "Cancelar por error": a missing reason is said under its field, before the port is asked.
        if (string.IsNullOrWhiteSpace(ChangeReason))
        {
            ChangeReasonError = ContractRefusal.ReasonMissing.OperatorText();
            return;
        }

        var reason = ChangeReason;
        var outcome = await Runner.RunAsync(() => _contracts.SummarizeCancelAsync(_contractId, reason));
        if (!outcome.Completed)
        {
            return;
        }

        var summary = outcome.Value!;
        if (summary.Refusal is not null)
        {
            RefusalMessage = summary.RefusalText;
        }
        else if (ChangeReason == reason)
        {
            PendingChange = new PendingCancel(reason, summary);
        }
    }

    private async Task ConfirmAsync()
    {
        RefusalMessage = null;
        var pending = PendingChange;

        var outcome = await Runner.RunAsync(
            () => pending switch
            {
                PendingNotice notice => _contracts.GiveNoticeAsync(_contractId, notice.NoticeDate, notice.MoveOutDate),
                PendingEnd end => _contracts.EndAsync(_contractId, end.Reason, end.EndDate),
                PendingUndoNotice undo => _contracts.UndoNoticeAsync(_contractId, undo.Reason),
                PendingUndoEnd undo => _contracts.UndoEndAsync(_contractId, undo.Reason),
                PendingCancel cancel => _contracts.CancelAsync(_contractId, cancel.Reason, cancel.Figures),
                _ => throw new InvalidOperationException("Nothing to confirm."),
            },
            OperationKind.Commit);
        if (!outcome.Completed)
        {
            return;
        }

        CloseSummary();
        if (outcome.Value!.Refusal is { } refusal)
        {
            RefusalMessage = outcome.Value.RefusalText ?? refusal.OperatorText(outcome.Value.CurrentTenant);
        }
        else if (pending is PendingNotice landed && Detail is { } before)
        {
            Done = new ActionDone(
                "Aviso registrado",
                $"El contrato de {Names(before, PartyRole.Tenant)} quedó En rescisión. La desocupación está prevista para el {Formats.Date(landed.MoveOutDate)}.");
        }
        else if (pending is PendingEnd ended && Detail is { } closing)
        {
            Done = new ActionDone(
                "Contrato terminado",
                $"El contrato de {Names(closing, PartyRole.Tenant)} terminó el {Formats.Date(ended.EndDate)}. Si fue un error, se puede deshacer.");
        }
        else if (pending is PendingUndoNotice && Detail is { } undone)
        {
            Done = new ActionDone("Listo", $"El aviso se deshizo. El contrato de {Names(undone, PartyRole.Tenant)} vuelve a estar Vigente.");
        }
        else if (pending is PendingUndoEnd && Detail is { } reopened)
        {
            Done = new ActionDone("Listo", $"El fin se deshizo. El contrato de {Names(reopened, PartyRole.Tenant)} vuelve a estar Vigente.");
        }
        else if (pending is PendingCancel && Detail is { } cancelled)
        {
            Done = new ActionDone(
                "Contrato cancelado",
                $"El contrato de {Names(cancelled, PartyRole.Tenant)} quedó cancelado por error. Sigue visible en la lista, con su motivo.");
        }

        await LoadAsync();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            ReviewNoticeCommand.NotifyCanExecuteChanged();
            ReviewEndCommand.NotifyCanExecuteChanged();
            ReviewUndoNoticeCommand.NotifyCanExecuteChanged();
            ReviewUndoEndCommand.NotifyCanExecuteChanged();
            ReviewCancelCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            OpenNoticeCommand.NotifyCanExecuteChanged();
            OpenUndoNoticeCommand.NotifyCanExecuteChanged();
            OpenEndCommand.NotifyCanExecuteChanged();
            OpenUndoEndCommand.NotifyCanExecuteChanged();
            OpenCancelCommand.NotifyCanExecuteChanged();
            CloseActionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>What the form says when a date was left empty. The port's rules take over once both are there.</summary>
    public const string MissingNoticeDates = "Escriba la fecha del aviso y la de desocupación.";

    /// <summary>What the form says when the end date was left empty.</summary>
    public const string MissingEndDate = "Escriba la fecha de fin del contrato.";
}

/// <summary>A reason offered for ending, with its name.</summary>
public sealed record EndReasonOption(EndReason Reason, string Text);

/// <summary>An action of the contract with its own screen, shown in place of the detail (canvas "Registrar aviso" and the rest).</summary>
public enum ContractAction
{
    Notice,
    UndoNotice,
    End,
    UndoEnd,
    Cancel,
}

/// <summary>The notice an action ends on when it lands: its title and its sentence.</summary>
public sealed record ActionDone(string Title, string Text);

/// <summary>The detail's tabs, as on the canvas.</summary>
public enum ContractDetailTab
{
    Summary,
    Adjustments,
    Events,
}

/// <summary>One line of a card on the detail's Resumen: what it is, and its value.</summary>
public sealed record DetailLine(string Label, string Value);

/// <summary>One line of the contract's event log, as the screen reads it.</summary>
public sealed class ContractEventRow
{
    public ContractEventRow(ContractEventLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;
    }

    public ContractEventLine Line { get; }

    /// <summary>The agency's day, whatever offset it was written with.</summary>
    public string WhenText => Formats.Date(DateOnly.FromDateTime(Line.RecordedAt.ToLocalTime().DateTime));

    public string EventText => Line.Kind switch
    {
        ContractEventKind.Cancelled => "Cancelado por error",
        ContractEventKind.NoticeUndone => "Aviso deshecho",
        ContractEventKind.EndUndone => "Fin deshecho",
        ContractEventKind.PartyRemoved => "Persona quitada del contrato",
        _ => throw new ArgumentOutOfRangeException(nameof(Line), Line.Kind, "An event without a name."),
    };

    public string ReasonText => Line.Reason ?? string.Empty;

    public string WhoText => Line.RecordedBy;
}

/// <summary>What the operator is about to confirm.</summary>
public abstract record PendingContractChange;

/// <param name="NoticeDate">The date notice was given.</param>
/// <param name="MoveOutDate">The planned move-out.</param>
public sealed record PendingNotice(DateOnly NoticeDate, DateOnly MoveOutDate) : PendingContractChange
{
    public string Summary => $"El contrato pasa a rescisión avisada el {NoticeDate:dd/MM/yyyy}, con desocupación el {MoveOutDate:dd/MM/yyyy}.";
}

/// <param name="Reason">Why, in the operator's words.</param>
/// <param name="NoticeDate">The notice date being cleared.</param>
/// <param name="MoveOutDate">The move-out date being cleared.</param>
public sealed record PendingUndoNotice(string Reason, DateOnly? NoticeDate, DateOnly? MoveOutDate) : PendingContractChange
{
    public string Summary => $"Se deshace el aviso del {NoticeDate:dd/MM/yyyy}, con desocupación el {MoveOutDate:dd/MM/yyyy}. El contrato vuelve a estar activo.";
}

/// <param name="Reason">Why, in the operator's words.</param>
/// <param name="Figures">What undoing leaves, as the port read it.</param>
public sealed record PendingUndoEnd(string Reason, ContractUndoEndSummary Figures) : PendingContractChange
{
    /// <summary>The months between the end and today accrue as usual, and the operator is told which (spec Requirement 24).</summary>
    public string Summary => Figures.PeriodsToAccrue.Count == 0
        ? "Se deshace el fin del contrato. No hay períodos para cobrar por el tiempo que estuvo terminado."
        : $"Se deshace el fin del contrato. Estos períodos van a cargarse a la cuenta: {string.Join(", ", Figures.PeriodsToAccrue.Select(p => p.ToString("MM/yyyy")))}.";
}

/// <param name="Reason">Why, in the operator's words.</param>
/// <param name="Figures">What cancelling means, as the port read it. Sent back on confirming, and compared.</param>
public sealed record PendingCancel(string Reason, ContractCancellationSummary Figures) : PendingContractChange
{
    /// <summary>The tenant, the units and the exact amount, since this cannot be undone (spec Requirement 23).</summary>
    public string Summary =>
        $"Se cancela el contrato de {Figures.Tenants}, sobre {Figures.Units}. Deja de deberse {Formats.Money(Figures.AmountStoppedBeingOwed)}.";

    /// <summary>What stops being owed, as the summary's card shows it.</summary>
    public string AmountText => Formats.Money(Figures.AmountStoppedBeingOwed);

    /// <summary>
    /// How what stops being owed is cleared, or null when nothing is. The canvas also names the period's due date; the summary
    /// carries only the total, so that waits for a later change (Tomás, 2026-11-20).
    /// </summary>
    public string? CompensationLine => Figures.AmountStoppedBeingOwed == 0m
        ? null
        : "Se compensa con una corrección, sin recargo. Los movimientos anteriores no se tocan.";

    /// <summary>What cancelling does to the contract, its unit and its people (canvas "Cancelar por error").</summary>
    public const string EffectsLine =
        "El contrato deja de generar períodos, también los que todavía no se cargaron. La propiedad queda disponible. " +
        "Las personas y la propiedad no se borran. El contrato sigue visible, marcado como cancelado y con su motivo.";

    /// <summary>Said after "Esto es definitivo.", which the screen sets in bold.</summary>
    public const string FinalityLine = "Una vez cancelado, el contrato no se puede recuperar.";

    /// <summary>Money past the voiding window, or null when there is none: it stays with its recibos and is returned by hand (spec Decision 36).</summary>
    public string? RetainedLine => Figures.RetainedRecibos.Count == 0
        ? null
        : $"Los recibos {ContractCancellationSummary.ListRecibos(Figures.RetainedRecibos)} quedan como están, por {Formats.Money(Figures.RetainedAmount)}: ese dinero se devuelve a mano.";
}

/// <param name="Reason">Why it ends.</param>
/// <param name="EndDate">When it ends.</param>
/// <param name="Figures">What ending leaves, as the port read it.</param>
public sealed record PendingEnd(EndReason Reason, DateOnly EndDate, ContractEndSummary Figures) : PendingContractChange
{
    /// <summary>What the summary says when ending leaves no debt.</summary>
    public const string NothingOwed = "No queda nada adeudado.";

    /// <summary>The reason in the operator's words, as the summary names it.</summary>
    public string ReasonText => Formats.EndReason(Reason);

    /// <summary>The amount owed; with invalid recargo settings, the ledger's balance, saying so. Debt never stops the end (spec Requirement 10).</summary>
    public string OwedLine => (Figures.AmountOwed ?? Figures.LedgerBalance) == 0m
        ? NothingOwed
        : Figures.AmountOwed is { } owed
        ? $"Queda adeudado {Formats.Money(owed)}, que se puede seguir cobrando."
        : $"Queda adeudado {Formats.Money(Figures.LedgerBalance)} sin contar recargo, que no se puede calcular con la configuración actual. Se puede seguir cobrando.";

    /// <summary>Periods on the ledger due after the end date, or null when there are none: they stay, and the operator is told.</summary>
    public string? AlreadyWrittenLine => Figures.PeriodsAlreadyWritten.Count == 0
        ? null
        : $"Estos períodos, con vencimiento posterior a la fecha de fin, ya están en la cuenta y quedan: {string.Join(", ", Figures.PeriodsAlreadyWritten.Select(p => p.ToString("MM/yyyy")))}.";

    /// <summary>The month the end leaves uncharged, or null (spec Decision 39, Requirement 10): a new contract can start on its 1st.</summary>
    public string? UnchargedMonthLine => Figures.MonthLeftUncharged is not { } month
        ? null
        : $"El período {month.ToString("MM/yyyy")} vence después de la fecha de fin, así que este contrato no lo cobra. Un contrato nuevo sobre la misma unidad puede empezar el {month.ToString("dd/MM/yyyy")}.";
}
