using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// The new-contract form (gestion-de-alquileres spec Requirements 7 and 8, design §6). The port applies every rule again, and
/// the database decides who may set a cutover date: offering it only to an Admin is a courtesy, not the guard. Editing
/// anything withdraws warnings waiting to be accepted, so what is accepted is what is written. The view arrives in S12.
/// </summary>
public sealed partial class ContractFormViewModel : ObservableObject
{
    /// <summary>What the form says when warnings came back for values it no longer holds.</summary>
    public const string FormChangedWhileSaving = "El formulario cambió mientras se guardaba. Revíselo y guarde de nuevo.";

    private readonly IContractRegistry _contracts;

    /// <summary>Counts edits, so a reply can tell whether it still describes what the form holds.</summary>
    private int _edits;

    public ContractFormViewModel(IContractRegistry contracts, bool canSetCutover)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = contracts;
        IsCutoverOffered = canSetCutover;
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        AcceptWarningsCommand = new AsyncRelayCommand(AcceptWarningsAsync, () => CanAct() && HasWarnings);
        CancelCommand = new RelayCommand(WithdrawWarnings);
        AddLessorCommand = new AsyncRelayCommand<PartyListing>(AddLessorAsync);
        Lessors.CollectionChanged += OnFormCollectionChanged;
        Codebtors.CollectionChanged += OnFormCollectionChanged;
        Units.CollectionChanged += OnUnitsChanged;
        ClauseIndexIds.CollectionChanged += OnFormCollectionChanged;
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>Only an Admin sees the cutover section; anyone else's date is never sent.</summary>
    public bool IsCutoverOffered { get; }

    public ObservableCollection<PartyListing> Lessors { get; } = [];

    public ObservableCollection<PartyListing> Codebtors { get; } = [];

    public ObservableCollection<ContractFormUnit> Units { get; } = [];

    public ObservableCollection<Guid> ClauseIndexIds { get; } = [];

    /// <summary>What the operator has to accept before the contract is written.</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand AcceptWarningsCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>Adds a lessor and, while the rate is empty, suggests the one of that person's most recent contract carrying one.</summary>
    public IAsyncRelayCommand<PartyListing> AddLessorCommand { get; }

    [ObservableProperty]
    private PartyListing? _tenant;

    [ObservableProperty]
    private DateOnly? _startDate;

    [ObservableProperty]
    private DateOnly? _nominalEndDate;

    [ObservableProperty]
    private DateOnly? _cutoverDate;

    [ObservableProperty]
    private string _monthlyRentText = string.Empty;

    [ObservableProperty]
    private string _dueDayText = Contract.DefaultDueDay.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _honorariosText = string.Empty;

    [ObservableProperty]
    private string _feePlanAmountText = string.Empty;

    [ObservableProperty]
    private string _feePlanInstalmentsText = string.Empty;

    [ObservableProperty]
    private string _clauseIntervalText = string.Empty;

    /// <summary>Whether each unit's share is typed as an amount, converted to a percentage, rather than as a percentage.</summary>
    [ObservableProperty]
    private bool _sharesAsAmounts;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>The contract just written, or null.</summary>
    [ObservableProperty]
    private ContractListing? _created;

    partial void OnTenantChanged(PartyListing? value) => WithdrawWarnings();

    partial void OnStartDateChanged(DateOnly? value) => WithdrawWarnings();

    partial void OnNominalEndDateChanged(DateOnly? value) => WithdrawWarnings();

    partial void OnCutoverDateChanged(DateOnly? value) => WithdrawWarnings();

    partial void OnMonthlyRentTextChanged(string value) => WithdrawWarnings();

    partial void OnDueDayTextChanged(string value) => WithdrawWarnings();

    partial void OnHonorariosTextChanged(string value) => WithdrawWarnings();

    partial void OnFeePlanAmountTextChanged(string value) => WithdrawWarnings();

    partial void OnFeePlanInstalmentsTextChanged(string value) => WithdrawWarnings();

    partial void OnClauseIntervalTextChanged(string value) => WithdrawWarnings();

    partial void OnSharesAsAmountsChanged(bool value) => WithdrawWarnings();

    private bool CanAct() => !Runner.IsBusy;

    private void WithdrawWarnings()
    {
        _edits++;
        SetWarnings([]);
    }

    private void SetWarnings(IEnumerable<string> warnings)
    {
        Warnings.Clear();
        foreach (var warning in warnings)
        {
            Warnings.Add(warning);
        }

        OnPropertyChanged(nameof(HasWarnings));
        AcceptWarningsCommand.NotifyCanExecuteChanged();
    }

    private async Task AddLessorAsync(PartyListing? lessor)
    {
        if (lessor is null || Lessors.Any(l => l.Id == lessor.Id))
        {
            return;
        }

        Lessors.Add(lessor);
        if (!string.IsNullOrWhiteSpace(HonorariosText))
        {
            return;
        }

        var outcome = await Runner.RunAsync(() => _contracts.SuggestRateAsync(lessor.Id));

        // Whatever the operator typed while this was out stays theirs.
        if (outcome is { Completed: true, Value: { } rate } && string.IsNullOrWhiteSpace(HonorariosText))
        {
            HonorariosText = Formats.Money(rate);
        }
    }

    private async Task SaveAsync()
    {
        WithdrawWarnings();
        await SubmitAsync(acceptWarnings: false);
    }

    private async Task AcceptWarningsAsync()
    {
        WithdrawWarnings();
        await SubmitAsync(acceptWarnings: true);
    }

    private async Task SubmitAsync(bool acceptWarnings)
    {
        RefusalMessage = null;
        Created = null;

        if (TryBuildDraft(out var refused) is not { } draft)
        {
            RefusalMessage = refused!.Value.OperatorText();
            return;
        }

        var editsWhenSent = _edits;
        var outcome = await Runner.RunAsync(() => _contracts.CreateAsync(draft, acceptWarnings));
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        var editedSince = _edits != editsWhenSent;
        if (result.Succeeded)
        {
            // What was typed while this was out is not the contract just written, so it is not thrown away with it.
            if (!editedSince)
            {
                ClearForm();
            }

            Created = result.Contract;
        }
        else if (result.Refusal is { } refusal)
        {
            RefusalMessage = refusal.OperatorText(result.CurrentTenant);
        }
        else if (editedSince)
        {
            RefusalMessage = FormChangedWhileSaving;
        }
        else
        {
            SetWarnings(result.Warnings.Select(w => w.OperatorText()));
        }
    }

    /// <summary>What was typed, or null with the refusal for text that is not even a number. Everything else is the rules' to say.</summary>
    private ContractDraft? TryBuildDraft(out ContractRefusal? refused)
    {
        refused = null;

        if (!Formats.TryParseMoney(MonthlyRentText, out var rent) || (SharesAsAmounts && rent is not > 0m))
        {
            refused = ContractRefusal.RentNotWholePositive;
        }
        else if (!int.TryParse(DueDayText, NumberStyles.None, CultureInfo.InvariantCulture, out var dueDay))
        {
            refused = ContractRefusal.DueDayOutOfRange;
        }
        else if (!Formats.TryParseMoney(HonorariosText, out var rate))
        {
            refused = ContractRefusal.HonorariosOutOfRange;
        }
        else if (!TryParseFeePlan(out var feeAmount, out var feeInstalments))
        {
            refused = ContractRefusal.FeePlanInvalid;
        }
        else if (!TryParseClause(out var clause))
        {
            refused = ContractRefusal.ClauseInvalid;
        }
        else if (!TryBuildShares(rent, out var shares))
        {
            refused = ContractRefusal.SharesNotHundred;
        }
        else
        {
            return new ContractDraft
            {
                TenantId = Tenant?.Id,
                LessorIds = Lessors.Select(l => l.Id).ToList(),
                CodebtorIds = Codebtors.Select(c => c.Id).ToList(),
                Shares = shares,
                StartDate = StartDate,
                NominalEndDate = NominalEndDate,
                MonthlyRent = rent,
                DueDay = dueDay,
                HonorariosPercentage = rate,
                CutoverDate = IsCutoverOffered ? CutoverDate : null,
                FeePlanAmount = feeAmount,
                FeePlanInstalments = feeInstalments,
                Clause = clause,
            };
        }

        return null;
    }

    private bool TryParseFeePlan(out decimal? amount, out int? instalments)
    {
        instalments = null;
        if (!Formats.TryParseMoney(FeePlanAmountText, out amount))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FeePlanInstalmentsText))
        {
            return true;
        }

        if (!int.TryParse(FeePlanInstalmentsText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        instalments = parsed;
        return true;
    }

    private bool TryParseClause(out ClauseDraft? clause)
    {
        clause = null;
        if (ClauseIndexIds.Count == 0 && string.IsNullOrWhiteSpace(ClauseIntervalText))
        {
            return true;
        }

        if (!int.TryParse(ClauseIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var interval))
        {
            return false;
        }

        clause = new ClauseDraft(ClauseIndexIds.ToList(), interval);
        return true;
    }

    /// <summary>No share typed splits equally; a share left blank beside typed ones does not total anything.</summary>
    private bool TryBuildShares(decimal? rent, out IReadOnlyList<UnitShare> shares)
    {
        shares = [];
        if (Units.Count == 0)
        {
            return true;
        }

        if (Units.All(u => string.IsNullOrWhiteSpace(u.ShareText)))
        {
            shares = RentSplit.Equal(Units.Select(u => u.Unit.Id).ToList());
            return true;
        }

        var typed = new List<(Guid UnitId, decimal Value)>();
        foreach (var line in Units)
        {
            if (!Formats.TryParseMoney(line.ShareText, out var value) || value is null)
            {
                return false;
            }

            typed.Add((line.Unit.Id, value.Value));
        }

        if (!SharesAsAmounts)
        {
            shares = typed.Select(t => new UnitShare(t.UnitId, t.Value)).ToList();
            return true;
        }

        if (RentSplit.FromAmounts(rent!.Value, typed) is not { } converted)
        {
            return false;
        }

        shares = converted;
        return true;
    }

    private void ClearForm()
    {
        Tenant = null;
        Lessors.Clear();
        Codebtors.Clear();
        Units.Clear();
        ClauseIndexIds.Clear();
        StartDate = null;
        NominalEndDate = null;
        CutoverDate = null;
        MonthlyRentText = string.Empty;
        DueDayText = Contract.DefaultDueDay.ToString(CultureInfo.InvariantCulture);
        HonorariosText = string.Empty;
        FeePlanAmountText = string.Empty;
        FeePlanInstalmentsText = string.Empty;
        ClauseIntervalText = string.Empty;
        SharesAsAmounts = false;
    }

    private void OnFormCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => WithdrawWarnings();

    private void OnUnitsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var added in e.NewItems?.OfType<ContractFormUnit>() ?? [])
        {
            added.PropertyChanged += OnUnitLineChanged;
        }

        foreach (var removed in e.OldItems?.OfType<ContractFormUnit>() ?? [])
        {
            removed.PropertyChanged -= OnUnitLineChanged;
        }

        WithdrawWarnings();
    }

    private void OnUnitLineChanged(object? sender, PropertyChangedEventArgs e) => WithdrawWarnings();

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            SaveCommand.NotifyCanExecuteChanged();
            AcceptWarningsCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>A unit on the form, with what was typed for its share. Everything blank splits equally.</summary>
public sealed partial class ContractFormUnit : ObservableObject
{
    public ContractFormUnit(UnitListing unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        Unit = unit;
    }

    public UnitListing Unit { get; }

    [ObservableProperty]
    private string _shareText = string.Empty;
}
