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
/// every action, refused or not. Both roles; the view arrives in slice S12.
/// </summary>
public sealed partial class PartiesViewModel : ObservableObject
{
    /// <summary>What a correction shows for a value that is, or becomes, empty.</summary>
    public const string AbsentValue = "(sin dato)";

    private readonly IPartyRegistry _parties;

    public PartiesViewModel(IPartyRegistry parties)
    {
        ArgumentNullException.ThrowIfNull(parties);

        _parties = parties;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SearchCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && Pending is not null);
        AcceptNoIdentifierCommand = new AsyncRelayCommand(AcceptNoIdentifierAsync, () => CanAct() && NoIdentifierWarning is not null);
        EditCommand = new RelayCommand<PartyRow>(Edit);
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

    [ObservableProperty]
    private string _searchText = string.Empty;

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
    private PartyRow? _editing;

    /// <summary>The correction waiting for confirmation, with old and new values, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private PendingPartyCorrection? _pending;

    /// <summary>The warning that duplicates of this person cannot be detected, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptNoIdentifierCommand))]
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

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(() => _parties.SearchAsync(SearchText));
        if (!outcome.Completed)
        {
            return;
        }

        Persons.Clear();
        foreach (var listing in outcome.Value!)
        {
            Persons.Add(new PartyRow(listing));
        }
    }

    private void Edit(PartyRow? row)
    {
        if (row is null)
        {
            return;
        }

        ClearMessages();
        Editing = row;
        FirstName = row.Listing.FirstName;
        LastName = row.Listing.LastName;
        Dni = row.Listing.Dni ?? string.Empty;
        Cuil = row.Listing.Cuil ?? string.Empty;
        Email = row.Listing.Email ?? string.Empty;
        PhoneNumber = row.Listing.PhoneNumber ?? string.Empty;
        Domicile = row.Listing.Domicile ?? string.Empty;
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
    }

    private async Task SaveAsync()
    {
        ClearMessages();

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

    /// <summary>Shown beside a person with neither identifier (spec Decision 28).</summary>
    public string? IdentifierWarning => Listing.HasNoIdentifier ? "Sin DNI ni CUIL: no se pueden detectar duplicados." : null;
}
