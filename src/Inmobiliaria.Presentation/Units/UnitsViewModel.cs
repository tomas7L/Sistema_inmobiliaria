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
/// The Propiedades screen (gestion-de-alquileres spec Requirements 4 and 5, design §6): the list with availability, a form
/// to create a unit or correct its address, and the warning for an identical address, which the operator can accept. The
/// list is read again after every action, refused or not. Both roles; the view arrives in slice S12.
/// </summary>
public sealed partial class UnitsViewModel : ObservableObject
{
    private readonly IUnitRegistry _units;

    public UnitsViewModel(IUnitRegistry units)
    {
        ArgumentNullException.ThrowIfNull(units);

        _units = units;
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanAct);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanAct);
        AcceptIdenticalAddressCommand = new AsyncRelayCommand(AcceptIdenticalAddressAsync, () => CanAct() && IdenticalAddressWarning is not null);
        EditCommand = new RelayCommand<UnitRow>(Edit);
        CancelCommand = new RelayCommand(Cancel);
        ClearFormCommand = new RelayCommand(ClearForm);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<UnitRow> Units { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand AcceptIdenticalAddressCommand { get; }

    public IRelayCommand<UnitRow> EditCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearFormCommand { get; }

    /// <summary>The type is chosen once: correcting a unit never changes it (spec Requirement 5).</summary>
    public bool CanChooseType => Editing is null;

    /// <summary>The type a new unit gets. Ignored while correcting one.</summary>
    [ObservableProperty]
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
    private string _city = string.Empty;

    [ObservableProperty]
    private string _province = string.Empty;

    [ObservableProperty]
    private string _postalCode = string.Empty;

    /// <summary>The unit the form is correcting, or null while it creates one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseType))]
    private UnitRow? _editing;

    /// <summary>The warning naming the unit already at this address, waiting to be accepted, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptIdenticalAddressCommand))]
    private string? _identicalAddressWarning;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

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

        Units.Clear();
        foreach (var listing in outcome.Value!)
        {
            Units.Add(new UnitRow(listing));
        }
    }

    private void Edit(UnitRow? row)
    {
        if (row is null)
        {
            return;
        }

        ClearMessages();
        Editing = row;
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
        City = string.Empty;
        Province = string.Empty;
        PostalCode = string.Empty;
    }

    private void ClearMessages()
    {
        IdenticalAddressWarning = null;
        RefusalMessage = null;
    }

    private async Task SaveAsync()
    {
        ClearMessages();
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

/// <summary>One unit on the Propiedades screen.</summary>
public sealed class UnitRow
{
    public UnitRow(UnitListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Listing = listing;
    }

    public UnitListing Listing { get; }

    public Guid Id => Listing.Id;

    public string AddressText => Listing.AddressText;

    public bool IsAvailable => Listing.IsAvailable;

    public string TypeText => Listing.Type == UnitType.ParkingSpace ? "Cochera" : "Propiedad";

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
}
