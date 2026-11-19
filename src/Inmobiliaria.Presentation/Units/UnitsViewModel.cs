using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Units;

/// <summary>
/// The Propiedades screen (gestion-de-alquileres spec Requirements 4, 5 and 28, design §6): the list with availability,
/// filtered by type and searched by address, and a form to create a unit or correct its address, with the warning for an
/// identical address, which the operator can accept. The list is read again after every write, refused or not. Both roles.
/// As the designer drew it (canvas "Propiedades", "Nueva propiedad", "Editar propiedad"), the form is a screen of its own,
/// opened from "Nueva propiedad" or "Editar", and a write that lands ends on a saved notice with the way back.
/// </summary>
public sealed partial class UnitsViewModel : ObservableObject
{
    private readonly IUnitRegistry _units;

    /// <summary>What the last read brought. The type filter and the search work on this, asking the database nothing.</summary>
    private IReadOnlyList<UnitListing> _found = [];

    public UnitsViewModel(IUnitRegistry units)
    {
        ArgumentNullException.ThrowIfNull(units);

        _units = units;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        AcceptIdenticalAddressCommand = new AsyncRelayCommand(AcceptIdenticalAddressAsync, () => CanAct() && IdenticalAddressWarning is not null);
        EditCommand = new RelayCommand<UnitRow>(Edit, row => row is not null);
        NewCommand = new RelayCommand(OpenNew);
        BackCommand = new RelayCommand(Back);
        CancelCommand = new RelayCommand(Cancel);
        ClearFormCommand = new RelayCommand(ClearForm);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<UnitRow> Units { get; } = [];

    /// <summary>The provinces the form offers, the only ones it can save (spec Decision 49).</summary>
    public IReadOnlyList<string> Provinces => Domain.Units.Provinces.All;

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand AcceptIdenticalAddressCommand { get; }

    public IRelayCommand<UnitRow> EditCommand { get; }

    /// <summary>"Nueva propiedad": an empty form.</summary>
    public IRelayCommand NewCommand { get; }

    /// <summary>"Volver a propiedades" and "Cancelar": back to the list, with the form cleared.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>"Volver a revisar": the warning goes, the form stays as typed.</summary>
    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearFormCommand { get; }

    /// <summary>The type is chosen once: correcting a unit never changes it (spec Requirement 5).</summary>
    public bool CanChooseType => Editing is null;

    /// <summary>The locked type's label while correcting.</summary>
    public string TypeText => UnitRow.TypeName(SelectedType);

    public string FormTitle => Editing is null && _correctedAddress is null ? UnitsTexts.NewTitle : UnitsTexts.EditTitle;

    public string FormSubtitle => Editing?.AddressText ?? _correctedAddress ?? UnitsTexts.NewSubtitle;

    public string SaveText => Editing is null ? UnitsTexts.SaveNew : UnitsTexts.SaveCorrection;

    /// <summary>Which part of the form is shown.</summary>
    public UnitFormStep Step =>
        Saved is not null ? UnitFormStep.Saved
        : IdenticalAddressWarning is not null ? UnitFormStep.Warning
        : UnitFormStep.Fields;

    /// <summary>The foot of the list: how many units it shows.</summary>
    public string CountText => Units.Count == 1 ? "1 unidad" : $"{Units.Count} unidades";

    /// <summary>True when the filter and the search leave nothing to show.</summary>
    public bool IsEmpty => Units.Count == 0;

    /// <summary>True while the form, or its saved notice, is shown in place of the list.</summary>
    [ObservableProperty]
    private bool _isFormOpen;

    /// <summary>The unit selected in the list, which "Editar" opens.</summary>
    [ObservableProperty]
    private UnitRow? _selectedUnit;

    /// <summary>The type to list, or null for every unit.</summary>
    [ObservableProperty]
    private UnitType? _typeFilter;

    /// <summary>Part of the address to look for, matched as typed, without regard to case or accents.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The type a new unit gets. Ignored while correcting one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private UnitType _selectedType = UnitType.Property;

    [ObservableProperty]
    private string _street = string.Empty;

    [ObservableProperty]
    private string _number = string.Empty;

    [ObservableProperty]
    private string _floor = string.Empty;

    [ObservableProperty]
    private string _apartment = string.Empty;

    [ObservableProperty]
    private string _city = UnitsTexts.DefaultCity;

    [ObservableProperty]
    private string _province = Domain.Units.Provinces.Default;

    [ObservableProperty]
    private string _postalCode = UnitsTexts.DefaultPostalCode;

    /// <summary>The unit the form is correcting, or null while it creates one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseType), nameof(FormTitle), nameof(FormSubtitle), nameof(SaveText))]
    private UnitRow? _editing;

    /// <summary>The warning naming the unit already at this address, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptIdenticalAddressCommand))]
    [NotifyPropertyChangedFor(nameof(Step))]
    private string? _identicalAddressWarning;

    /// <summary>Each field's error from the last save, all at once (spec Decision 44). Cleared when the next starts.</summary>
    [ObservableProperty]
    private UnitFieldErrors _errors = UnitFieldErrors.None;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>What the last write that landed saved, shown in place of the form until the operator goes back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step), nameof(FormTitle), nameof(FormSubtitle))]
    private UnitSaved? _saved;

    /// <summary>
    /// The address a correction saved, so its notice stays under "Editar propiedad" once the form is cleared; it lives as
    /// long as that notice.
    /// </summary>
    private string? _correctedAddress;

    partial void OnSavedChanged(UnitSaved? value)
    {
        if (value is null)
        {
            _correctedAddress = null;
        }
    }

    partial void OnTypeFilterChanged(UnitType? value) => ShowFound();

    partial void OnSearchTextChanged(string value) => ShowFound();

    partial void OnSelectedTypeChanged(UnitType value) => WithdrawWarning();

    partial void OnStreetChanged(string value) => WithdrawWarning();

    partial void OnNumberChanged(string value) => WithdrawWarning();

    partial void OnFloorChanged(string value) => WithdrawWarning();

    partial void OnApartmentChanged(string value) => WithdrawWarning();

    partial void OnCityChanged(string value) => WithdrawWarning();

    partial void OnProvinceChanged(string value) => WithdrawWarning();

    partial void OnPostalCodeChanged(string value) => WithdrawWarning();

    /// <summary>Accepting the warning covers only the address it named, so editing any field means saving again.</summary>
    private void WithdrawWarning() => IdenticalAddressWarning = null;

    private bool CanAct() => !Runner.IsBusy;

    private UnitDraft Draft() => new(Street, Number, City, Province, PostalCode, Floor, Apartment);

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(() => _units.ListAsync());
        if (!outcome.Completed)
        {
            return;
        }

        _found = outcome.Value!;
        ShowFound();
    }

    private void ShowFound()
    {
        var search = SearchText.Trim();
        var compare = CultureInfo.InvariantCulture.CompareInfo;

        Units.Clear();
        foreach (var listing in _found)
        {
            var row = new UnitRow(listing);
            if ((TypeFilter is not { } type || listing.Type == type)
                && (search.Length == 0
                    || compare.IndexOf(row.AddressText, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            {
                Units.Add(row);
            }
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Edit(UnitRow? row)
    {
        if (row is null)
        {
            return;
        }

        ClearMessages();
        Saved = null;
        Editing = row;
        IsFormOpen = true;
        SelectedType = row.Listing.Type;
        var address = row.Listing.Address;
        Street = address.Street;
        Number = address.Number;
        Floor = address.Floor ?? string.Empty;
        Apartment = address.Apartment ?? string.Empty;
        City = address.City;
        Province = address.Province;
        PostalCode = address.PostalCode;
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
        IdenticalAddressWarning = null;
    }

    private void ClearForm()
    {
        ClearMessages();
        Editing = null;
        SelectedType = UnitType.Property;
        Street = string.Empty;
        Number = string.Empty;
        Floor = string.Empty;
        Apartment = string.Empty;
        City = UnitsTexts.DefaultCity;
        Province = Domain.Units.Provinces.Default;
        PostalCode = UnitsTexts.DefaultPostalCode;
    }

    private void ClearMessages()
    {
        IdenticalAddressWarning = null;
        Errors = UnitFieldErrors.None;
        RefusalMessage = null;
    }

    /// <summary>The form's own problems first, all at once and nothing sent; then the port, whose refusals need the database.</summary>
    private async Task SaveAsync()
    {
        ClearMessages();

        var problems = UnitDraftRules.Problems(Draft());
        if (problems.Count > 0)
        {
            Errors = new UnitFieldErrors(problems, Draft());
            return;
        }

        await SubmitAsync(acceptIdenticalAddress: false);
    }

    private async Task AcceptIdenticalAddressAsync()
    {
        IdenticalAddressWarning = null;
        await SubmitAsync(acceptIdenticalAddress: true);
    }

    /// <summary>
    /// One write, then the list again, refused or not. A failure leaves the generic message on screen and the form
    /// as it was. Only a success clears the form, so a refused unit is not typed twice.
    /// </summary>
    private async Task SubmitAsync(bool acceptIdenticalAddress)
    {
        var draft = Draft();
        var type = SelectedType;
        var correcting = Editing?.Id;

        var outcome = await Runner.RunAsync(
            () => correcting is { } id
                ? _units.RelocateAsync(id, draft, acceptIdenticalAddress)
                : _units.CreateAsync(type, draft, acceptIdenticalAddress));
        if (!outcome.Completed)
        {
            return;
        }

        var result = outcome.Value!;
        await ReloadAsync();

        if (result.Succeeded)
        {
            ClearForm();
            var address = result.Unit!.AddressText;
            _correctedAddress = correcting is null ? null : address;
            Saved = correcting is null
                ? new UnitSaved(UnitsTexts.CreatedTitle, UnitsTexts.Created(address))
                : new UnitSaved(UnitsTexts.CorrectedTitle, UnitsTexts.Corrected(address));
            return;
        }

        if (result.Refusal == UnitRefusal.IdenticalAddressNotAccepted)
        {
            IdenticalAddressWarning = result.Refusal.Value.OperatorText(result.Existing?.AddressText);
            return;
        }

        RefusalMessage = result.Refusal!.Value.OperatorText();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            AcceptIdenticalAddressCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>The parts of the Propiedades form, shown one at a time.</summary>
public enum UnitFormStep
{
    Fields,
    Warning,
    Saved,
}

/// <summary>Each field's error text, or null for a field without one; the screen shows each beside its field.</summary>
public sealed class UnitFieldErrors
{
    public const string SummaryText = "No se pudo guardar: revise los datos marcados.";

    public static readonly UnitFieldErrors None = new([], new UnitDraft(null, null, null, null, null));

    private readonly Dictionary<UnitField, string> _texts;

    /// <param name="problems">What is wrong.</param>
    /// <param name="draft">The draft they were found in, which some texts name.</param>
    public UnitFieldErrors(IEnumerable<UnitProblem> problems, UnitDraft draft)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(draft);
        _texts = problems.ToDictionary(p => p.Field, p => p.OperatorText(draft));
    }

    public string? this[UnitField field] => _texts.GetValueOrDefault(field);

    public int Count => _texts.Count;

    /// <summary>The line over the form while any field has an error.</summary>
    public string? Summary => _texts.Count > 0 ? SummaryText : null;
}

/// <summary>The notice a write that landed ends on: its title and its sentence.</summary>
public sealed record UnitSaved(string Title, string Text);

/// <summary>The Propiedades screen's own texts, as the designer wrote them. Refusals keep theirs in the domain.</summary>
public static class UnitsTexts
{
    public const string NewTitle = "Nueva propiedad";
    public const string NewSubtitle = "Cargue la dirección de la propiedad o de la cochera.";
    public const string EditTitle = "Editar propiedad";
    public const string SaveNew = "Guardar propiedad";
    public const string SaveCorrection = "Guardar cambios";

    /// <summary>The agency's own city and postal code, which the designer's form opens on.</summary>
    public const string DefaultCity = "Sunchales";

    public const string DefaultPostalCode = "2322";

    public const string CreatedTitle = "Propiedad guardada";
    public const string CorrectedTitle = "Cambios guardados";

    public static string Created(string address) => $"{address} quedó cargada.";

    public static string Corrected(string address) => $"{address} quedó actualizada.";
}

/// <summary>One unit on the Propiedades screen.</summary>
public sealed class UnitRow
{
    /// <summary>What the list shows for a floor or apartment the unit does not have.</summary>
    public const string AbsentValue = "—";

    public UnitRow(UnitListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Listing = listing;
    }

    public UnitListing Listing { get; }

    public Guid Id => Listing.Id;

    public string AddressText => Listing.AddressText;

    /// <summary>Street and number, the list's "Dirección" column.</summary>
    public string StreetText => $"{Listing.Address.Street} {Listing.Address.Number}";

    public string FloorText => Listing.Address.Floor ?? AbsentValue;

    public string ApartmentText => Listing.Address.Apartment ?? AbsentValue;

    public string City => Listing.Address.City;

    public string Province => Listing.Address.Province;

    public string PostalCode => Listing.Address.PostalCode;

    public bool IsAvailable => Listing.IsAvailable;

    public bool IsParking => Listing.Type == UnitType.ParkingSpace;

    public string TypeText => TypeName(Listing.Type);

    /// <summary>Available, or who rents it and, once notice was given, when they move out.</summary>
    public string AvailabilityText
    {
        get
        {
            if (Listing.Covering is not { } covering)
            {
                return "Disponible";
            }

            var tenant = covering.TenantName is { } name ? $"Alquilada a {name}" : "Alquilada";

            return covering is { Status: ContractStatus.PendingTermination, MoveOutDate: { } moveOut }
                ? $"{tenant}, se muda el {moveOut.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
                : tenant;
        }
    }

    public static string TypeName(UnitType type) => type == UnitType.ParkingSpace ? "Cochera" : "Propiedad";
}
