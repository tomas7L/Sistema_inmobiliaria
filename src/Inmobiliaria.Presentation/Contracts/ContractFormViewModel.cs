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
using Inmobiliaria.Domain.Shared;
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

    /// <summary>Under the clause, once an adjustment is confirmed (canvas "Editar contrato").</summary>
    public const string ClauseLockedByAdjustment =
        "La cláusula ya no se puede cambiar: hay un ajuste confirmado. Un cambio vale desde el próximo ajuste y nunca toca uno ya " +
        "confirmado, pero a partir del primero confirmado queda cerrada.";

    /// <summary>Under the clause of an ended contract, as its detail says it.</summary>
    public const string ClauseLockedByEnd = "El contrato terminó: su canon ya no se ajusta ni se corrige.";

    /// <summary>What a save of an unchanged contract says: nothing is sent.</summary>
    public const string NothingChanged = "No hay cambios para guardar.";

    /// <summary>The saved notice's text after a correction (canvas "Editar contrato", under "Cambios guardados").</summary>
    public const string CorrectedText = "El contrato quedó con los datos corregidos.";

    /// <summary>The contract being corrected, or null for a new one.</summary>
    private readonly ContractDetail? _editing;

    /// <summary>What the contract holds as far as this form knows: what a save compares against, moved on as each part lands.</summary>
    private EditBaseline? _baseline;

    private readonly IContractRegistry _contracts;
    private readonly IAgencyClock _clock;
    private readonly IIndexRegistry _indices;
    private readonly IPartyRegistry? _parties;
    private readonly IUnitRegistry? _units;

    /// <summary>Counts edits, so a reply can tell whether it still describes what the form holds.</summary>
    private int _edits;

    /// <summary>Set while a date and its text follow each other, so neither answers the other.</summary>
    private bool _readingDate;

    /// <param name="parties">The people the pickers offer; without it they offer none.</param>
    /// <param name="units">The units the unit picker offers; without it, none.</param>
    public ContractFormViewModel(
        IContractRegistry contracts, bool canSetCutover, IAgencyClock clock, IIndexRegistry indices, IPartyRegistry? parties = null,
        IUnitRegistry? units = null, ContractDetail? editing = null)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(indices);

        _contracts = contracts;
        _clock = clock;
        _indices = indices;
        _parties = parties;
        _units = units;
        _editing = editing;
        IsCutoverOffered = canSetCutover;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        AcceptWarningsCommand = new AsyncRelayCommand(AcceptWarningsAsync, () => CanAct() && HasWarnings);
        CancelCommand = new RelayCommand(WithdrawWarnings);
        AddLessorCommand = new AsyncRelayCommand<PartyListing>(AddLessorAsync);
        TenantPicker = new FormPicker<PartyListing>(DescribeParty, person =>
        {
            Tenant = person;
            return Task.CompletedTask;
        });
        LessorPicker = new FormPicker<PartyListing>(DescribeParty, AddLessorAsync);
        CodebtorPicker = new FormPicker<PartyListing>(DescribeParty, person =>
        {
            Codebtors.Add(person);
            return Task.CompletedTask;
        });
        UnitPicker = new FormPicker<UnitListing>(DescribeUnit, unit =>
        {
            Units.Add(new ContractFormUnit(unit));
            return Task.CompletedTask;
        });
        ClearTenantCommand = new RelayCommand(() => Tenant = null);
        AddFeePlanCommand = new RelayCommand(() => HasFeePlan = true);
        RemoveFeePlanCommand = new RelayCommand(() => HasFeePlan = false);
        RemoveLessorCommand = new RelayCommand<PartyListing>(person => RemoveRow(Lessors, person));
        RemoveCodebtorCommand = new RelayCommand<PartyListing>(person => RemoveRow(Codebtors, person));
        RemoveUnitCommand = new RelayCommand<ContractFormUnit>(unit => RemoveRow(Units, unit));
        Lessors.CollectionChanged += OnFormCollectionChanged;
        Codebtors.CollectionChanged += OnFormCollectionChanged;
        Units.CollectionChanged += OnUnitsChanged;
        ClauseIndexIds.CollectionChanged += OnFormCollectionChanged;
        ClauseIndexIds.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SuggestedClauseOffset));
            OnPropertyChanged(nameof(ClauseWindowText));
            OnPropertyChanged(nameof(ClauseOffsetHint));
            foreach (var choice in ClauseIndexChoices)
            {
                choice.Reflect(ClauseIndexIds.Contains(choice.Option.Id));
            }
        };
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>Only an Admin sees the cutover section; anyone else's date is never sent.</summary>
    public bool IsCutoverOffered { get; }

    /// <summary>
    /// The cutover shows only where it serves, as on the canvas: an Administrador's contract that started before this month.
    /// </summary>
    public bool IsCutoverShown =>
        !IsEditing && IsCutoverOffered && StartDate is { } start && start < new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);

    /// <summary>Opened on a contract that exists (canvas "Editar contrato"): it corrects it rather than creating one.</summary>
    public bool IsEditing => _editing is not null;

    /// <summary>
    /// The contract's own data, everything but the honorarios, the codeudores and the clause, can no longer be corrected: it has
    /// movements, a confirmed adjustment, or it ended (spec Requirement 9).
    /// </summary>
    public bool IsCoreLocked => _editing?.CorrectionLock is not null;

    /// <summary>The clause cannot change either: an adjustment is confirmed (spec Decision 32), or the contract ended.</summary>
    public bool IsClauseLocked => _editing?.CorrectionLock is ContractRefusal.AdjustmentConfirmed or ContractRefusal.ContractEnded;

    /// <summary>The banner over a locked form, saying why; null while nothing is locked.</summary>
    public string? LockText => _editing?.CorrectionLock?.OperatorText();

    /// <summary>Why the clause is locked, under it; null while it is not.</summary>
    public string? ClauseLockText => _editing?.CorrectionLock switch
    {
        ContractRefusal.AdjustmentConfirmed => ClauseLockedByAdjustment,
        ContractRefusal.ContractEnded => ClauseLockedByEnd,
        _ => null,
    };

    public ObservableCollection<PartyListing> Lessors { get; } = [];

    public ObservableCollection<PartyListing> Codebtors { get; } = [];

    public ObservableCollection<ContractFormUnit> Units { get; } = [];

    public ObservableCollection<Guid> ClauseIndexIds { get; } = [];

    /// <summary>The indices a new clause can choose: the catalog's, less the discontinued ones (task 12.3c).</summary>
    public ObservableCollection<IndexOption> ClauseIndexOptions { get; } = [];

    /// <summary>The same indices as switches, for the screen: turning one on or off adds it to or takes it from the clause.</summary>
    public ObservableCollection<ClauseIndexChoice> ClauseIndexChoices { get; } = [];

    /// <summary>What the indices chosen suggest for the window's end, beside its choices (canvas "Nuevo contrato").</summary>
    public string ClauseOffsetHint
    {
        get
        {
            var chosen = ClauseIndexIds.Where(ClauseIndexNames.ContainsKey).Select(id => ClauseIndexNames[id]).ToList();
            return chosen.Count == 0 || SuggestedClauseOffset is not { } suggested
                ? "Se sugiere según los índices elegidos: ICL 0, IPC 2, y 3 si lleva RIPTE."
                : $"Sugerido para {string.Join(" y ", chosen)}: {suggested}. Son los últimos valores que normalmente ya están publicados al vencer.";
        }
    }

    /// <summary>How many months the contract runs, counting both ends' months, once both dates are there.</summary>
    public string DurationText
    {
        get
        {
            if (StartDate is not { } start || NominalEndDate is not { } end)
            {
                return "Escriba las dos fechas para ver la duración.";
            }

            var months = ((end.Year * 12) + end.Month) - ((start.Year * 12) + start.Month) + 1;
            return months switch
            {
                < 1 => "Escriba las dos fechas para ver la duración.",
                1 => "Duración: 1 mes",
                _ => $"Duración: {months} meses",
            };
        }
    }

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

    /// <summary>The tenant, typed and picked (canvas "Nuevo contrato").</summary>
    public FormPicker<PartyListing> TenantPicker { get; }

    /// <summary>A lessor, typed and picked; each one picked is a row.</summary>
    public FormPicker<PartyListing> LessorPicker { get; }

    public FormPicker<PartyListing> CodebtorPicker { get; }

    /// <summary>A unit, typed by its address and picked, saying whether a contract in force already covers it.</summary>
    public FormPicker<UnitListing> UnitPicker { get; }

    public IRelayCommand ClearTenantCommand { get; }

    /// <summary>"+ Agregar honorarios contractuales".</summary>
    public IRelayCommand AddFeePlanCommand { get; }

    /// <summary>"Quitar los honorarios contractuales": forgets what was typed.</summary>
    public IRelayCommand RemoveFeePlanCommand { get; }

    /// <summary>"Quitar": takes one row away.</summary>
    public IRelayCommand<PartyListing> RemoveLessorCommand { get; }

    public IRelayCommand<PartyListing> RemoveCodebtorCommand { get; }

    public IRelayCommand<ContractFormUnit> RemoveUnitCommand { get; }

    [ObservableProperty]
    private PartyListing? _tenant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClauseWindowText), nameof(DurationText))]
    private DateOnly? _startDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private DateOnly? _nominalEndDate;

    /// <summary>The start as typed: dd/mm/aa, or the month and year alone (<see cref="DateEntry"/>).</summary>
    [ObservableProperty]
    private string _startDateText = string.Empty;

    /// <summary>The end as typed: a month and year alone is its last day.</summary>
    [ObservableProperty]
    private string _nominalEndDateText = string.Empty;

    [ObservableProperty]
    private string _cutoverDateText = string.Empty;

    /// <summary>
    /// Whether the rent adjusts: the canvas starts on a fixed rent. Choosing a clause starts it every six months; choosing a
    /// fixed rent again forgets the clause's indices, interval and offset.
    /// </summary>
    [ObservableProperty]
    private bool _hasClause;

    /// <summary>
    /// Whether the fee plan's fields show: behind "+ Agregar honorarios contractuales", as on the canvas. Taking it away
    /// forgets what was typed; a plan typed shows them.
    /// </summary>
    [ObservableProperty]
    private bool _hasFeePlan;

    partial void OnHasFeePlanChanged(bool value)
    {
        if (!value)
        {
            FeePlanAmountText = string.Empty;
            FeePlanInstalmentsText = string.Empty;
        }
    }

    /// <summary>What the saved notice says beside "Contrato guardado", or null before a save.</summary>
    [ObservableProperty]
    private string? _savedText;

    partial void OnHasClauseChanged(bool value)
    {
        if (value)
        {
            if (string.IsNullOrWhiteSpace(ClauseIntervalText))
            {
                ClauseIntervalText = "6";
            }

            return;
        }

        ClauseIndexIds.Clear();
        ClauseIntervalText = string.Empty;
        ClauseOffsetText = string.Empty;
    }

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

    /// <summary>The contract just corrected, every part of the save landed; or null.</summary>
    [ObservableProperty]
    private ContractListing? _corrected;

    partial void OnTenantChanged(PartyListing? value) => WithdrawWarnings();

    partial void OnStartDateChanged(DateOnly? value)
    {
        ShowDate(value, StartDateText, DateEntryKind.Start, text => StartDateText = text);
        ForgetHiddenCutover();
        WithdrawWarnings();
    }

    partial void OnNominalEndDateChanged(DateOnly? value)
    {
        ShowDate(value, NominalEndDateText, DateEntryKind.End, text => NominalEndDateText = text);
        WithdrawWarnings();
    }

    partial void OnCutoverDateChanged(DateOnly? value)
    {
        ShowDate(value, CutoverDateText, DateEntryKind.Day, text => CutoverDateText = text);
        WithdrawWarnings();
    }

    // A date typed short is read on leaving its field and shown in full; text that is no date stays, to be refused beside it.
    partial void OnStartDateTextChanged(string value) =>
        ReadDate(value, ContractField.StartDate, DateEntryKind.Start, date => StartDate = date, text => StartDateText = text);

    partial void OnNominalEndDateTextChanged(string value) =>
        ReadDate(value, ContractField.EndDate, DateEntryKind.End, date => NominalEndDate = date, text => NominalEndDateText = text);

    partial void OnCutoverDateTextChanged(string value) =>
        ReadDate(value, ContractField.Cutover, DateEntryKind.Day, date => CutoverDate = date, text => CutoverDateText = text);

    /// <summary>
    /// Reads a date on leaving its field. Text that is no date is said beside it at once, and the word goes once it is right
    /// (Tomás, 2026-11-17); every other problem still waits for "Guardar".
    /// </summary>
    private void ReadDate(string text, ContractField field, DateEntryKind kind, Action<DateOnly?> setDate, Action<string> setText)
    {
        if (_readingDate)
        {
            return;
        }

        _readingDate = true;
        var date = DateEntry.Parse(text, kind);
        setDate(date);
        if (date is { } read)
        {
            setText(Formats.Date(read));
        }

        _readingDate = false;
        Errors = !string.IsNullOrWhiteSpace(text) && date is null
            ? Errors.With(new ContractProblem(field, ContractRefusal.DateNotUnderstood))
            : Errors.Without(field);
    }

    /// <summary>A cutover the screen no longer shows is forgotten, so nothing unseen is sent.</summary>
    private void ForgetHiddenCutover()
    {
        OnPropertyChanged(nameof(IsCutoverShown));
        if (IsCutoverShown || (CutoverDate is null && CutoverDateText.Length == 0))
        {
            return;
        }

        var reading = _readingDate;
        _readingDate = true;
        CutoverDate = null;
        CutoverDateText = string.Empty;
        _readingDate = reading;
        Errors = Errors.Without(ContractField.Cutover);
    }

    /// <summary>A date set by the form itself, not typed, shows in its field; one just read from the field leaves it alone.</summary>
    private void ShowDate(DateOnly? date, string text, DateEntryKind kind, Action<string> setText)
    {
        if (_readingDate || DateEntry.Parse(text, kind) == date)
        {
            return;
        }

        _readingDate = true;
        setText(date is { } shown ? Formats.Date(shown) : string.Empty);
        _readingDate = false;
    }

    // An amount typed without its dots is shown with them (Tomás, 2026-11-16); the view binds these fields on leaving them.
    partial void OnMonthlyRentTextChanged(string value)
    {
        MonthlyRentText = Formats.TidyMoney(value);
        WithdrawWarnings();
    }

    partial void OnDueDayTextChanged(string value) => WithdrawWarnings();

    partial void OnHonorariosTextChanged(string value) => WithdrawWarnings();

    partial void OnFeePlanAmountTextChanged(string value)
    {
        FeePlanAmountText = Formats.TidyMoney(value);
        HasFeePlan |= !string.IsNullOrWhiteSpace(value);
        WithdrawWarnings();
    }

    partial void OnFeePlanInstalmentsTextChanged(string value)
    {
        HasFeePlan |= !string.IsNullOrWhiteSpace(value);
        WithdrawWarnings();
    }

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
        if (lessor is null)
        {
            return;
        }

        // Chosen again: it takes its row and is refused under it at once, as on Mateo's canvas (Tomás 2026-11-16).
        if (Lessors.Any(l => l.Id == lessor.Id))
        {
            Lessors.Add(lessor);
            Errors = new ContractFieldErrors([new ContractProblem(ContractField.Lessors, ContractRefusal.LessorRepeated, Lessors.Count - 1)]);
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
        Corrected = null;
        SavedText = null;
        Errors = ContractFieldErrors.None;

        if (_editing is { } standing)
        {
            await SubmitCorrectionAsync(standing, acceptWarnings);
            return;
        }

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
            SavedText = draft.HonorariosPercentage is null
                ? "Se creó el contrato junto con su cuenta, vacía. Ya se puede cobrar desde la caja, apenas se carguen los honorarios de administración."
                : "Se creó el contrato junto con su cuenta, vacía. Ya se puede cobrar desde la caja.";

            // What was typed while this was out is not the contract just written, so it is not thrown away with it.
            if (!editedSince)
            {
                ClearForm();
            }

            Created = result.Contract;
        }
        else if (result.Refusal is { } refusal && UnitRow(result.UnitId) is { } row)
        {
            Errors = ContractFieldErrors.OnRow(ContractField.Units, row, refusal.OperatorText(result.CurrentTenant));
        }
        else if (result.Refusal is { } other)
        {
            RefusalMessage = other.OperatorText(result.CurrentTenant);
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
    /// A correction sends only what changed, each part through its own call (spec Requirement 9, Decisions 32 and 33): the
    /// contract's data first, since it is the part warned about and most often refused, then the codeudores, the rate and the
    /// clause. What is locked is never sent. A part refused after another landed says what already saved, and the next save sends
    /// only what is left.
    /// </summary>
    private async Task SubmitCorrectionAsync(ContractDetail standing, bool acceptWarnings)
    {
        var baseline = _baseline ??= EditBaseline.Of(standing);
        var draft = BuildDraft(out var found);

        var problems = found.Where(p => IsEditable(p.Field)).ToList();
        if (!IsCoreLocked && Units.Count != standing.Units.Count)
        {
            problems.Add(new ContractProblem(ContractField.Units, ContractRefusal.UnitCountChanged));
        }

        if (draft.HonorariosPercentage is null && baseline.Rate is not null && problems.All(p => p.Field != ContractField.Honorarios))
        {
            problems.Add(new ContractProblem(ContractField.Honorarios, ContractRefusal.HonorariosCannotBeCleared));
        }

        if (!IsClauseLocked && draft.Clause is null && baseline.Clause is not null && problems.All(p => p.Field != ContractField.Clause))
        {
            problems.Add(new ContractProblem(ContractField.Clause, ContractRefusal.ClauseCannotBeRemoved));
        }

        if (problems.Count > 0)
        {
            Errors = new ContractFieldErrors(problems.OrderBy(p => p.Field));
            return;
        }

        var id = standing.Id;
        var parts = new List<(string Name, Func<Task<ContractResult>> Send, Action Landed)>();
        if (!IsCoreLocked && baseline.CoreDiffers(draft))
        {
            parts.Add(("los datos del contrato", () => _contracts.CorrectAsync(id, CorrectionOf(draft), acceptWarnings), () => _baseline = _baseline!.WithCore(draft)));
        }

        if (baseline.CodebtorsDiffer(draft.CodebtorIds))
        {
            parts.Add(("los codeudores", () => _contracts.SetCodebtorsAsync(id, draft.CodebtorIds), () => _baseline = _baseline! with { CodebtorIds = draft.CodebtorIds }));
        }

        if (draft.HonorariosPercentage is { } rate && rate != baseline.Rate)
        {
            parts.Add(("los honorarios", () => _contracts.SetRateAsync(id, rate), () => _baseline = _baseline! with { Rate = rate }));
        }

        if (!IsClauseLocked && draft.Clause is { } clause && baseline.ClauseDiffers(clause))
        {
            parts.Add(("la cláusula", () => _contracts.SetClauseAsync(id, clause), () => _baseline = _baseline! with { Clause = clause }));
        }

        if (parts.Count == 0)
        {
            RefusalMessage = NothingChanged;
            return;
        }

        var editsWhenSent = _edits;
        var landed = new List<string>();
        ContractListing? last = null;
        foreach (var part in parts)
        {
            var outcome = await Runner.RunAsync(part.Send);
            if (!outcome.Completed)
            {
                RefusalMessage = AlreadySaved(landed);
                return;
            }

            var result = outcome.Value!;
            if (result.Succeeded)
            {
                part.Landed();
                landed.Add(part.Name);
                last = result.Contract;
                continue;
            }

            if (result.Refusal is { } refusal && UnitRow(result.UnitId) is { } row)
            {
                Errors = ContractFieldErrors.OnRow(ContractField.Units, row, refusal.OperatorText(result.CurrentTenant));
                RefusalMessage = AlreadySaved(landed);
            }
            else if (result.Refusal is { } other)
            {
                RefusalMessage = AlreadySaved(landed) is { } saved
                    ? $"{other.OperatorText(result.CurrentTenant)} {saved}"
                    : other.OperatorText(result.CurrentTenant);
            }
            else if (_edits != editsWhenSent)
            {
                RefusalMessage = FormChangedWhileSaving;
            }
            else
            {
                SetWarnings(result.Warnings.Select(w => w.OperatorText()));
            }

            return;
        }

        SavedText = CorrectedText;
        Corrected = last;
    }

    /// <summary>What a stopped correction had already written, or null when nothing was.</summary>
    private static string? AlreadySaved(IReadOnlyList<string> landed) => landed.Count switch
    {
        0 => null,
        1 => $"Quedó guardado: {landed[0]}.",
        _ => $"Quedó guardado: {string.Join(", ", landed.Take(landed.Count - 1))} y {landed[^1]}.",
    };

    /// <summary>Whether a field's problem stops a correction: a locked field is never sent, so its problems are not the operator's.</summary>
    private bool IsEditable(ContractField field) =>
        !IsCoreLocked
        || field is ContractField.Honorarios or ContractField.Codebtors
        || (field == ContractField.Clause && !IsClauseLocked);

    private static ContractCorrection CorrectionOf(ContractDraft draft) => new()
    {
        TenantId = draft.TenantId,
        LessorIds = draft.LessorIds,
        Shares = draft.Shares,
        StartDate = draft.StartDate,
        NominalEndDate = draft.NominalEndDate,
        MonthlyRent = draft.MonthlyRent,
        DueDay = draft.DueDay,
        FeePlanAmount = draft.FeePlanAmount,
        FeePlanInstalments = draft.FeePlanInstalments,
    };

    /// <summary>The contract as it stands, shown on the form so it is corrected rather than typed again (canvas "Editar contrato").</summary>
    private void Fill(ContractDetail standing, IReadOnlyList<PartyListing> people, IReadOnlyList<UnitListing> units)
    {
        // A person the list no longer offers still shows, by the name the contract gives them.
        PartyListing Person(ContractPartyLine line) =>
            people.FirstOrDefault(p => p.Id == line.PartyId) ?? new PartyListing(line.PartyId, string.Empty, line.Name, null, null, null, null, null);

        IEnumerable<PartyListing> InRole(PartyRole role) => standing.Parties.Where(p => p.Role == role).Select(Person);

        Tenant = InRole(PartyRole.Tenant).FirstOrDefault();
        foreach (var lessor in InRole(PartyRole.Lessor))
        {
            Lessors.Add(lessor);
        }

        foreach (var codebtor in InRole(PartyRole.Codebtor))
        {
            Codebtors.Add(codebtor);
        }

        foreach (var line in standing.Units)
        {
            if (units.FirstOrDefault(u => u.Id == line.UnitId) is { } unit)
            {
                Units.Add(new ContractFormUnit(unit) { ShareText = standing.Units.Count > 1 ? Formats.Number(line.Percentage) : string.Empty });
            }
        }

        StartDate = standing.StartDate;
        NominalEndDate = standing.NominalEndDate;
        MonthlyRentText = Whole(standing.MonthlyRent);
        DueDayText = standing.DueDay.ToString(CultureInfo.InvariantCulture);
        HonorariosText = standing.HonorariosPercentage is { } rate ? Formats.Number(rate) : string.Empty;
        if (standing.FeePlan is { } plan)
        {
            FeePlanAmountText = Whole(plan.Amount);
            FeePlanInstalmentsText = plan.Instalments.ToString(CultureInfo.InvariantCulture);
        }

        if (standing.Clause is { } clause)
        {
            HasClause = true;
            foreach (var indexId in clause.IndexIds)
            {
                ClauseIndexIds.Add(indexId);
            }

            ClauseIntervalText = clause.IntervalMonths.ToString(CultureInfo.InvariantCulture);
            ClauseOffsetText = clause.WindowOffsetMonths.ToString(CultureInfo.InvariantCulture);
        }

        _baseline = EditBaseline.Of(standing);
        Errors = ContractFieldErrors.None;
        WithdrawWarnings();
    }

    private static string Whole(decimal amount) => amount.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// What was typed, with every problem the form can find on its own (spec Decision 44): text that is not even a number,
    /// beside its field, and the rules' own (<see cref="ContractDraftRules.Problems"/>) for the rest, in the form's order.
    /// A field whose text could not be read gets only that problem, not the rules' about the blank it leaves.
    /// </summary>
    private ContractDraft BuildDraft(out IReadOnlyList<ContractProblem> problems)
    {
        var unread = new List<ContractProblem>();

        // Text typed that is no date: refused as such, not as a date missing.
        foreach (var (field, text, date) in new[]
        {
            (ContractField.StartDate, StartDateText, StartDate),
            (ContractField.EndDate, NominalEndDateText, NominalEndDate),
            (ContractField.Cutover, IsCutoverOffered ? CutoverDateText : string.Empty, CutoverDate),
        })
        {
            if (!string.IsNullOrWhiteSpace(text) && date is null)
            {
                unread.Add(new ContractProblem(field, ContractRefusal.DateNotUnderstood));
            }
        }

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

        // A correction keeps the contract's cutover, and its start may be one that has already passed (ContractDraftRules.Check).
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
            CutoverDate = _editing is { } standing ? standing.CutoverDate : IsCutoverOffered ? CutoverDate : null,
            FeePlanAmount = feeAmount,
            FeePlanInstalments = feeInstalments,
            Clause = clause,
        };

        var unreadFields = unread.Select(p => p.Field).ToHashSet();
        problems = unread
            .Concat(ContractDraftRules.Problems(draft, _clock.Today, _editing?.StartDate).Where(p => !unreadFields.Contains(p.Field)))
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

    private void ChooseIndex(Guid indexId, bool chosen)
    {
        if (chosen && !ClauseIndexIds.Contains(indexId))
        {
            ClauseIndexIds.Add(indexId);
        }
        else if (!chosen)
        {
            ClauseIndexIds.Remove(indexId);
        }
    }

    /// <summary>A row's error names its position, so taking a row away withdraws the errors rather than leave them on others.</summary>
    private void RemoveRow<TRow>(ObservableCollection<TRow> rows, TRow? row)
    {
        if (row is not null && rows.Remove(row))
        {
            Errors = ContractFieldErrors.None;
        }
    }

    private static PickerOption<PartyListing> DescribeParty(PartyListing person)
    {
        var name = $"{person.LastName}, {person.FirstName}";
        var roles = person.ActiveRoles.Count == 0 ? null : string.Join(", ", person.ActiveRoles.Select(Formats.Role));

        return new PickerOption<PartyListing>(
            person, name, roles, TextFold.Fold($"{name} {person.FirstName} {person.LastName} {person.Dni} {person.Cuil}"));
    }

    private static PickerOption<UnitListing> DescribeUnit(UnitListing unit)
    {
        var tag = unit.Covering switch
        {
            null => "Disponible",
            { Status: ContractStatus.PendingTermination } covering => $"En rescisión, de {covering.TenantName}",
            var covering => $"Alquilada a {covering.TenantName}",
        };

        return new PickerOption<UnitListing>(unit, unit.AddressText, tag, TextFold.Fold(unit.AddressText));
    }

    /// <summary>The row of the unit a refusal names, or null when it names none or that unit is no longer on the form.</summary>
    private int? UnitRow(Guid? unitId)
    {
        for (var row = 0; unitId is not null && row < Units.Count; row++)
        {
            if (Units[row].Unit.Id == unitId)
            {
                return row;
            }
        }

        return null;
    }

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
        ClauseIndexChoices.Clear();
        foreach (var option in IndexOption.Offered(outcome.Value!, []))
        {
            ClauseIndexOptions.Add(option);
            ClauseIndexChoices.Add(new ClauseIndexChoice(option, ClauseIndexIds.Contains(option.Id), ChooseIndex));
        }

        OnPropertyChanged(nameof(SuggestedClauseOffset));
        OnPropertyChanged(nameof(ClauseWindowText));

        IReadOnlyList<PartyListing> people = [];
        if (_parties is not null && await Runner.RunAsync(() => _parties.SearchAsync(null)) is { Completed: true, Value: { } found })
        {
            people = found;
            TenantPicker.SetOptions(people);
            LessorPicker.SetOptions(people);
            CodebtorPicker.SetOptions(people);
        }

        IReadOnlyList<UnitListing> units = [];
        if (_units is not null && await Runner.RunAsync(() => _units.ListAsync()) is { Completed: true, Value: { } listed })
        {
            units = listed;
            UnitPicker.SetOptions(units);
        }

        // Filled once, after the lists, so each person and unit is the one the pickers offer.
        if (_editing is { } standing && _baseline is null)
        {
            Fill(standing, people, units);
        }
    }

    /// <summary>The contract's parts as last known, so a save sends only what differs from them.</summary>
    private sealed record EditBaseline(
        Guid? TenantId,
        IReadOnlyList<Guid> LessorIds,
        IReadOnlyList<UnitShare> Shares,
        DateOnly? StartDate,
        DateOnly? NominalEndDate,
        decimal? MonthlyRent,
        int DueDay,
        decimal? FeePlanAmount,
        int? FeePlanInstalments,
        decimal? Rate,
        IReadOnlyList<Guid> CodebtorIds,
        ClauseDraft? Clause)
    {
        public static EditBaseline Of(ContractDetail standing) => new(
            standing.Parties.FirstOrDefault(p => p.Role == PartyRole.Tenant)?.PartyId,
            standing.Parties.Where(p => p.Role == PartyRole.Lessor).Select(p => p.PartyId).ToList(),
            standing.Units.Select(u => new UnitShare(u.UnitId, u.Percentage)).ToList(),
            standing.StartDate,
            standing.NominalEndDate,
            standing.MonthlyRent,
            standing.DueDay,
            standing.FeePlan?.Amount,
            standing.FeePlan?.Instalments,
            standing.HonorariosPercentage,
            standing.Parties.Where(p => p.Role == PartyRole.Codebtor).Select(p => p.PartyId).ToList(),
            standing.Clause is { } clause ? new ClauseDraft(clause.IndexIds, clause.IntervalMonths, clause.WindowOffsetMonths) : null);

        public bool CoreDiffers(ContractDraft draft) =>
            draft.TenantId != TenantId
            || !draft.LessorIds.Order().SequenceEqual(LessorIds.Order())
            || !draft.Shares.OrderBy(s => s.UnitId).SequenceEqual(Shares.OrderBy(s => s.UnitId))
            || draft.StartDate != StartDate
            || draft.NominalEndDate != NominalEndDate
            || draft.MonthlyRent != MonthlyRent
            || draft.DueDay != DueDay
            || draft.FeePlanAmount != FeePlanAmount
            || draft.FeePlanInstalments != FeePlanInstalments;

        public EditBaseline WithCore(ContractDraft draft) => this with
        {
            TenantId = draft.TenantId,
            LessorIds = draft.LessorIds,
            Shares = draft.Shares,
            StartDate = draft.StartDate,
            NominalEndDate = draft.NominalEndDate,
            MonthlyRent = draft.MonthlyRent,
            DueDay = draft.DueDay,
            FeePlanAmount = draft.FeePlanAmount,
            FeePlanInstalments = draft.FeePlanInstalments,
        };

        public bool CodebtorsDiffer(IReadOnlyList<Guid> codebtorIds) => !codebtorIds.Order().SequenceEqual(CodebtorIds.Order());

        public bool ClauseDiffers(ClauseDraft clause) =>
            Clause is null
            || !clause.IndexIds.Order().SequenceEqual(Clause.IndexIds.Order())
            || clause.IntervalMonths != Clause.IntervalMonths
            || clause.WindowOffsetMonths != Clause.WindowOffsetMonths;
    }
}

/// <summary>Each field's error text, or null for a field without one; the screen shows each beside its field.</summary>
/// <summary>The form's tabs, as on the canvas.</summary>
public enum ContractFormSection
{
    Parties,
    Units,
    DatesAndAmounts,
    Clause,
}

public sealed class ContractFieldErrors
{
    public const string SummaryText = "No se pudo guardar: revise los datos marcados.";

    public static readonly ContractFieldErrors None = new([]);

    private readonly Dictionary<ContractField, string> _texts;

    private readonly Dictionary<(ContractField Field, int Row), string> _rows;

    /// <param name="problems">In the form's order; a field, or a row of one, with two problems shows the first.</param>
    public ContractFieldErrors(IEnumerable<ContractProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var all = problems.ToList();
        _texts = all
            .Where(p => p.Row is null)
            .GroupBy(p => p.Field)
            .ToDictionary(g => g.Key, g => g.First().Refusal.OperatorText());
        _rows = all
            .Where(p => p.Row is not null)
            .GroupBy(p => (p.Field, p.Row!.Value))
            .ToDictionary(g => g.Key, g => g.First().Refusal.OperatorText());
    }

    private ContractFieldErrors(ContractField field, int row, string text)
    {
        _texts = [];
        _rows = new() { [(field, row)] = text };
    }

    private ContractFieldErrors(Dictionary<ContractField, string> texts, Dictionary<(ContractField Field, int Row), string> rows)
    {
        _texts = texts;
        _rows = rows;
    }

    /// <summary>These errors with one field's own replaced by <paramref name="problem"/>, its rows' kept.</summary>
    public ContractFieldErrors With(ContractProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var texts = new Dictionary<ContractField, string>(_texts) { [problem.Field] = problem.Refusal.OperatorText() };
        return new ContractFieldErrors(texts, new Dictionary<(ContractField Field, int Row), string>(_rows));
    }

    /// <summary>These errors without one field's own; the summary goes with the last.</summary>
    public ContractFieldErrors Without(ContractField field)
    {
        if (!_texts.ContainsKey(field))
        {
            return this;
        }

        var texts = new Dictionary<ContractField, string>(_texts);
        texts.Remove(field);
        return new ContractFieldErrors(texts, new Dictionary<(ContractField Field, int Row), string>(_rows));
    }

    /// <summary>One error under one row, with a text the port wrote, such as who rents a unit.</summary>
    public static ContractFieldErrors OnRow(ContractField field, int row, string text) => new(field, row, text);

    /// <summary>The error of the field as a whole; a row's own is in <see cref="ForRow"/>.</summary>
    public string? this[ContractField field] => _texts.GetValueOrDefault(field);

    /// <summary>The error under one row of a list field (lessors, codeudores, units), by its position.</summary>
    public string? ForRow(ContractField field, int row) => _rows.GetValueOrDefault((field, row));

    public int Count => _texts.Count + _rows.Count;

    /// <summary>How many errors one tab of the form holds, its rows' included, for the mark on the tab (canvas "Nuevo contrato").</summary>
    public int CountIn(ContractFormSection section) =>
        _texts.Keys.Count(field => SectionOf(field) == section) + _rows.Keys.Count(key => SectionOf(key.Field) == section);

    /// <summary>Each tab's count, for binding: Partes.</summary>
    public int PartiesCount => CountIn(ContractFormSection.Parties);

    /// <summary>Propiedad.</summary>
    public int UnitsCount => CountIn(ContractFormSection.Units);

    /// <summary>Fechas e importes.</summary>
    public int DatesAndAmountsCount => CountIn(ContractFormSection.DatesAndAmounts);

    /// <summary>Cláusula de ajuste.</summary>
    public int ClauseCount => CountIn(ContractFormSection.Clause);

    private static ContractFormSection SectionOf(ContractField field) => field switch
    {
        ContractField.Tenant or ContractField.Lessors or ContractField.Codebtors => ContractFormSection.Parties,
        ContractField.Units or ContractField.Shares => ContractFormSection.Units,
        ContractField.Clause => ContractFormSection.Clause,
        _ => ContractFormSection.DatesAndAmounts,
    };

    /// <summary>The line over the form while any field has an error.</summary>
    public string? Summary => Count > 0 ? SummaryText : null;
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

    // An amount typed without its dots is shown with them (Tomás, 2026-11-16).
    partial void OnShareTextChanged(string value) => ShareText = Formats.TidyMoney(value);
}

/// <summary>One index of the clause as a switch on the screen.</summary>
public sealed partial class ClauseIndexChoice : ObservableObject
{
    private readonly Action<Guid, bool> _choose;
    private bool _reflecting;

    public ClauseIndexChoice(IndexOption option, bool isChosen, Action<Guid, bool> choose)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(choose);

        Option = option;
        _isChosen = isChosen;
        _choose = choose;
    }

    public IndexOption Option { get; }

    [ObservableProperty]
    private bool _isChosen;

    /// <summary>Follows the clause when it changed elsewhere, without changing it again.</summary>
    public void Reflect(bool chosen)
    {
        _reflecting = true;
        IsChosen = chosen;
        _reflecting = false;
    }

    partial void OnIsChosenChanged(bool value)
    {
        if (!_reflecting)
        {
            _choose(Option.Id, value);
        }
    }
}
