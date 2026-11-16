using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Tests.Fakes;
using Inmobiliaria.Presentation.Units;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirements 4 and 5 and Tests 8 and 9 on the screen's logic, with the port faked.</summary>
public class UnitsViewModelTests
{
    private static readonly Guid AlemId = Guid.NewGuid();
    private static readonly Guid CocheraId = Guid.NewGuid();

    private static readonly UnitListing Alem = new(
        AlemId,
        UnitType.Property,
        new Address("Alem", "1258", "Sunchales", "Santa Fe", "2322", "2", "B"),
        null);

    private static readonly UnitListing Cochera = new(
        CocheraId,
        UnitType.ParkingSpace,
        new Address("Belgrano", "40", "Sunchales", "Santa Fe", "2322"),
        new UnitCoverage(ContractStatus.Active, "Juan Pérez", null));

    private static FakeUnitRegistry Port() => new() { Units = [Alem, Cochera] };

    private static async Task<UnitsViewModel> LoadedAsync(FakeUnitRegistry port)
    {
        var viewModel = new UnitsViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static UnitRow Row(UnitsViewModel viewModel, Guid id) => viewModel.Units.Single(u => u.Id == id);

    private static void Fill(UnitsViewModel viewModel)
    {
        viewModel.Street = "Alem";
        viewModel.Number = "1258";
        viewModel.Floor = "2";
        viewModel.Apartment = "B";
        viewModel.City = "Sunchales";
        viewModel.Province = "Santa Fe";
        viewModel.PostalCode = "2322";
    }

    /// <summary>Requirement 4: address, type and availability, in the port's order.</summary>
    [Fact]
    public async Task TheList_ShowsAddressTypeAndAvailability()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.Equal([AlemId, CocheraId], viewModel.Units.Select(u => u.Id).ToList());
        Assert.Equal("Alem 1258, piso 2, depto B, Sunchales", Row(viewModel, AlemId).AddressText);
        Assert.Equal("Propiedad", Row(viewModel, AlemId).TypeText);
        Assert.Equal("Disponible", Row(viewModel, AlemId).AvailabilityText);
        Assert.True(Row(viewModel, AlemId).IsAvailable);
        Assert.Equal("Belgrano 40, Sunchales", Row(viewModel, CocheraId).AddressText);
        Assert.Equal("Cochera", Row(viewModel, CocheraId).TypeText);
        Assert.Equal("Alquilada a Juan Pérez", Row(viewModel, CocheraId).AvailabilityText);
        Assert.False(Row(viewModel, CocheraId).IsAvailable);
    }

    /// <summary>Requirement 4: a contract waiting for its move-out says when, and one without a tenant still shows as rented.</summary>
    [Theory]
    [InlineData("Juan Pérez", "Alquilada a Juan Pérez, se muda el 31/10/2026")]
    [InlineData(null, "Alquilada, se muda el 31/10/2026")]
    public void AUnitWaitingForAMoveOut_SaysWhen(string? tenant, string expected)
    {
        var row = new UnitRow(Alem with
        {
            Covering = new UnitCoverage(ContractStatus.PendingTermination, tenant, new DateOnly(2026, 10, 31)),
        });

        Assert.Equal(expected, row.AvailabilityText);
    }

    /// <summary>Requirement 5: the chosen type and the typed address reach the port; success clears the form and reads the list again.</summary>
    [Fact]
    public async Task Creating_SendsTheTypeAndAddress_ClearsTheForm_AndReadsTheListAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        var lists = port.Lists;
        Fill(viewModel);
        viewModel.SelectedType = UnitType.ParkingSpace;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(port.Created);
        Assert.Equal(UnitType.ParkingSpace, created.Type);
        Assert.Equal(new UnitDraft("Alem", "1258", "Sunchales", "Santa Fe", "2322", "2", "B"), created.Draft);
        Assert.False(created.Accepted);
        Assert.Equal(string.Empty, viewModel.Street);
        Assert.Equal(UnitsTexts.DefaultPostalCode, viewModel.PostalCode);
        Assert.Equal(UnitType.Property, viewModel.SelectedType);
        Assert.Equal(lists + 1, port.Lists);
    }

    /// <summary>Spec Test 9: an identical address warns naming the existing unit, and the operator can proceed.</summary>
    [Fact]
    public async Task AnIdenticalAddress_WarnsNamingTheUnit_AndCanProceed()
    {
        var port = Port();
        port.OnCreate = (type, draft, accepted) => accepted
            ? UnitResult.Saved(Alem)
            : UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Alem 1258, piso 2, depto B, Sunchales", viewModel.IdenticalAddressWarning);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("Alem", viewModel.Street);

        await viewModel.AcceptIdenticalAddressCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Created.Select(c => c.Accepted).ToList());
        Assert.Null(viewModel.IdenticalAddressWarning);
        Assert.Equal(string.Empty, viewModel.Street);
    }

    /// <summary>Spec Test 9: declining the warning writes nothing and keeps the form.</summary>
    [Fact]
    public async Task DecliningTheWarning_WritesNothing()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.Null(viewModel.IdenticalAddressWarning);
        Assert.Single(port.Created);
        Assert.Equal("Alem", viewModel.Street);
    }

    /// <summary>What is accepted is what is written, so editing any field withdraws the warning.</summary>
    [Theory]
    [InlineData(nameof(UnitsViewModel.Street))]
    [InlineData(nameof(UnitsViewModel.Number))]
    [InlineData(nameof(UnitsViewModel.Floor))]
    [InlineData(nameof(UnitsViewModel.Apartment))]
    [InlineData(nameof(UnitsViewModel.City))]
    [InlineData(nameof(UnitsViewModel.Province))]
    [InlineData(nameof(UnitsViewModel.PostalCode))]
    public async Task EditingAFieldAfterTheWarning_WithdrawsIt(string field)
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.IdenticalAddressWarning);

        typeof(UnitsViewModel).GetProperty(field)!.SetValue(viewModel, "Otro");

        Assert.Null(viewModel.IdenticalAddressWarning);
        Assert.False(viewModel.AcceptIdenticalAddressCommand.CanExecute(null));
        Assert.Single(port.Created);
    }

    /// <summary>Choosing another type also withdraws it: the warning was about a unit of the type then chosen.</summary>
    [Fact]
    public async Task ChoosingAnotherTypeAfterTheWarning_WithdrawsIt()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.SelectedType = UnitType.ParkingSpace;

        Assert.Null(viewModel.IdenticalAddressWarning);
    }

    /// <summary>Spec Test 9: correcting fills the form with the unit, locks its type, and writes the address only.</summary>
    [Fact]
    public async Task Correcting_LocksTheType_AndRelocatesTheUnit()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Assert.True(viewModel.CanChooseType);

        viewModel.EditCommand.Execute(Row(viewModel, CocheraId));

        Assert.False(viewModel.CanChooseType);
        Assert.Equal(UnitType.ParkingSpace, viewModel.SelectedType);
        Assert.Equal("Belgrano", viewModel.Street);
        Assert.Equal(string.Empty, viewModel.Floor);

        viewModel.Number = "42";
        await viewModel.SaveCommand.ExecuteAsync(null);

        var written = Assert.Single(port.Relocated);
        Assert.Equal(CocheraId, written.Id);
        Assert.Equal("42", written.Draft.Number);
        Assert.Empty(port.Created);
        Assert.Null(viewModel.Editing);
        Assert.True(viewModel.CanChooseType);
    }

    /// <summary>A correction to an address another unit has warns too, and accepting relocates.</summary>
    [Fact]
    public async Task CorrectingToAnIdenticalAddress_Warns_AndRelocatesAfterAccepting()
    {
        var port = Port();
        port.OnRelocate = (id, _, accepted) => accepted
            ? UnitResult.Saved(Cochera)
            : UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, CocheraId));
        Fill(viewModel);

        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.AcceptIdenticalAddressCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Relocated.Select(r => r.Accepted).ToList());
        Assert.Null(viewModel.Editing);
    }

    /// <summary>
    /// Requirement 27, Decision 44: every problem of the form at once, each beside its field under one summary, the form
    /// kept as typed and the port never called.
    /// </summary>
    [Fact]
    public async Task AFormWithSeveralMistakes_ShowsThemAllAtOnce_AndCallsNoPort()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.Street = "Alem";
        viewModel.Number = string.Empty;
        viewModel.City = new string('a', 61);
        viewModel.PostalCode = string.Empty;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.Errors.Count);
        Assert.Equal("Escriba el número. Si no tiene, escriba S/N.", viewModel.Errors[UnitField.Number]);
        Assert.Equal("La localidad admite hasta 60 caracteres.", viewModel.Errors[UnitField.City]);
        Assert.Equal("Escriba el código postal.", viewModel.Errors[UnitField.PostalCode]);
        Assert.Null(viewModel.Errors[UnitField.Street]);
        Assert.Equal(UnitFieldErrors.SummaryText, viewModel.Errors.Summary);
        Assert.Empty(port.Created);
        Assert.Equal("Alem", viewModel.Street);
    }

    /// <summary>Saving again after fixing them clears every error.</summary>
    [Fact]
    public async Task FixingTheMistakes_ClearsTheErrors()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotEqual(0, viewModel.Errors.Count);
        Fill(viewModel);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.Errors.Count);
        Assert.Null(viewModel.Errors.Summary);
        Assert.Single(port.Created);
    }

    /// <summary>A refusal only the port can give shows its own text and keeps the form.</summary>
    [Fact]
    public async Task ARefusalFromThePort_ShowsItsOwnText_AndKeepsTheForm()
    {
        var port = Port();
        port.OnRelocate = (_, _, _) => UnitResult.Refused(UnitRefusal.UnitNotFound);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, AlemId));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(UnitRefusal.UnitNotFound.OperatorText(), viewModel.RefusalMessage);
        Assert.Null(viewModel.IdenticalAddressWarning);
        Assert.Equal("Alem", viewModel.Street);
        Assert.Null(viewModel.Saved);
    }

    /// <summary>The next action clears the previous refusal.</summary>
    [Fact]
    public async Task TheNextAction_ClearsThePreviousRefusal()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Refused(UnitRefusal.UnitNotFound);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.RefusalMessage);
        port.OnCreate = null;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.RefusalMessage);
    }

    /// <summary>Clearing the form leaves correcting and goes back to creating a property.</summary>
    [Fact]
    public async Task ClearingTheForm_GoesBackToCreating()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.EditCommand.Execute(Row(viewModel, CocheraId));

        viewModel.ClearFormCommand.Execute(null);

        Assert.Null(viewModel.Editing);
        Assert.True(viewModel.CanChooseType);
        Assert.Equal(UnitType.Property, viewModel.SelectedType);
        Assert.Equal(string.Empty, viewModel.Street);
        Assert.Equal(Provinces.Default, viewModel.Province);
    }

    /// <summary>A write that fails shows the generic text only, keeps what was typed and the commands, and reads nothing.</summary>
    [Fact]
    public async Task AWriteThatFails_ShowsTheGenericMessage_AndKeepsTheForm()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => throw new InvalidOperationException("Host=secret");
        var viewModel = await LoadedAsync(port);
        var lists = port.Lists;
        Fill(viewModel);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("Alem", viewModel.Street);
        Assert.Equal(lists, port.Lists);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    /// <summary>A write that landed stands even if the list cannot be read after it: the form clears, so it is not sent twice.</summary>
    [Fact]
    public async Task ASavedUnit_ClearsTheForm_EvenIfTheListCannotBeReadAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        port.ListFailure = new InvalidOperationException("Host=secret");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(port.Created);
        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.Street);
    }

    /// <summary>Requirement 28: the form opens on Santa Fe, as the designer drew it with the agency's city and postal code, and offers the 24 jurisdictions only.</summary>
    [Fact]
    public async Task TheForm_OpensOnSantaFe_AndOffersOnlyTheJurisdictions()
    {
        var viewModel = await LoadedAsync(Port());

        viewModel.NewCommand.Execute(null);

        Assert.True(viewModel.IsFormOpen);
        Assert.Equal("Santa Fe", viewModel.Province);
        Assert.Equal("Sunchales", viewModel.City);
        Assert.Equal("2322", viewModel.PostalCode);
        Assert.Equal(Provinces.All, viewModel.Provinces);
        Assert.Equal(UnitsTexts.NewTitle, viewModel.FormTitle);
        Assert.Equal(UnitsTexts.SaveNew, viewModel.SaveText);
    }

    /// <summary>Test 112: "PB", "Local 3" and "S/N" reach the port as typed.</summary>
    [Fact]
    public async Task FloorApartmentAndNumber_ReachThePort_AsTyped()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.NewCommand.Execute(null);
        viewModel.Street = "Alem";
        viewModel.Number = "S/N";
        viewModel.Floor = "PB";
        viewModel.Apartment = "Local 3";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(port.Created);
        Assert.Equal(new UnitDraft("Alem", "S/N", "Sunchales", "Santa Fe", "2322", "PB", "Local 3"), created.Draft);
    }

    /// <summary>A province the list does not hold, as one stored before the list, is refused beside the field.</summary>
    [Fact]
    public async Task AProvinceOffTheList_IsRefusedBesideItsField()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        viewModel.Province = "Sta Fe";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Elija una provincia de la lista.", viewModel.Errors[UnitField.Province]);
        Assert.Empty(port.Created);
    }

    /// <summary>Test 119: a malformed postal code and a CPA of another province are refused beside the field, each in its words.</summary>
    [Theory]
    [InlineData("23", "Escriba el código postal como 2322 o como S2322ABC.")]
    [InlineData("x5000abc", "Un código postal de Santa Fe empieza con S, no con X.")]
    public async Task APostalCodeTheRulesRefuse_IsRefusedBesideItsField(string postalCode, string expected)
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        viewModel.PostalCode = postalCode;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected, viewModel.Errors[UnitField.PostalCode]);
        Assert.Empty(port.Created);
    }

    /// <summary>A CPA typed in lower case reaches the port in capitals.</summary>
    [Fact]
    public async Task ACpaInLowerCase_ReachesThePortInCapitals()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        viewModel.PostalCode = "s2322abc";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("S2322ABC", Assert.Single(port.Created).Draft.Normalized().PostalCode);
    }

    /// <summary>The canvas: a write that lands ends on the saved notice naming the address, and the way back closes the form.</summary>
    [Fact]
    public async Task ASavedUnit_EndsOnTheNotice_AndGoesBackToTheList()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Saved(Alem);
        var viewModel = await LoadedAsync(port);
        viewModel.NewCommand.Execute(null);
        Fill(viewModel);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(UnitFormStep.Saved, viewModel.Step);
        Assert.Equal(new UnitSaved("Propiedad guardada", "Alem 1258, piso 2, depto B, Sunchales quedó cargada."), viewModel.Saved);

        viewModel.BackCommand.Execute(null);

        Assert.False(viewModel.IsFormOpen);
        Assert.Null(viewModel.Saved);
        Assert.Equal(UnitFormStep.Fields, viewModel.Step);
    }

    /// <summary>A correction ends on its own notice; the form's title and subtitle name the unit while it is open.</summary>
    [Fact]
    public async Task ACorrection_IsTitledByTheUnit_AndEndsOnItsOwnNotice()
    {
        var port = Port();
        port.OnRelocate = (_, _, _) => UnitResult.Saved(Alem);
        var viewModel = await LoadedAsync(port);
        viewModel.SelectedUnit = Row(viewModel, AlemId);

        viewModel.EditCommand.Execute(viewModel.SelectedUnit);

        Assert.True(viewModel.IsFormOpen);
        Assert.Equal(UnitsTexts.EditTitle, viewModel.FormTitle);
        Assert.Equal("Alem 1258, piso 2, depto B, Sunchales", viewModel.FormSubtitle);
        Assert.Equal(UnitsTexts.SaveCorrection, viewModel.SaveText);
        Assert.Equal("Propiedad", viewModel.TypeText);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new UnitSaved("Cambios guardados", "Alem 1258, piso 2, depto B, Sunchales quedó actualizada."), viewModel.Saved);
    }

    /// <summary>"Editar" needs a unit chosen in the list.</summary>
    [Fact]
    public async Task Editing_NeedsAUnitChosen()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.False(viewModel.EditCommand.CanExecute(null));
        Assert.True(viewModel.EditCommand.CanExecute(Row(viewModel, AlemId)));
    }

    /// <summary>The warning is a step of its own; "Volver a revisar" goes back to the fields as typed.</summary>
    [Fact]
    public async Task TheWarning_IsItsOwnStep()
    {
        var port = Port();
        port.OnCreate = (_, _, _) => UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, Alem);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(UnitFormStep.Warning, viewModel.Step);

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(UnitFormStep.Fields, viewModel.Step);
        Assert.Equal("Alem", viewModel.Street);
    }

    /// <summary>The canvas's type filter and search work on what was read, asking the database nothing.</summary>
    [Fact]
    public async Task TheTypeFilterAndTheSearch_NarrowTheList_WithoutReadingAgain()
    {
        var port = Port();
        port.Units.Add(new UnitListing(Guid.NewGuid(), UnitType.Property, new Address("Pellegrini", "455", "Sunchales", "Santa Fe", "2322"), null));
        var viewModel = await LoadedAsync(port);
        var lists = port.Lists;
        Assert.Equal("3 unidades", viewModel.CountText);

        viewModel.TypeFilter = UnitType.ParkingSpace;

        Assert.Equal([CocheraId], viewModel.Units.Select(u => u.Id).ToList());
        Assert.Equal("1 unidad", viewModel.CountText);

        viewModel.TypeFilter = null;
        viewModel.SearchText = "ALÉM";

        Assert.Equal([AlemId], viewModel.Units.Select(u => u.Id).ToList());

        viewModel.SearchText = "Mitre";

        Assert.True(viewModel.IsEmpty);
        Assert.Equal(lists, port.Lists);
    }

    /// <summary>The list's columns, as the canvas draws them; a missing floor or apartment shows a dash.</summary>
    [Fact]
    public async Task TheRow_SplitsTheAddressIntoTheCanvasColumns()
    {
        var viewModel = await LoadedAsync(Port());

        var alem = Row(viewModel, AlemId);
        Assert.Equal("Alem 1258", alem.StreetText);
        Assert.Equal("2", alem.FloorText);
        Assert.Equal("B", alem.ApartmentText);
        Assert.Equal("Sunchales", alem.City);
        Assert.Equal("Santa Fe", alem.Province);
        Assert.Equal("2322", alem.PostalCode);
        Assert.False(alem.IsParking);

        var cochera = Row(viewModel, CocheraId);
        Assert.Equal(UnitRow.AbsentValue, cochera.FloorText);
        Assert.Equal(UnitRow.AbsentValue, cochera.ApartmentText);
        Assert.True(cochera.IsParking);
    }
}
