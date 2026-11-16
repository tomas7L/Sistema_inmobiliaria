using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// The new-contract form (gestion-de-alquileres spec Requirements 7, 8, 27 and 29, design §6). The form shows every error it
/// can find on its own at once, each beside its field (Decision 44), and sends nothing while any remains; the port applies
/// every rule again, and the database decides who may set a cutover date: offering it only to an Admin is a courtesy, not
/// the guard. Editing anything withdraws warnings waiting to be accepted, so what is accepted is what is written.
/// </summary>
public sealed partial class ContractFormViewModel : ObservableObject
{
    /// <summary>What the form says when warnings came back for values it no longer holds.</summary>
    public const string FormChangedWhileSaving = "El formulario cambió mientras se guardaba. Revíselo y guarde de nuevo.";

    private readonly IContractRegistry _contracts;
    private readonly IAgencyClock _clock;
    private readonly IIndexRegistry _indices;

    /// <summary>Counts edits, so a reply can tell whether it still describes what the form holds.</summary>
    private int _edits;

    public ContractFormViewModel(IContractRegistry contracts, bool canSetCutover, IAgencyClock clock, IIndexRegistry indices)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(indices);

        _contracts = contracts;
        _clock = clock;
        _indices = indices;
        IsCutoverOffered = canSetCutover;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        AcceptWarningsCommand = new AsyncRelayCommand(AcceptWarningsAsync, () => CanAct() && HasWarnings);
        CancelCommand = new RelayCommand(WithdrawWarnings);
        AddLessorCommand = new AsyncRelayCommand<PartyListing>(AddLessorAsync);
        Lessors.CollectionChanged += OnFormCollectionChanged;
        Codebtors.CollectionChanged += OnFormCollectionChanged;
        Units.CollectionChanged += OnUnitsChanged;
        ClauseIndexIds.CollectionChanged += OnFormCollectionChanged;
        ClauseIndexIds.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SuggestedClauseOffset));
            OnPropertyChanged(nameof(ClauseWindowText));
        };
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>Only an Admin sees the cutover section; anyone else's date is never sent.</summary>
    public bool IsCutoverOffered { get; }

    public ObservableCollection<PartyListing> Lessors { get; } = [];

    public ObservableCollection<PartyListing> Codebtors { get; } = [];

    public ObservableCollection<ContractFormUnit> Units { get; } = [];

    public ObservableCollection<Guid> ClauseIndexIds { get; } = [];

    /// <summary>The indices a new clause can choose: the catalog's, less the discontinued ones (task 12.3c).</summary>
    public ObservableCollection<IndexOption> ClauseIndexOptions { get; } = [];

    /// <summary>The catalog's names, for the suggestion. Read by <see cref="LoadCommand"/>.</summary>
    public IReadOnlyDictionary<Guid, string> ClauseIndexNames { get; private set; } = new Dictionary<Guid, string>();

    /// <summary>Reads the index catalog, so the indices chosen are named and the offset suggestion follows them.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>The offset the indices chosen suggest, or null. Spec Decision 15 only suggests it.</summary>
    public int? SuggestedClauseOffset =>
        ClauseOffsetSuggestion.For(ClauseIndexIds.Where(ClauseIndexNames.ContainsKey).Select(id => ClauseIndexNames[id]));

    /// <summary>The due date and the months it will read, so the operator can check them against the paper (spec Requirement 13).</summary>
    public string? ClauseWindowText => ClauseFormRules.WindowText(StartDate, CutoverDate, ClauseIntervalText, ClauseOffsetText, SuggestedClauseOffset);

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
    [NotifyPropertyChangedFor(nameof(ClauseWindowText))]
    private DateOnly? _startDate;

    [ObservableProperty]
    private DateOnly? _nominalEndDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClauseWindowText))]
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
    [NotifyPropertyChangedFor(nameof(ClauseWindowText))]
    private string _clauseIntervalText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClauseWindowText))]
    private string _clauseOffsetText = string.Empty;

    /// <summary>Whether each unit's share is typed as an amount, converted to a percentage, rather than as a percentage.</summary>
    [ObservableProperty]
    private bool _sharesAsAmounts;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>Each field's error from the last save, all at once (spec Decision 44). Cleared when the next starts.</summary>
    [ObservableProperty]
    private ContractFieldErrors _errors = ContractFieldErrors.None;

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

    partial void OnClauseOffsetTextChanged(string value) => WithdrawWarnings();

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
            HonorariosText = Formats.Number(rate);
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
        Errors = ContractFieldErrors.None;

        var draft = BuildDraft(out var problems);
        if (problems.Count > 0)
        {
            Errors = new ContractFieldErrors(problems);
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

    /// <summary>
    /// What was typed, with every problem the form can find on its own (spec Decision 44): text that is not even a number,
    /// beside its field, and the rules' own (<see cref="ContractDraftRules.Problems"/>) for the rest, in the form's order.
    /// A field whose text could not be read gets only that problem, not the rules' about the blank it leaves.
    /// </summary>
    private ContractDraft BuildDraft(out IReadOnlyList<ContractProblem> problems)
    {
        var unread = new List<ContractProblem>();

        var rentRead = Formats.TryParseMoney(MonthlyRentText, out var rent);
        if (!rentRead || (SharesAsAmounts && rent is not > 0m))
        {
            unread.Add(new ContractProblem(ContractField.Rent, ContractRefusal.RentNotWholePositive));
        }

        if (!int.TryParse(DueDayText, NumberStyles.None, CultureInfo.InvariantCulture, out var dueDay))
        {
            unread.Add(new ContractProblem(ContractField.DueDay, ContractRefusal.DueDayOutOfRange));
            dueDay = Contract.DefaultDueDay;
        }

        if (!Formats.TryParseMoney(HonorariosText, out var rate))
        {
            unread.Add(new ContractProblem(ContractField.Honorarios, ContractRefusal.HonorariosOutOfRange));
        }

        if (!TryParseFeePlan(out var feeAmount, out var feeInstalments))
        {
            unread.Add(new ContractProblem(ContractField.FeePlan, ContractRefusal.FeePlanInvalid));
            (feeAmount, feeInstalments) = (null, null);
        }

        if (!TryParseClause(out var clause))
        {
            unread.Add(new ContractProblem(ContractField.Clause, ContractRefusal.ClauseInvalid));
        }

        // Amounts convert against the canon, so with no canon the problem is the canon's alone, not the shares'.
        var canonMissing = SharesAsAmounts && rent is not > 0m;
        if (!TryBuildShares(rent, out var shares))
        {
            if (!canonMissing)
            {
                unread.Add(new ContractProblem(ContractField.Shares, ContractRefusal.SharesNotHundred));
            }

            shares = RentSplit.Equal(Units.Select(u => u.Unit.Id).ToList());
        }

        var draft = new ContractDraft
        {
            TenantId = Tenant?.Id,
            LessorIds = Lessors.Select(l => l.Id).ToList(),
            CodebtorIds = Codebtors.Select(c => c.Id).ToList(),
            Shares = shares,
            StartDate = StartDate,
            NominalEndDate = NominalEndDate,
            MonthlyRent = rentRead ? rent : null,
            DueDay = dueDay,
            HonorariosPercentage = rate,
            CutoverDate = IsCutoverOffered ? CutoverDate : null,
            FeePlanAmount = feeAmount,
            FeePlanInstalments = feeInstalments,
            Clause = clause,
        };

        var unreadFields = unread.Select(p => p.Field).ToHashSet();
        problems = unread
            .Concat(ContractDraftRules.Problems(draft, _clock.Today).Where(p => !unreadFields.Contains(p.Field)))
            .OrderBy(p => p.Field)
            .ToList();

        return draft;
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

    private bool TryParseClause(out ClauseDraft? clause) =>
        ClauseFormRules.TryBuild(ClauseIndexIds, ClauseIntervalText, ClauseOffsetText, SuggestedClauseOffset, out clause);

    /// <summary>No share typed splits equally; a share left blank beside typed ones does not total anything.</summary>
    private bool TryBuildShares(decimal? rent, out IReadOnlyList<UnitShare> shares)
    {
        shares = [];
        if (SharesAsAmounts && rent is not > 0m)
        {
            return false;
        }

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
        ClauseOffsetText = string.Empty;
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
            LoadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            AcceptWarningsCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadAsync()
    {
        var outcome = await Runner.RunAsync(() => _indices.ListAsync());
        if (!outcome.Completed)
        {
            return;
        }

        ClauseIndexNames = IndexOption.Names(outcome.Value!);
        ClauseIndexOptions.Clear();
        foreach (var option in IndexOption.Offered(outcome.Value!, []))
        {
            ClauseIndexOptions.Add(option);
        }

        OnPropertyChanged(nameof(SuggestedClauseOffset));
        OnPropertyChanged(nameof(ClauseWindowText));
    }
}

/// <summary>Each field's error text, or null for a field without one; the screen shows each beside its field.</summary>
public sealed class ContractFieldErrors
{
    public const string SummaryText = "No se pudo guardar: revise los datos marcados.";

    public static readonly ContractFieldErrors None = new([]);

    private readonly Dictionary<ContractField, string> _texts;

    /// <param name="problems">In the form's order; a field with two problems shows the first.</param>
    public ContractFieldErrors(IEnumerable<ContractProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        _texts = problems
            .GroupBy(p => p.Field)
            .ToDictionary(g => g.Key, g => g.First().Refusal.OperatorText());
    }

    public string? this[ContractField field] => _texts.GetValueOrDefault(field);

    public int Count => _texts.Count;

    /// <summary>The line over the form while any field has an error.</summary>
    public string? Summary => _texts.Count > 0 ? SummaryText : null;
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
