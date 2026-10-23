using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.GoLive;

/// <summary>
/// The go-live screen (spec Requirements 17 to 19, design sections 5.3 and 6.3): each contract's cutover
/// date and whether it can still change, and the recibo starting number. Admin only, by the shell; the
/// database refuses an Empleado anyway. A refusal is shown as its operator text and the screen re-reads
/// what it shows; an exception goes through <see cref="OperationRunner"/> and leaves the typed values.
/// </summary>
public sealed partial class GoLiveViewModel : ObservableObject
{
    private readonly IGoLiveSetup _setup;

    public GoLiveViewModel(IGoLiveSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        _setup = setup;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        SetCutoverDateCommand = new AsyncRelayCommand<GoLiveContractRow>(SetCutoverDateAsync, CanSetCutoverDate);
        SetStartingNumberCommand = new AsyncRelayCommand(SetStartingNumberAsync, () => !Runner.IsBusy && CanChangeStartingNumber);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<GoLiveContractRow> Contracts { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand<GoLiveContractRow> SetCutoverDateCommand { get; }

    public IAsyncRelayCommand SetStartingNumberCommand { get; }

    /// <summary>The last refusal's operator text, or null. Cleared when the next operation starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>"Sin cargar" or the number.</summary>
    [ObservableProperty]
    private string _startingNumberText = string.Empty;

    [ObservableProperty]
    private string _startingNumberStateText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetStartingNumberCommand))]
    private bool _canChangeStartingNumber;

    /// <summary>What the operator typed; parsed only when submitted (spec Decision 32).</summary>
    [ObservableProperty]
    private string _newStartingNumber = string.Empty;

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(async () =>
            (Contracts: await _setup.ListContractsAsync(), State: await _setup.GetStartingNumberStateAsync()));

        if (!outcome.Completed)
        {
            return;
        }

        var (contracts, state) = outcome.Value;

        Contracts.Clear();
        foreach (var listing in contracts)
        {
            Contracts.Add(new GoLiveContractRow(listing, SetCutoverDateCommand.NotifyCanExecuteChanged));
        }

        StartingNumberText = state.StartingNumber?.ToString(CultureInfo.InvariantCulture) ?? "Sin cargar";
        StartingNumberStateText = state.IsLocked ? "Bloqueado: ya se emitió un recibo" : "Modificable";
        CanChangeStartingNumber = !state.IsLocked;
    }

    private bool CanSetCutoverDate(GoLiveContractRow? row) =>
        !Runner.IsBusy && row is { CanChange: true, NewCutoverDate: not null };

    private async Task SetCutoverDateAsync(GoLiveContractRow? row)
    {
        if (row?.NewCutoverDate is not { } typed)
        {
            return;
        }

        RefusalMessage = null;
        var date = DateOnly.FromDateTime(typed);
        var outcome = await Runner.RunAsync(() => _setup.SetCutoverDateAsync(row.ContractId, date));
        await AfterSetAsync(outcome);
    }

    private async Task SetStartingNumberAsync()
    {
        RefusalMessage = null;

        // Integer style only, invariant: "12.5", "12,5", text and anything beyond int never reach the port.
        if (!int.TryParse(NewStartingNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || number < 1)
        {
            RefusalMessage = SetupRefusal.StartingNumberOutOfRange.OperatorText();
            return;
        }

        var outcome = await Runner.RunAsync(() => _setup.SetStartingNumberAsync(number));
        await AfterSetAsync(outcome);
    }

    private async Task AfterSetAsync(OperationOutcome<SetupResult> outcome)
    {
        if (!outcome.Completed)
        {
            return;
        }

        var refusal = outcome.Value!.Refusal?.OperatorText();

        // A refusal can mean the screen was out of date (a lock that arrived after it loaded), so the
        // state is re-read either way, and the refusal stays on screen beside the refreshed state.
        await ReloadAsync();
        RefusalMessage = refusal;
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            SetCutoverDateCommand.NotifyCanExecuteChanged();
            SetStartingNumberCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One contract on the go-live screen, with the date the operator is typing for it.</summary>
public sealed partial class GoLiveContractRow : ObservableObject
{
    private readonly Action _dateChanged;

    public GoLiveContractRow(CutoverListing listing, Action dateChanged)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(dateChanged);

        _dateChanged = dateChanged;
        ContractId = listing.ContractId;
        Tenants = listing.Tenants;
        Units = listing.Units;
        StartDateText = Format(listing.StartDate);
        CutoverText = listing.CutoverDate is { } date ? Format(date) : "Sin fecha de corte";
        StateText = listing.State switch
        {
            CutoverState.Changeable => "Modificable",
            CutoverState.Locked => "Bloqueada: la cuenta ya tiene movimientos",
            CutoverState.NoAccount => "No se puede cargar: el contrato no tiene cuenta",
            _ => throw new ArgumentOutOfRangeException(nameof(listing), listing.State, "An unmapped cutover state."),
        };
        CanChange = listing.State == CutoverState.Changeable;
    }

    public Guid ContractId { get; }

    public string Tenants { get; }

    public string Units { get; }

    public string StartDateText { get; }

    public string CutoverText { get; }

    public string StateText { get; }

    /// <summary>False when the date is locked or the contract has no account: no change is offered.</summary>
    public bool CanChange { get; }

    /// <summary>The date being typed. A <see cref="DateTime"/> because that is what a date picker binds.</summary>
    [ObservableProperty]
    private DateTime? _newCutoverDate;

    partial void OnNewCutoverDateChanged(DateTime? value) => _dateChanged();

    private static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
