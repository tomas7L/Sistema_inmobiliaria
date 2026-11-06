using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// The contract list (gestion-de-alquileres spec Requirement 6): by tenant, lessor, unit and state, flagging a contract past
/// its end without ending it. Both roles; the view arrives in S12.
/// </summary>
public sealed partial class ContractsViewModel : ObservableObject
{
    private readonly IContractRegistry _contracts;

    public ContractsViewModel(IContractRegistry contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<ContractRow> Contracts { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The state to list, or null for all of them.</summary>
    [ObservableProperty]
    private ContractStatus? _statusFilter;

    private async Task LoadAsync()
    {
        var text = SearchText;
        var status = StatusFilter;
        var outcome = await Runner.RunAsync(() => _contracts.ListAsync(text, status));
        if (!outcome.Completed)
        {
            return;
        }

        Contracts.Clear();
        foreach (var listing in outcome.Value!)
        {
            Contracts.Add(new ContractRow(listing));
        }
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One contract on the list.</summary>
public sealed class ContractRow
{
    public ContractRow(ContractListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Listing = listing;
    }

    public ContractListing Listing { get; }

    public Guid Id => Listing.Id;

    public string Tenants => Listing.Tenants;

    public string Lessors => Listing.Lessors;

    public string Units => Listing.Units;

    public string StatusText => Listing.IsCancelled ? "Cancelado por error de carga" : Formats.Status(Listing.Status);

    /// <summary>Beside the state, which stays as it is: passing the end date does not end a contract.</summary>
    public string? PastEndFlag => Listing.IsPastEnd ? "Fecha de fin vencida" : null;
}
