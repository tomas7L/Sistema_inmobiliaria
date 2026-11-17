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
    private readonly Func<Guid, ContractDetailViewModel>? _buildDetail;
    private IReadOnlyList<ContractListing> _loaded = [];

    /// <param name="contracts">The port.</param>
    /// <param name="openDetail">Builds one contract's detail; without it, no contract is offered to open.</param>
    public ContractsViewModel(IContractRegistry contracts, Func<Guid, ContractDetailViewModel>? openDetail = null)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        _buildDetail = openDetail;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        OpenCommand = new AsyncRelayCommand<ContractRow>(OpenAsync, row => _buildDetail is not null && row is not null && !Runner.IsBusy);
        BackCommand = new AsyncRelayCommand(BackAsync, () => !Runner.IsBusy);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>The rows the filter lets through.</summary>
    public ObservableCollection<ContractRow> Contracts { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>"Abrir": the row's detail in place of the list.</summary>
    public IAsyncRelayCommand<ContractRow> OpenCommand { get; }

    /// <summary>"Volver a contratos": the list again, read anew so a change made in the detail shows.</summary>
    public IAsyncRelayCommand BackCommand { get; }

    /// <summary>The contract open in place of the list, or null while the list shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen))]
    private ContractDetailViewModel? _openDetail;

    public bool IsDetailOpen => OpenDetail is not null;

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

    private async Task OpenAsync(ContractRow? row)
    {
        if (_buildDetail is null || row is null)
        {
            return;
        }

        var detail = _buildDetail(row.Id);
        OpenDetail = detail;
        await detail.LoadCommand.ExecuteAsync(null);
    }

    private async Task BackAsync()
    {
        OpenDetail = null;
        await LoadAsync();
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
            OpenCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
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
