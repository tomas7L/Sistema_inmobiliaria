using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>
/// The cash desk's account search (spec Requirement 6): tenant, units, contract status, short id and
/// readiness for every result. An account that is not ready stays selectable, because the commit's own
/// refusal is what decides; readiness is shown so the operator is not surprised.
/// </summary>
public sealed partial class AccountPickerViewModel : ObservableObject
{
    /// <summary>Fewer characters than this are not searched (design D17).</summary>
    public const int MinimumSearchLength = 2;

    private readonly IAccountDirectory _directory;
    private readonly Func<AccountListing, Task> _onChosen;
    private readonly Func<bool> _canChoose;

    /// <param name="directory">The search.</param>
    /// <param name="onChosen">Receives the chosen account.</param>
    /// <param name="canChoose">False while the owner cannot take another account (a commit running).</param>
    public AccountPickerViewModel(
        IAccountDirectory directory, Func<AccountListing, Task> onChosen, Func<bool>? canChoose = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(onChosen);

        _directory = directory;
        _onChosen = onChosen;
        _canChoose = canChoose ?? (() => true);
        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        // Concurrent on purpose: a choice still waiting on a slow database must not hold the next one; the
        // owner cancels the earlier choice (S3b review).
        ChooseCommand = new AsyncRelayCommand<AccountRow>(
            ChooseAsync, row => row is not null && _canChoose(), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<AccountRow> Results { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand<AccountRow> ChooseCommand { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = string.Empty;

    /// <summary>Set when a search found nothing; null otherwise.</summary>
    [ObservableProperty]
    private string? _noResultsMessage;

    private bool CanSearch() => !Runner.IsBusy && SearchText.Trim().Length >= MinimumSearchLength;

    private async Task SearchAsync()
    {
        if (!CanSearch())
        {
            return;
        }

        NoResultsMessage = null;
        var outcome = await Runner.RunAsync(() => _directory.SearchAsync(SearchText));
        if (!outcome.Completed)
        {
            return;
        }

        Results.Clear();
        foreach (var listing in outcome.Value!)
        {
            Results.Add(new AccountRow(listing));
        }

        NoResultsMessage = Results.Count == 0 ? "No se encontró ninguna cuenta." : null;
    }

    private Task ChooseAsync(AccountRow? row) =>
        row is null || !_canChoose() ? Task.CompletedTask : _onChosen(row.Listing);

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            SearchCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One search result as the operator reads it.</summary>
public sealed class AccountRow
{
    public AccountRow(AccountListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Listing = listing;
        Problems = CashDesk.ReadinessText.Problems(listing.Readiness);
    }

    public AccountListing Listing { get; }

    public string Tenants => Listing.Tenants;

    public string Lessors => Listing.Lessors;

    public string Units => Listing.Units;

    public string ShortId => Listing.ContractShortId;

    public string StatusText => Formats.Status(Listing.Status);

    public bool IsReady => Listing.Readiness.IsReady;

    public string ReadinessText => IsReady ? CashDesk.ReadinessText.Ready : CashDesk.ReadinessText.NotReady;

    /// <summary>Each missing gate with what to do; empty when ready.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Information only: a missing cutover date never blocks (spec Decision 21).</summary>
    public string CutoverText => Listing.CutoverDate is { } date
        ? "Fecha de corte: " + Formats.Date(date)
        : "Sin fecha de corte (informativo)";
}
