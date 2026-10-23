using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Printing;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Presentation.Voiding;

/// <summary>
/// The voiding screen (spec Requirement 16, design section 8): find the pair by either printed number or from
/// the recent list, type a reason, preview, confirm. The pre-gate offers or withholds by age and role; the
/// desk's refusals decide. A stale voiding previews once more and waits for a new confirmation; the voiding is
/// never repeated on its own. After a voiding the pair is read again and shown annulled. Nothing prints.
/// </summary>
public sealed partial class VoidingViewModel : ObservableObject
{
    private readonly ICollectionDesk _desk;
    private readonly IReciboReader _reader;
    private readonly IAgencyClock _clock;
    private readonly UserRole _role;
    private readonly ReciboPrinting? _printing;
    private bool _voiding;

    // Bumped whenever another pair is looked up or opened: a preview still in flight for the previous pair is
    // then dropped, so it can never be confirmed under the new one (S4a review).
    private int _pairVersion;

    public VoidingViewModel(
        ICollectionDesk desk, IReciboReader reader, IAgencyClock clock, UserRole role, ReciboPrinting? printing = null)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(clock);

        _desk = desk;
        _reader = reader;
        _clock = clock;
        _role = role;
        _printing = printing;
        FindCommand = new AsyncRelayCommand(FindAsync, () => !Runner.IsBusy && !_voiding);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, CanConfirm);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public IAsyncRelayCommand FindCommand { get; }

    public IAsyncRelayCommand PreviewCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    [ObservableProperty]
    private string _numberText = string.Empty;

    [ObservableProperty]
    private string? _findMessage;

    [ObservableProperty]
    private ReciboViewModel? _recibo;

    [ObservableProperty]
    private VoidingGateResult? _gate;

    [ObservableProperty]
    private string _reasonText = string.Empty;

    [ObservableProperty]
    private VoidingPreview? _voidPreview;

    [ObservableProperty]
    private string? _refusalMessage;

    [ObservableProperty]
    private string? _staleMessage;

    [ObservableProperty]
    private string? _doneMessage;

    /// <summary>Each reversal the voiding writes, with its signed amount.</summary>
    public IReadOnlyList<string> Corrections =>
        VoidPreview?.Corrections.Select(c => "Corrección: " + Formats.Money(c.Amount)).ToList() ?? [];

    public string ResultingAmountOwedText => VoidPreview is { } p ? Formats.Money(p.ResultingAmountOwed) : string.Empty;

    public string AnnulmentsText => VoidPreview is { Annulments.Count: > 0 } p
        ? "Se anularán los recibos " + string.Join(" y ", p.Annulments.Select(a => $"N° {a.ReciboNumber}")) + "."
        : string.Empty;

    /// <summary>Opens the pair of a collection chosen in the recent list.</summary>
    public async Task OpenAsync(Guid collectionId)
    {
        ++_pairVersion;
        DoneMessage = null;
        FindMessage = null;
        var outcome = await Runner.RunAsync(() => _reader.GetByCollectionAsync(collectionId));
        if (outcome.Completed)
        {
            Show(outcome.Value);
        }
    }

    partial void OnReasonTextChanged(string value)
    {
        // The confirmation is for the reason shown; a new one needs a new preview.
        VoidPreview = null;
        StaleMessage = null;
        RefreshCommands();
    }

    partial void OnVoidPreviewChanged(VoidingPreview? value)
    {
        OnPropertyChanged(nameof(Corrections));
        OnPropertyChanged(nameof(ResultingAmountOwedText));
        OnPropertyChanged(nameof(AnnulmentsText));
        RefreshCommands();
    }

    private async Task FindAsync()
    {
        ++_pairVersion;
        FindMessage = null;
        DoneMessage = null;
        if (!ReciboSearch.TryParse(NumberText, out var number))
        {
            Show(null);
            FindMessage = ReciboSearch.TypeANumber;
            return;
        }

        var outcome = await Runner.RunAsync(() => _reader.FindByNumberAsync(number));
        if (!outcome.Completed)
        {
            return;
        }

        Show(outcome.Value);
        if (outcome.Value is null)
        {
            FindMessage = ReciboSearch.NotFound(number);
        }
    }

    private void Show(IssuedPayment? payment)
    {
        ++_pairVersion;
        Recibo = payment is null ? null : new ReciboViewModel(payment, _printing);
        Gate = payment is null ? null : VoidingGate.For(payment.RecordedOn, _clock.Today, _role, payment.IsAnnulled);
        VoidPreview = null;
        RefusalMessage = null;
        StaleMessage = null;
        RefreshCommands();
    }

    private bool CanPreview() => !Runner.IsBusy && !_voiding && Recibo is not null && Gate is { Offered: true };

    private async Task PreviewAsync()
    {
        if (!CanPreview())
        {
            return;
        }

        RefusalMessage = null;
        StaleMessage = null;

        // The reason first: a blank one never reaches the desk (spec Requirement 16).
        if (string.IsNullOrWhiteSpace(ReasonText))
        {
            RefusalMessage = VoidingRefusal.ReasonRequired.OperatorText();
            return;
        }

        await RequestPreviewAsync();
    }

    private async Task RequestPreviewAsync()
    {
        var reciboId = Recibo!.Payment.Locatario.ReciboId;
        var reason = ReasonText.Trim();
        var version = _pairVersion;
        var outcome = await Runner.RunAsync(() => _desk.PreviewVoidAsync(reciboId, reason, _role));
        if (!outcome.Completed || version != _pairVersion)
        {
            return;
        }

        var preview = outcome.Value!;
        if (preview.Refusal is { } refusal)
        {
            RefusalMessage = refusal.OperatorText();
            VoidPreview = null;
        }
        else
        {
            VoidPreview = preview;
        }
    }

    private bool CanConfirm() => !Runner.IsBusy && !_voiding && VoidPreview is { CanCommit: true };

    private async Task ConfirmAsync()
    {
        // The command refuses a second run; this keeps a direct second call from voiding twice.
        if (!CanConfirm())
        {
            return;
        }

        var preview = VoidPreview!;
        var collectionId = Recibo!.Payment.CollectionId;
        _voiding = true;
        RefusalMessage = null;
        StaleMessage = null;
        RefreshCommands();

        try
        {
            var outcome = await Runner.RunAsync(() => _desk.VoidAsync(preview), OperationKind.Void);
            VoidPreview = null;
            if (!outcome.Completed)
            {
                // It may or may not have been applied; the runner says so, and the pair is withdrawn so that
                // nothing is offered until it is looked up again (S4a review).
                Show(null);
                return;
            }

            switch (outcome.Value!.Refusal)
            {
                case null:
                    // Read back outside the runner, as the desk does: its generic "try again" would invite a
                    // second voiding of a pair already annulled (S4a review).
                    IssuedPayment? reread = null;
                    try
                    {
                        reread = await _reader.GetByCollectionAsync(collectionId);
                    }
                    catch (Exception)
                    {
                        // Deliberately discarded, as in OperationRunner: the text never reaches the screen.
                    }

                    Show(reread);
                    DoneMessage = reread is null ? VoidingTexts.DoneButNotShown : VoidingTexts.Done;
                    break;

                case VoidingRefusal.PreviewStale:
                    // One fresh preview, and a new confirmation; never a second voiding on its own.
                    StaleMessage = VoidingRefusal.PreviewStale.OperatorText();
                    _voiding = false;
                    await RequestPreviewAsync();
                    break;

                case var refusal:
                    RefusalMessage = refusal.Value.OperatorText();
                    break;
            }
        }
        finally
        {
            _voiding = false;
            RefreshCommands();
        }
    }

    private void RefreshCommands()
    {
        FindCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            RefreshCommands();
        }
    }
}
