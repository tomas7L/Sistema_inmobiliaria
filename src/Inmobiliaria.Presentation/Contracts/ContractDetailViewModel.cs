using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// One contract, with notice, end, their undoing and cancelling (gestion-de-alquileres spec Requirements 6, 10, 23 and 24, design §6). Each of them opens a summary
/// that the operator confirms, and editing what the summary was built from closes it, so what is confirmed is what was read. The
/// contract is read again after every action. Both roles; the view arrives in S12.
/// </summary>
public sealed partial class ContractDetailViewModel : ObservableObject
{
    private readonly IContractRegistry _contracts;
    private readonly Guid _contractId;

    public ContractDetailViewModel(IContractRegistry contracts, Guid contractId)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        _contractId = contractId;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        ReviewNoticeCommand = new RelayCommand(ReviewNotice, CanAct);
        ReviewEndCommand = new AsyncRelayCommand(ReviewEndAsync, CanAct);
        ReviewUndoNoticeCommand = new RelayCommand(ReviewUndoNotice, CanAct);
        ReviewUndoEndCommand = new AsyncRelayCommand(ReviewUndoEndAsync, CanAct);
        ReviewCancelCommand = new AsyncRelayCommand(ReviewCancelAsync, CanAct);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && PendingChange is not null);
        CancelCommand = new RelayCommand(CloseSummary, CanAct);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public IAsyncRelayCommand LoadCommand { get; }

    public IRelayCommand ReviewNoticeCommand { get; }

    public IAsyncRelayCommand ReviewEndCommand { get; }

    public IRelayCommand ReviewUndoNoticeCommand { get; }

    public IAsyncRelayCommand ReviewUndoEndCommand { get; }

    public IAsyncRelayCommand ReviewCancelCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>The reasons offered for ending. "Cargado por error" never is: only cancelling uses it (spec Requirement 10).</summary>
    public IReadOnlyList<EndReason> EndReasons { get; } = Enum.GetValues<EndReason>().Where(r => r != Domain.Leasing.EndReason.LoadedByMistake).ToList();

    /// <summary>The contract as last read, or null before that or when it is not there.</summary>
    [ObservableProperty]
    private ContractDetail? _detail;

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

    partial void OnEndReasonChanged(EndReason? value) => CloseSummary();

    partial void OnEndDateChanged(DateOnly? value) => CloseSummary();

    partial void OnChangeReasonChanged(string value) => CloseSummary();

    private bool CanAct() => !Runner.IsBusy;

    private void CloseSummary() => PendingChange = null;

    private async Task LoadAsync()
    {
        var outcome = await Runner.RunAsync(() => _contracts.DetailAsync(_contractId));
        if (outcome.Completed)
        {
            Detail = outcome.Value;
        }
    }

    /// <summary>Nothing to fetch: the summary repeats the dates, and the port applies the rules again on confirming.</summary>
    private void ReviewNotice()
    {
        RefusalMessage = null;
        CloseSummary();

        if (NoticeDate is not { } notice || MoveOutDate is not { } moveOut)
        {
            RefusalMessage = MissingNoticeDates;
            return;
        }

        PendingChange = new PendingNotice(notice, moveOut);
    }

    private async Task ReviewEndAsync()
    {
        RefusalMessage = null;
        CloseSummary();

        if (EndDate is not { } endDate)
        {
            RefusalMessage = MissingEndDate;
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
            RefusalMessage = ContractRefusal.ReasonMissing.OperatorText();
            return;
        }

        PendingChange = new PendingUndoNotice(ChangeReason, Detail?.NoticeGivenDate, Detail?.PlannedMoveOutDate);
    }

    private async Task ReviewUndoEndAsync()
    {
        RefusalMessage = null;
        CloseSummary();

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
        }
    }

    /// <summary>What the form says when a date was left empty. The port's rules take over once both are there.</summary>
    public const string MissingNoticeDates = "Escriba la fecha del aviso y la de desocupación.";

    /// <summary>What the form says when the end date was left empty.</summary>
    public const string MissingEndDate = "Escriba la fecha de fin del contrato.";
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
        $"Se cancela el contrato de {Figures.Tenants}, sobre {Figures.Units}. Deja de deberse {Formats.Money(Figures.AmountStoppedBeingOwed)}. Esto no se puede deshacer.";

    /// <summary>Money past the voiding window, or null when there is none: it stays with its recibos and is returned by hand (spec Decision 36).</summary>
    public string? RetainedLine => Figures.RetainedRecibos.Count == 0
        ? null
        : $"Los recibos {string.Join(", ", Figures.RetainedRecibos)} quedan como están, por {Formats.Money(Figures.RetainedAmount)}: ese dinero se devuelve a mano.";
}

/// <param name="Reason">Why it ends.</param>
/// <param name="EndDate">When it ends.</param>
/// <param name="Figures">What ending leaves, as the port read it.</param>
public sealed record PendingEnd(EndReason Reason, DateOnly EndDate, ContractEndSummary Figures) : PendingContractChange
{
    /// <summary>What the summary says when ending leaves no debt.</summary>
    public const string NothingOwed = "No queda nada adeudado.";

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
}
