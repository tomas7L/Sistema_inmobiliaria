using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// Attaches or corrects a contract's clause (gestion-de-alquileres spec Requirement 13, Decision 32), showing the months the next
/// due date will read. Both roles; the view arrives in slice S12.
/// </summary>
public sealed partial class ContractClauseViewModel : ObservableObject
{
    private readonly IContractRegistry _contracts;
    private readonly Guid _contractId;

    public ContractClauseViewModel(IContractRegistry contracts, Guid contractId)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        _contractId = contractId;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !Runner.IsBusy && CanChange);
        IndexIds.CollectionChanged += OnIndexIdsChanged;
        Runner.PropertyChanged += (_, _) =>
        {
            LoadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        };
    }

    public OperationRunner Runner { get; } = new();

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public ObservableCollection<Guid> IndexIds { get; } = [];

    /// <summary>The catalog's names, for the suggestion. Filled by whoever shows the list of indices.</summary>
    public IReadOnlyDictionary<Guid, string> IndexNames { get; set; } = new Dictionary<Guid, string>();

    /// <summary>The offset the indices chosen suggest, or null. Decision 15 only suggests it.</summary>
    public int? SuggestedOffset => ClauseOffsetSuggestion.For(IndexIds.Where(IndexNames.ContainsKey).Select(id => IndexNames[id]));

    public string? WindowText => ClauseFormRules.WindowText(_startDate, _cutoverDate, IntervalText, OffsetText, SuggestedOffset, _lastEffectiveDate);

    private DateOnly? _startDate;
    private DateOnly? _cutoverDate;
    private DateOnly? _lastEffectiveDate;

    /// <summary>False once the contract has a confirmed adjustment or has ended: the form only says why.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowText))]
    private string _intervalText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowText))]
    private string _offsetText = string.Empty;

    [ObservableProperty]
    private string? _refusalMessage;

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        var outcome = await Runner.RunAsync(() => _contracts.DetailAsync(_contractId));
        if (!outcome.Completed)
        {
            return;
        }

        if (outcome.Value is not { } detail)
        {
            CanChange = false;
            RefusalMessage = ContractRefusal.ContractNotFound.OperatorText();
            return;
        }

        Fill(detail);
    }

    private void Fill(ContractDetail detail)
    {
        _startDate = detail.StartDate;
        _cutoverDate = detail.CutoverDate;
        _lastEffectiveDate = detail.Adjustments.Count > 0 ? detail.Adjustments.Max(a => a.EffectiveDate) : null;

        IndexIds.Clear();
        foreach (var id in detail.Clause?.IndexIds ?? [])
        {
            IndexIds.Add(id);
        }

        IntervalText = detail.Clause?.IntervalMonths.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        OffsetText = detail.Clause?.WindowOffsetMonths.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        OnPropertyChanged(nameof(WindowText));

        var stopped = detail.IsCancelled
            ? ContractRefusal.ContractCancelled
            : detail.Status == ContractStatus.Ended
                ? ContractRefusal.ContractEnded
                : detail.Adjustments.Count > 0 ? ContractRefusal.AdjustmentConfirmed : (ContractRefusal?)null;
        CanChange = stopped is null;
        RefusalMessage = stopped?.OperatorText();
    }

    private async Task SaveAsync()
    {
        RefusalMessage = null;
        if (!ClauseFormRules.TryBuild(IndexIds, IntervalText, OffsetText, SuggestedOffset, out var clause) || clause is null)
        {
            RefusalMessage = ContractRefusal.ClauseInvalid.OperatorText();
            return;
        }

        var outcome = await Runner.RunAsync(() => _contracts.SetClauseAsync(_contractId, clause), OperationKind.Commit);
        if (!outcome.Completed)
        {
            return;
        }

        RefusalMessage = outcome.Value!.Refusal?.OperatorText();
        await ReloadAfterSaveAsync();
    }

    /// <summary>Read again, refused or not, but keeping the refusal: a contract that changed meanwhile says so on the form.</summary>
    private async Task ReloadAfterSaveAsync()
    {
        var refusal = RefusalMessage;
        await LoadAsync();
        RefusalMessage ??= refusal;
    }

    private void OnIndexIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SuggestedOffset));
        OnPropertyChanged(nameof(WindowText));
    }
}
