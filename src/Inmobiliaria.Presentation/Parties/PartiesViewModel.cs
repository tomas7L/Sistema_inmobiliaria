using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Parties;

/// <summary>
/// The Personas screen (gestion-de-alquileres spec Requirements 1 to 3, design §6): search, a form to create or
/// correct a person, the old and new values to confirm a correction of name, DNI or CUIL, the warning for a person
/// with neither identifier, and the offer to use whoever already holds a DNI or CUIL. The list is read again after
/// every action, refused or not. Both roles. As the designer drew it (tasks 12.3d), the form is a screen of its own,
/// opened from "Nueva persona" or "Editar", and a write that lands ends on a saved notice with the way back.
/// </summary>
public sealed partial class PartiesViewModel : ObservableObject
{
    /// <summary>What a correction shows for a value that is, or becomes, empty.</summary>
    public const string AbsentValue = "(sin dato)";

    private readonly IPartyRegistry _parties;

    /// <summary>What the last search brought. The role filter works on this, so choosing one asks the database nothing.</summary>
    private IReadOnlyList<PartyListing> _found = [];

    /// <summary>True once the operator confirmed old and new identity data, so the saved notice says the recibos keep theirs.</summary>
    private bool _identityConfirmed;

    public PartiesViewModel(IPartyRegistry parties)
    {
        ArgumentNullException.ThrowIfNull(parties);

        _parties = parties;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SearchCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && Pending is not null);
        AcceptNoIdentifierCommand = new AsyncRelayCommand(AcceptNoIdentifierAsync, () => CanAct() && NoIdentifierWarning is not null);
        EditCommand = new RelayCommand<PartyRow>(Edit, row => row is not null);
        NewCommand = new RelayCommand(OpenNew);
        BackCommand = new RelayCommand(Back);
        CancelCommand = new RelayCommand(Cancel);
        ClearFormCommand = new RelayCommand(ClearForm);
        UseHolderCommand = new RelayCommand(UseHolder, () => OfferedHolder is not null);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<PartyRow> Persons { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IAsyncRelayCommand AcceptNoIdentifierCommand { get; }

    public IRelayCommand<PartyRow> EditCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearFormCommand { get; }

    public IRelayCommand UseHolderCommand { get; }

    /// <summary>"Nueva persona": an empty form.</summary>
    public IRelayCommand NewCommand { get; }

    /// <summary>"Volver a personas" and "Cancelar": back to the list, with the form cleared.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>True while the form, or its saved notice, is shown in place of the list.</summary>
    [ObservableProperty]
    private bool _isFormOpen;

    /// <summary>What the last write that landed saved, shown in place of the form until the operator goes back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step))]
    private PartySaved? _saved;

    /// <summary>The person selected in the list, which "Editar" opens.</summary>
    [ObservableProperty]
    private PartyRow? _selectedPerson;

    public string FormTitle => Editing is null ? PartiesTexts.NewTitle : PartiesTexts.EditTitle;

    /// <summary>Which part of the form is shown: one at a time, in the order the operator meets them.</summary>
    public PartyFormStep Step =>
        Saved is not null ? PartyFormStep.Saved
        : Pending is not null ? PartyFormStep.Confirm
        : NoIdentifierWarning is not null ? PartyFormStep.NoIdentifier
        : PartyFormStep.Fields;

    /// <summary>The foot of the list: how many people it shows.</summary>
    public string CountText => Persons.Count == 1 ? "1 persona" : $"{Persons.Count} personas";

    /// <summary>True when the search and the filter leave no one to show.</summary>
    public bool IsEmpty => Persons.Count == 0;

    public string FormSubtitle => Editing?.ListName ?? PartiesTexts.NewSubtitle;

    public string SaveText => Editing is null ? PartiesTexts.SaveNew : PartiesTexts.SaveCorrection;

    /// <summary>The note under the identity fields: no duplicates when creating, what may be corrected when editing.</summary>
    public string IdentityHint => Editing is null ? PartiesTexts.NewIdentityHint : PartiesTexts.EditIdentityHint;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The role to list, or null for everyone.</summary>
    [ObservableProperty]
    private PartyRole? _roleFilter;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _dni = string.Empty;

    [ObservableProperty]
    private string _cuil = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _domicile = string.Empty;

    /// <summary>The person the form is correcting, or null while it creates one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSubtitle), nameof(SaveText), nameof(IdentityHint))]
    private PartyRow? _editing;

    /// <summary>The correction waiting for confirmation, with old and new values, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(Step))]
    private PendingPartyCorrection? _pending;

    /// <summary>The warning that duplicates of this person cannot be detected, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptNoIdentifierCommand))]
    [NotifyPropertyChangedFor(nameof(Step))]
    private string? _noIdentifierWarning;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>Whoever already holds the identifier just refused on creating, offered in place of a duplicate.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseHolderCommand))]
    private PartyListing? _offeredHolder;

    /// <summary>The existing person the operator chose to use instead of creating one.</summary>
    [ObservableProperty]
    private PartyRow? _chosenParty;

    partial void OnRoleFilterChanged(PartyRole? value) => ShowFound();

    partial void OnFirstNameChanged(string value) => WithdrawConfirmations();

    partial void OnLastNameChanged(string value) => WithdrawConfirmations();

    partial void OnDniChanged(string value) => WithdrawConfirmations();

    partial void OnCuilChanged(string value) => WithdrawConfirmations();

    partial void OnEmailChanged(string value) => WithdrawConfirmations();

    partial void OnPhoneNumberChanged(string value) => WithdrawConfirmations();

    partial void OnDomicileChanged(string value) => WithdrawConfirmations();

    /// <summary>
    /// What is confirmed is what is written (spec Requirement 3), and a write takes the form as it stands, so a confirmation
    /// or an accepted warning covers only the values it was asked about. Editing a field means saving again.
    /// </summary>
    private void WithdrawConfirmations()
    {
        Pending = null;
        NoIdentifierWarning = null;
    }

    private bool CanAct() => !Runner.IsBusy;

    private PartyDraft Draft() => new(FirstName, LastName, Dni, Cuil, Email, PhoneNumber, Domicile);

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private void ShowFound()
    {
        Persons.Clear();
        foreach (var listing in _found.Where(p => RoleFilter is not { } role || p.ActiveRoles.Contains(role)))
        {
            Persons.Add(new PartyRow(listing));
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(() => _parties.SearchAsync(SearchText));
        if (!outcome.Completed)
        {
            return;
        }

        _found = outcome.Value!;
        ShowFound();
    }

    private void Edit(PartyRow? row)
    {
        if (row is null)
        {
            return;
        }

        ClearMessages();
        Saved = null;
        Editing = row;
        IsFormOpen = true;
        FirstName = row.Listing.FirstName;
        LastName = row.Listing.LastName;
        Dni = row.Listing.Dni ?? string.Empty;
        Cuil = row.Listing.Cuil ?? string.Empty;
        Email = row.Listing.Email ?? string.Empty;
        PhoneNumber = row.Listing.PhoneNumber ?? string.Empty;
        Domicile = row.Listing.Domicile ?? string.Empty;
    }

    private void OpenNew()
    {
        ClearForm();
        Saved = null;
        IsFormOpen = true;
    }

    private void Back()
    {
        ClearForm();
        Saved = null;
        IsFormOpen = false;
    }

    private void Cancel()
    {
        Pending = null;
        NoIdentifierWarning = null;
    }

    private void ClearForm()
    {
        ClearMessages();
        Editing = null;
        FirstName = string.Empty;
        LastName = string.Empty;
        Dni = string.Empty;
        Cuil = string.Empty;
        Email = string.Empty;
        PhoneNumber = string.Empty;
        Domicile = string.Empty;
    }

    private void ClearMessages()
    {
        Pending = null;
        NoIdentifierWarning = null;
        RefusalMessage = null;
        OfferedHolder = null;
    }

    private void UseHolder()
    {
        var holder = OfferedHolder!;
        ClearForm();
        ChosenParty = new PartyRow(holder);

        // Back to the list on the holder (the designer's navigation), when the list has them.
        IsFormOpen = false;
        SelectedPerson = Persons.FirstOrDefault(p => p.Id == holder.Id);
    }

    private async Task SaveAsync()
    {
        ClearMessages();
        _identityConfirmed = false;

        if (Editing is not { } editing)
        {
            await SubmitAsync(acceptNoIdentifier: false);
            return;
        }

        // What the rules refuse is said now, rather than after asking to confirm something that cannot be written.
        if (PartyDraftRules.Check(Draft(), acceptNoIdentifier: true) is { } refusal)
        {
            RefusalMessage = refusal.OperatorText();
            return;
        }

        var changes = ChangesFrom(editing.Listing, Draft().Normalized());
        if (changes.Count == 0)
        {
            await SubmitAsync(acceptNoIdentifier: false);
            return;
        }

        Pending = new PendingPartyCorrection(
            $"¿Corregir los datos de {editing.FullName}? Los recibos ya emitidos conservan lo que imprimieron.", changes);
    }

    private async Task ConfirmAsync()
    {
        Pending = null;
        _identityConfirmed = true;
        await SubmitAsync(acceptNoIdentifier: false);
    }

    private async Task AcceptNoIdentifierAsync()
    {
        NoIdentifierWarning = null;
        await SubmitAsync(acceptNoIdentifier: true);
    }

    /// <summary>
    /// One write, then the list again, refused or not. A failure leaves the generic message on screen and the form
    /// as it was. Only a success clears the form, so a refused person is not typed twice.
    /// </summary>
    private async Task SubmitAsync(bool acceptNoIdentifier)
    {
        var draft = Draft();
        var correcting = Editing?.Id;

        var outcome = await Runner.RunAsync(
            () => correcting is { } id
                ? _parties.CorrectAsync(id, draft, acceptNoIdentifier)
                : _parties.CreateAsync(draft, acceptNoIdentifier));
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        await ReloadAsync();

        if (result.Succeeded)
        {
            ClearForm();
            Saved = correcting is null
                ? new PartySaved(PartiesTexts.CreatedTitle, PartiesTexts.Created(new PartyRow(result.Party!).ListName))
                : _identityConfirmed
                    ? new PartySaved(PartiesTexts.CorrectedTitle, PartiesTexts.Corrected)
                    : new PartySaved(PartiesTexts.ContactSavedTitle, PartiesTexts.ContactSaved);
            return;
        }

        if (result.Refusal == PartyRefusal.NoIdentifierNotAccepted)
        {
            NoIdentifierWarning = result.Refusal.Value.OperatorText();
            return;
        }

        RefusalMessage = result.Refusal!.Value.OperatorText(result.Holder?.FullName);

        // Using the holder instead only makes sense for someone new; a correction belongs to the person being corrected.
        OfferedHolder = correcting is null ? result.Holder : null;
    }

    private static List<PartyChange> ChangesFrom(PartyListing old, PartyDraft clean)
    {
        var compared = new (string Field, string? Old, string? New)[]
        {
            ("Nombre", old.FirstName, clean.FirstName),
            ("Apellido", old.LastName, clean.LastName),
            ("DNI", old.Dni, clean.Dni),
            ("CUIL", old.Cuil, clean.Cuil),
        };

        return compared
            .Where(c => c.Old != c.New)
            .Select(c => new PartyChange(c.Field, c.Old ?? AbsentValue, c.New ?? AbsentValue))
            .ToList();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            SearchCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
            AcceptNoIdentifierCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One value a correction changes.</summary>
public sealed record PartyChange(string Field, string Old, string New);

/// <summary>A correction the operator asked for and has not confirmed yet; <see cref="Text"/> names the person.</summary>
/// <summary>The parts of the Personas form, shown one at a time.</summary>
public enum PartyFormStep
{
    Fields,
    Confirm,
    NoIdentifier,
    Saved,
}

/// <summary>The notice a write that landed ends on: its title and its sentence.</summary>
public sealed record PartySaved(string Title, string Text);

/// <summary>The Personas screen's own texts, as the designer wrote them (tasks 12.3d). Refusals keep theirs in the domain.</summary>
public static class PartiesTexts
{
    public const string NewTitle = "Nueva persona";
    public const string NewSubtitle = "Complete los datos de identidad y de contacto.";
    public const string EditTitle = "Editar persona";
    public const string SaveNew = "Guardar persona";
    public const string SaveCorrection = "Guardar cambios";

    public const string NewIdentityHint =
        "Cada persona se carga una sola vez, aunque sea propietaria en un contrato e inquilina en otro. Si ya está cargada, búsquela en Personas en vez de crearla de nuevo.";

    public const string EditIdentityHint =
        "Puede corregir el nombre, el apellido, el DNI y el CUIL si se cargaron mal. El DNI y el CUIL no pueden coincidir con los de otra persona.";

    public const string CreatedTitle = "Persona guardada";
    public const string CorrectedTitle = "Corrección guardada";
    public const string Corrected = "Se actualizaron los datos de identidad. Los recibos ya emitidos conservan lo que imprimieron.";
    public const string ContactSavedTitle = "Cambios guardados";
    public const string ContactSaved = "Se actualizaron los datos de contacto.";

    public static string Created(string listName) => $"{listName} ya está en la lista de personas.";
}

public sealed record PendingPartyCorrection(string Text, IReadOnlyList<PartyChange> Changes);

/// <summary>One person on the Personas screen.</summary>
public sealed class PartyRow
{
    public PartyRow(PartyListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Listing = listing;
    }

    public PartyListing Listing { get; }

    public Guid Id => Listing.Id;

    public string FullName => Listing.FullName;

    public string? Dni => Listing.Dni;

    public string? Cuil => Listing.Cuil;

    /// <summary>"Last, first", as the list's "Apellido y nombre" column reads.</summary>
    public string ListName => $"{Listing.LastName}, {Listing.FirstName}";

    public string? Email => Listing.Email;

    public string? PhoneNumber => Listing.PhoneNumber;

    /// <summary>The roles on contracts in force (spec Requirement 1), empty for someone on none.</summary>
    public string RolesText => string.Join(", ", RoleNames);

    /// <summary>Each role on its own, for the list's labels.</summary>
    public IReadOnlyList<string> RoleNames => Listing.ActiveRoles.Select(Formats.Role).ToList();

    /// <summary>Shown beside a person with neither identifier (spec Decision 28).</summary>
    public string? IdentifierWarning => Listing.HasNoIdentifier ? "Sin DNI ni CUIL: no se pueden detectar duplicados." : null;
}
