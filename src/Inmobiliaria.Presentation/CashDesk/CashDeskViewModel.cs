using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>Where the desk is in a payment (design section 7.1).</summary>
public enum DeskState
{
    NoAccount,
    Owed,
    Ready,
    Summary,
    Committing,
    Issued,
    Refused,
    Stale,
    Failed,
}

/// <summary>The exact preview and request the summary was built from; the commit sends this pair.</summary>
public sealed record DeskSummary(CollectionPreview Preview, CollectionRequest Request);

/// <summary>
/// The cash desk (spec Requirements 7 to 11, design sections 7.1 to 7.4). Every figure on screen comes from the
/// domain's preview; nothing here computes money. Choosing an account previews it with a handed amount of 0
/// (the "what is owed" step); any input change previews again, latest-wins (D23). The commit sends the exact
/// pair the summary showed, once, and never retries: a stale or concurrent refusal previews once more and
/// shows what changed. Every operation goes through <see cref="OperationRunner"/>.
/// </summary>
public sealed partial class CashDeskViewModel : ObservableObject
{
    private readonly ICollectionDesk _desk;
    private readonly IDueAdjustmentQuery _adjustments;
    private readonly IReciboReader _reader;
    private readonly IAgencyClock _clock;

    private IReadOnlyList<string> _adjustmentWarnings = [];
    private CollectionRequest? _previewRequest;
    private CancellationTokenSource? _previewCancellation;
    private int _previewVersion;
    private bool _resettingInputs;
    private bool _committing;
    private bool _inputsChangedDuringCommit;

    public CashDeskViewModel(
        ICollectionDesk desk,
        IAccountDirectory directory,
        IDueAdjustmentQuery adjustments,
        IReciboReader reader,
        IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(adjustments);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(clock);

        _desk = desk;
        _adjustments = adjustments;
        _reader = reader;
        _clock = clock;

        Picker = new AccountPickerViewModel(directory, ChooseAccountAsync, () => !_committing);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, CanContinue);
        CommitCommand = new AsyncRelayCommand(CommitAsync, CanCommit);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public AccountPickerViewModel Picker { get; }

    public OperationRunner Runner { get; } = new();

    public IAsyncRelayCommand ContinueCommand { get; }

    public IAsyncRelayCommand CommitCommand { get; }

    /// <summary>The preview the latest input change started; tests and the view can await it.</summary>
    public Task PreviewInFlight { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private DeskState _state = DeskState.NoAccount;

    [ObservableProperty]
    private AccountListing? _account;

    // ---- Inputs: each change withdraws the summary and previews again ----

    /// <summary>The month to settle, or null for the domain's default. Only the year and month are used.</summary>
    [ObservableProperty]
    private DateTime? _namedPeriod;

    /// <summary>A received date the operator typed; null means the agency's today (D21).</summary>
    [ObservableProperty]
    private DateTime? _receivedOnInput;

    [ObservableProperty]
    private string _tasaMunicipalText = string.Empty;

    [ObservableProperty]
    private string _seguroText = string.Empty;

    [ObservableProperty]
    private string _otrosConceptosText = string.Empty;

    /// <summary>Starts empty and is never pre-filled (spec Requirement 8).</summary>
    [ObservableProperty]
    private string _handedAmountText = string.Empty;

    // ---- What the screen shows ----

    [ObservableProperty]
    private CollectionPreview? _preview;

    [ObservableProperty]
    private DeskSummary? _summary;

    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>A typed value that is not a number, named; no preview is requested while it stands.</summary>
    [ObservableProperty]
    private string? _inputProblem;

    [ObservableProperty]
    private IReadOnlyList<string> _warnings = [];

    [ObservableProperty]
    private IReadOnlyList<DifferenceLine> _differences = [];

    [ObservableProperty]
    private string? _staleMessage;

    [ObservableProperty]
    private string? _issuedText;

    [ObservableProperty]
    private ReciboViewModel? _recibo;

    /// <summary>The unset starting number, from readiness (D8): it blocks the summary before any commit.</summary>
    public string? ReadinessBlock => Account is { Readiness.StartingNumberSet: false }
        ? CollectionRefusal.CutoverNotSet.OperatorText()
        : null;

    public string PeriodText => Preview?.Period is { } period ? Formats.Period(period) : string.Empty;

    public string ReceivedOnText => Preview is { } p ? Formats.Date(p.ReceivedOn) : string.Empty;

    public string PreviousAmountOwedText => Preview is { } p ? Formats.Money(p.PreviousAmountOwed) : string.Empty;

    public string LedgerFigureText => Preview is { } p ? Formats.Money(p.LedgerFigure) : string.Empty;

    public string ResultingAmountOwedText => Preview is { } p ? Formats.Money(p.ResultingAmountOwed) : string.Empty;

    public IReadOnlyList<ReciboLineView> LocatarioLines => ReciboLineView.Of(Preview?.Locatario?.Lines ?? []);

    public IReadOnlyList<ReciboLineView> PropietarioLines => ReciboLineView.Of(Preview?.Propietario?.Lines ?? []);

    public string LocatarioTotalText => Preview?.Locatario is { } d ? Formats.Money(d.Total) : string.Empty;

    public string PropietarioTotalText => Preview?.Propietario is { } d ? Formats.Money(d.Total) : string.Empty;

    private bool HandedTyped => !string.IsNullOrWhiteSpace(HandedAmountText);

    /// <summary>Called by the picker. Starts over on the new account: inputs cleared, adjustments read once, then the owed step.</summary>
    public async Task ChooseAccountAsync(AccountListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        // The commit running belongs to the account on screen; another one waits until it ends (S3b review).
        if (_committing)
        {
            return;
        }

        // Whatever the previous account still has in flight is for the previous account: it can never land
        // here, not even while the adjustments below are being read (S3b review).
        ++_previewVersion;
        _previewCancellation?.Cancel();

        _resettingInputs = true;
        NamedPeriod = null;
        ReceivedOnInput = null;
        TasaMunicipalText = string.Empty;
        SeguroText = string.Empty;
        OtrosConceptosText = string.Empty;
        HandedAmountText = string.Empty;
        _resettingInputs = false;

        Account = listing;
        Summary = null;
        Preview = null;
        _previewRequest = null;
        RefusalMessage = null;
        InputProblem = null;
        Differences = [];
        StaleMessage = null;
        IssuedText = null;
        Recibo = null;
        State = DeskState.NoAccount;

        // Once per selection, not per preview (D9).
        var due = await Runner.RunAsync(() => _adjustments.GetDueAsync(_clock.Today));
        // A failed check is said, not hidden: the runner's message would be cleared by the preview below.
        _adjustmentWarnings = due.Completed
            ? AdjustmentWarning.For(due.Value!, listing.ContractId)
            : [DeskTexts.AdjustmentCheckFailed];

        PreviewInFlight = RefreshPreviewAsync();
        await PreviewInFlight;
    }

    partial void OnNamedPeriodChanged(DateTime? value) => OnInputChanged();

    partial void OnReceivedOnInputChanged(DateTime? value) => OnInputChanged();

    partial void OnTasaMunicipalTextChanged(string value) => OnInputChanged();

    partial void OnSeguroTextChanged(string value) => OnInputChanged();

    partial void OnOtrosConceptosTextChanged(string value) => OnInputChanged();

    partial void OnHandedAmountTextChanged(string value) => OnInputChanged();

    partial void OnAccountChanged(AccountListing? value) => OnPropertyChanged(nameof(ReadinessBlock));

    partial void OnPreviewChanged(CollectionPreview? value)
    {
        foreach (var name in new[]
                 {
                     nameof(PeriodText), nameof(ReceivedOnText), nameof(PreviousAmountOwedText), nameof(LedgerFigureText),
                     nameof(ResultingAmountOwedText), nameof(LocatarioLines), nameof(PropietarioLines),
                     nameof(LocatarioTotalText), nameof(PropietarioTotalText),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void OnInputChanged()
    {
        if (_resettingInputs || Account is null)
        {
            return;
        }

        // During a commit nothing is previewed: a result landing after it would undo what the commit shows.
        // The change is picked up when the commit ends, unless the payment was issued (S3b review).
        if (_committing)
        {
            _inputsChangedDuringCommit = true;
            return;
        }

        // The summary was built from the old inputs; it goes the moment one changes (spec test 15).
        Summary = null;
        RefreshCommands();
        PreviewInFlight = RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (Account is not { } account)
        {
            return;
        }

        var version = ++_previewVersion;
        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;

        // The inputs just changed, so the preview on screen no longer answers them: nothing can be
        // continued from it until a preview of these inputs arrives (S3b review).
        _previewRequest = null;
        RefreshCommands();

        if (!TryBuildRequest(account, out var request, out var problem))
        {
            InputProblem = problem;
            Preview = null;
            _previewRequest = null;
            RefusalMessage = null;
            State = DeskState.Owed;
            RefreshCommands();
            return;
        }

        InputProblem = null;
        var outcome = await Runner.RunAsync(async () =>
        {
            try
            {
                return await _desk.PreviewAsync(request, token);
            }
            catch (Exception) when (version != _previewVersion)
            {
                // Superseded: its failure is not the operator's business either (S3b review).
                throw new OperationCanceledException();
            }
        });

        // Latest wins: a result for inputs the operator has since changed is dropped (D23).
        if (version != _previewVersion)
        {
            return;
        }

        if (!outcome.Completed)
        {
            // The runner already shows the generic message. The old figures go too: they answered other inputs.
            Preview = null;
            RefusalMessage = null;
            State = DeskState.Owed;
            RefreshCommands();
            return;
        }

        ApplyPreview(outcome.Value!, request);
    }

    private bool TryBuildRequest(AccountListing account, out CollectionRequest request, out string? problem)
    {
        request = null!;
        problem = null;

        if (!Formats.TryParseMoney(TasaMunicipalText, out var tasa))
        {
            problem = "La tasa municipal no es un importe válido.";
        }
        else if (!Formats.TryParseMoney(SeguroText, out var seguro))
        {
            problem = "El seguro no es un importe válido.";
        }
        else if (!Formats.TryParseMoney(OtrosConceptosText, out var otros))
        {
            problem = "El importe de otros conceptos no es válido.";
        }
        else if (!Formats.TryParseMoney(HandedAmountText, out var handed))
        {
            problem = "El importe entregado no es un importe válido.";
        }
        else
        {
            request = new CollectionRequest(
                account.AccountId,
                NamedPeriod is { } p ? new DateOnly(p.Year, p.Month, 1) : null,
                ReceivedOnInput is { } r ? DateOnly.FromDateTime(r) : null,
                handed ?? 0m,
                tasa,
                seguro,
                otros);
            return true;
        }

        return false;
    }

    private void ApplyPreview(CollectionPreview preview, CollectionRequest request)
    {
        Preview = preview;
        _previewRequest = request;

        // At the owed step "not the total" is the expected answer, not an error (spec test 12).
        RefusalMessage = preview.Refusal switch
        {
            null => null,
            CollectionRefusal.AmountNotTotal when !HandedTyped => null,
            var refusal => refusal.Value.OperatorText(),
        };

        Warnings = preview.Warnings.Select(DeskTexts.Warning).Concat(_adjustmentWarnings).ToList();
        State = preview.CanCommit && HandedTyped ? DeskState.Ready : DeskState.Owed;
        RefreshCommands();
    }

    private bool CanContinue() =>
        !Runner.IsBusy &&
        Summary is null &&
        State is not (DeskState.Issued or DeskState.Committing) &&
        Preview is { CanCommit: true } &&
        _previewRequest is not null &&
        HandedTyped &&
        InputProblem is null &&
        ReadinessBlock is null;

    private Task ContinueAsync()
    {
        if (CanContinue())
        {
            Summary = new DeskSummary(Preview!, _previewRequest!);
            Differences = [];
            StaleMessage = null;
            State = DeskState.Summary;
            RefreshCommands();
        }

        return Task.CompletedTask;
    }

    private bool CanCommit() => !Runner.IsBusy && !_committing && Summary is not null;

    private async Task CommitAsync()
    {
        // The command already refuses a second run; this keeps a direct second call from sending twice.
        if (!CanCommit())
        {
            return;
        }

        var summary = Summary!;
        _committing = true;
        _inputsChangedDuringCommit = false;

        // No preview started before the commit may land after it.
        ++_previewVersion;
        _previewCancellation?.Cancel();

        RefusalMessage = null;
        State = DeskState.Committing;
        RefreshCommands();

        try
        {
            var outcome = await Runner.RunAsync(() => _desk.CommitAsync(summary.Preview, summary.Request), OperationKind.Commit);
            Summary = null;

            if (!outcome.Completed)
            {
                // The runner already says it may or may not have been recorded; the inputs stay as typed.
                State = DeskState.Failed;
                return;
            }

            var result = outcome.Value!;
            switch (result.Refusal)
            {
                case null:
                    await ShowIssuedAsync(result);
                    break;

                case CollectionRefusal.PreviewStale or CollectionRefusal.ConcurrentChange:
                    await ShowStaleAsync(summary, result.Refusal.Value);
                    break;

                default:
                    RefusalMessage = result.Refusal.Value.OperatorText();
                    State = DeskState.Refused;
                    break;
            }
        }
        finally
        {
            _committing = false;
            RefreshCommands();

            if (_inputsChangedDuringCommit && State != DeskState.Issued)
            {
                PreviewInFlight = RefreshPreviewAsync();
            }

            _inputsChangedDuringCommit = false;
        }
    }

    private async Task ShowIssuedAsync(CollectionResult result)
    {
        IssuedText = $"Recibo locatario N° {result.LocatarioNumber} · Recibo propietario N° {result.PropietarioNumber}";
        State = DeskState.Issued;

        // Nothing prints (spec Requirement 12); the recibos are read back as issued and shown.
        var read = await Runner.RunAsync(() => _reader.GetByCollectionAsync(result.CollectionId!.Value));
        if (read is { Completed: true, Value: { } payment })
        {
            Recibo = new ReciboViewModel(payment);
        }
    }

    /// <summary>One fresh preview of the same request, the difference, and no retry (spec Requirement 10).</summary>
    private async Task ShowStaleAsync(DeskSummary confirmed, CollectionRefusal refusal)
    {
        StaleMessage = refusal.OperatorText();

        // Inside latest-wins like any preview: if something newer started meanwhile, it wins.
        var version = ++_previewVersion;
        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;

        var fresh = await Runner.RunAsync(() => _desk.PreviewAsync(confirmed.Request, token));
        if (version != _previewVersion)
        {
            return;
        }

        if (!fresh.Completed)
        {
            // The rejected pair must not be offered again as if nothing happened.
            Preview = null;
            _previewRequest = null;
            State = DeskState.Stale;
            return;
        }

        Differences = PreviewDifference.Between(confirmed.Preview, fresh.Value!);
        ApplyPreview(fresh.Value!, confirmed.Request);
        State = DeskState.Stale;
    }

    private void RefreshCommands()
    {
        ContinueCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
        Picker.ChooseCommand.NotifyCanExecuteChanged();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            RefreshCommands();
        }
    }
}
