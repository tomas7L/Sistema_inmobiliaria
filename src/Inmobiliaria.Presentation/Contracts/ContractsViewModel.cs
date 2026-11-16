using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>The list's state filters (task 12.3d). Cancelled ones are apart from the ended ones, though both read Ended.</summary>
public enum ContractListFilter
{
    All,
    Active,
    PendingTermination,
    Ended,
    Cancelled,
}

/// <summary>
/// The contract list (gestion-de-alquileres spec Requirement 6, task 12.3d): by tenant, lessor and unit through the port, by
/// state on the screen, flagging a contract past its end without ending it. Both roles.
/// </summary>
public sealed partial class ContractsViewModel : ObservableObject
{
    private readonly IContractRegistry _contracts;
    private IReadOnlyList<ContractListing> _loaded = [];

    public ContractsViewModel(IContractRegistry contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>The rows the filter lets through.</summary>
    public ObservableCollection<ContractRow> Contracts { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Which states to show. Changing it filters what was loaded and asks the database nothing.</summary>
    [ObservableProperty]
    private ContractListFilter _filter = ContractListFilter.All;

    public string CountText => Contracts.Count == 1 ? "1 contrato" : $"{Contracts.Count} contratos";

    public bool IsEmpty => Contracts.Count == 0;

    partial void OnFilterChanged(ContractListFilter value) => Show();

    /// <summary>Every state is asked for, so the filter can then change on the screen alone.</summary>
    private async Task LoadAsync()
    {
        var text = SearchText;
        var outcome = await Runner.RunAsync(() => _contracts.ListAsync(text, null));
        if (!outcome.Completed)
        {
            return;
        }

        _loaded = outcome.Value!;
        Show();
    }

    private void Show()
    {
        Contracts.Clear();
        foreach (var listing in _loaded.Where(Passes))
        {
            Contracts.Add(new ContractRow(listing));
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Passes(ContractListing listing) => Filter switch
    {
        ContractListFilter.Active => listing.Status == ContractStatus.Active,
        ContractListFilter.PendingTermination => listing.Status == ContractStatus.PendingTermination,
        ContractListFilter.Ended => listing.Status == ContractStatus.Ended && !listing.IsCancelled,
        ContractListFilter.Cancelled => listing.IsCancelled,
        _ => true,
    };

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

    public string StatusText => Formats.Status(Listing.Status, Listing.IsCancelled);

    /// <summary>Beside the state, which stays as it is: passing the end date does not end a contract.</summary>
    public string? PastEndFlag => Listing.IsPastEnd ? "Fecha de fin vencida" : null;

    /// <summary>The day it really ended, once it has; until then the day it says it ends.</summary>
    public string EndText => Formats.Date(Listing.ActualEndDate ?? Listing.NominalEndDate);

    /// <summary>The rent today, adjustments included.</summary>
    public string RentText => Formats.Money(Listing.MonthlyRent);
}
