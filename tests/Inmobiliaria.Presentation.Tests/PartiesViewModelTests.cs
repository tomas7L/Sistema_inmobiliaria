using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Parties;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirements 1 to 3 and Tests 4 to 6 on the screen's logic, with the port faked.</summary>
public class PartiesViewModelTests
{
    private static readonly Guid JuanId = Guid.NewGuid();
    private static readonly Guid AnaId = Guid.NewGuid();

    private static readonly PartyListing Juan =
        new(JuanId, "Juan", "Pérez", "30123456", "20301234563", "juan@example.com", null, null);

    private static readonly PartyListing Ana = new(AnaId, "Ana", "Álvarez", null, null, null, null, null);

    private static FakePartyRegistry Port() => new() { Persons = [Ana, Juan] };

    private static async Task<PartiesViewModel> LoadedAsync(FakePartyRegistry port)
    {
        var viewModel = new PartiesViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static PartyRow Row(PartiesViewModel viewModel, Guid id) => viewModel.Persons.Single(p => p.Id == id);

    /// <summary>Two listings saying the same are equal, whichever list holds the roles, and a different role makes them different.</summary>
    [Fact]
    public void TwoListings_CompareByWhatTheySay_RolesIncluded()
    {
        var first = Juan with { ActiveRoles = [PartyRole.Tenant, PartyRole.Lessor] };
        var same = Juan with { ActiveRoles = new List<PartyRole> { PartyRole.Tenant, PartyRole.Lessor } };

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, Juan with { ActiveRoles = [PartyRole.Tenant] });
        Assert.NotEqual(first, first with { Email = "otro@example.com" });
    }

    /// <summary>Spec Test 93 on the screen: the column names every role, and is empty for someone on no contract in force.</summary>
    [Fact]
    public async Task TheList_NamesEachPersonsRoles()
    {
        var port = new FakePartyRegistry { Persons = [Juan with { ActiveRoles = [PartyRole.Lessor, PartyRole.Codebtor] }, Ana] };

        var viewModel = await LoadedAsync(port);

        Assert.Equal("Propietario, Codeudor", Row(viewModel, JuanId).RolesText);
        Assert.Equal(string.Empty, Row(viewModel, AnaId).RolesText);
    }

    /// <summary>Spec Test 93: the filter keeps whoever holds the role, works on what the search already brought, and clearing it lists everyone again.</summary>
    [Fact]
    public async Task TheRoleFilter_KeepsWhoeverHoldsTheRole_WithoutSearchingAgain()
    {
        var port = new FakePartyRegistry { Persons = [Juan with { ActiveRoles = [PartyRole.Lessor, PartyRole.Tenant] }, Ana with { ActiveRoles = [PartyRole.Codebtor] }] };
        var viewModel = await LoadedAsync(port);
        var searches = port.Searches.Count;

        viewModel.RoleFilter = PartyRole.Tenant;
        Assert.Equal([JuanId], viewModel.Persons.Select(p => p.Id));

        viewModel.RoleFilter = PartyRole.Codebtor;
        Assert.Equal([AnaId], viewModel.Persons.Select(p => p.Id));

        viewModel.RoleFilter = null;
        Assert.Equal(2, viewModel.Persons.Count);
        Assert.Equal(searches, port.Searches.Count);
    }

    /// <summary>A search or a re-read after saving keeps the filter the operator chose.</summary>
    [Fact]
    public async Task TheRoleFilter_StaysOnAfterSearchingAgain()
    {
        var port = new FakePartyRegistry { Persons = [Juan with { ActiveRoles = [PartyRole.Tenant] }, Ana] };
        var viewModel = await LoadedAsync(port);
        viewModel.RoleFilter = PartyRole.Tenant;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal([JuanId], viewModel.Persons.Select(p => p.Id));
    }


    /// <summary>
    /// A person the form accepts: a phone, so the phone-or-email rule holds (Decision 47), and, for a DNI typed alone, the
    /// CUIL that carries it, so no identifier warning stands between the test and the port (Decisions 46 and 47).
    /// </summary>
    private static void Fill(PartiesViewModel viewModel, string first, string last, string dni = "", string cuil = "")
    {
        viewModel.FirstName = first;
        viewModel.LastName = last;
        viewModel.Dni = dni;
        viewModel.Cuil = cuil.Length == 0 && dni.Length > 0 ? CuilFor(dni) : cuil;
        viewModel.PhoneNumber = "03493 421234";
    }

    /// <summary>The CUIL with prefix 20 that carries <paramref name="dni"/>, with its check digit.</summary>
    private static string CuilFor(string dni)
    {
        var body = "20" + new string(dni.Where(char.IsAsciiDigit).ToArray()).PadLeft(8, '0');
        int[] weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var digit = 11 - (body.Select((c, i) => (c - '0') * weights[i]).Sum() % 11);
        return body + (digit == 11 ? 0 : digit);
    }

    /// <summary>Requirement 1 and Decision 28: in the port's order; identifiers shown; a person with neither shows a warning.</summary>
    [Fact]
    public async Task TheList_KeepsThePortsOrder_AndAPersonWithNeitherIdentifierShowsAWarning()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.Equal([AnaId, JuanId], viewModel.Persons.Select(p => p.Id).ToList());
        Assert.Equal("Juan Pérez", Row(viewModel, JuanId).FullName);
        Assert.Equal("30123456", Row(viewModel, JuanId).Dni);
        Assert.Null(Row(viewModel, JuanId).IdentifierWarning);
        Assert.Contains("duplicados", Row(viewModel, AnaId).IdentifierWarning);
    }

    /// <summary>Requirement 1: the search box's text reaches the port as typed.</summary>
    [Fact]
    public async Task Searching_AsksThePortWithTheTypedText()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);

        viewModel.SearchText = "30.123.456";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("30.123.456", port.Searches[^1]);
    }

    /// <summary>Spec Test 4: a duplicate is refused naming the holder and offering that person; the form keeps its text.</summary>
    [Fact]
    public async Task ADuplicateDni_NamesTheHolder_AndOffersToUseThem()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.DniTaken, Juan);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Otro", "Señor", "30.123.456");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Juan Pérez", viewModel.RefusalMessage);
        Assert.Equal(Juan, viewModel.OfferedHolder);
        Assert.Equal("Otro", viewModel.FirstName);
        Assert.Null(viewModel.ChosenParty);

        viewModel.UseHolderCommand.Execute(null);

        Assert.Equal(JuanId, viewModel.ChosenParty!.Id);
        Assert.Null(viewModel.OfferedHolder);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal(string.Empty, viewModel.FirstName);
        Assert.Single(port.Created);
    }

    /// <summary>Spec Test 5 with Requirement 26: an invalid identifier shows its own text beside its field, and nothing is sent.</summary>
    [Fact]
    public async Task AWrongCheckDigit_ShowsItsOwnText()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Juan", "Pérez", cuil: "20-11111111-3");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyRefusal.CuilInvalid.OperatorText(), viewModel.Errors[PartyField.Cuil]);
        Assert.Equal(PartyFieldErrors.SummaryText, viewModel.Errors.Summary);
        Assert.Empty(port.Created);
        Assert.Null(viewModel.OfferedHolder);
        Assert.Null(viewModel.Runner.ErrorMessage);
    }

    /// <summary>Spec Test 6: neither identifier warns first, and the person is created only after accepting.</summary>
    [Fact]
    public async Task NeitherIdentifier_Warns_AndCreatesOnlyAfterAccepting()
    {
        var port = Port();
        port.OnCreate = (draft, accepted) => accepted
            ? PartyResult.Saved(new PartyListing(Guid.NewGuid(), draft.FirstName!, draft.LastName!, null, null, null, null, null))
            : PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Hans", "Müller");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyRefusal.NoIdentifierNotAccepted.OperatorText(), viewModel.Warning);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("Hans", viewModel.FirstName);

        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Created.Select(c => c.Accepted).ToList());
        Assert.Null(viewModel.Warning);
        Assert.Equal(string.Empty, viewModel.FirstName);
    }

    /// <summary>Spec Test 6: declining the warning writes nothing and keeps the form.</summary>
    [Fact]
    public async Task DecliningTheWarning_WritesNothing()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Hans", "Müller");
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.Null(viewModel.Warning);
        Assert.Single(port.Created);
        Assert.Equal("Hans", viewModel.FirstName);
    }

    /// <summary>Requirement 2: a successful create clears the form and re-reads the list.</summary>
    [Fact]
    public async Task Creating_ClearsTheForm_AndReadsTheListAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        var searches = port.Searches.Count;
        Fill(viewModel, "Lía", "Sosa", "28111222");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("28111222", port.Created.Single().Draft.Dni);
        Assert.False(port.Created.Single().Accepted);
        Assert.Equal(string.Empty, viewModel.FirstName);
        Assert.Equal(string.Empty, viewModel.Dni);
        Assert.Equal(searches + 1, port.Searches.Count);
    }

    /// <summary>Requirement 3: a name change shows old and new and writes nothing until confirmed.</summary>
    [Fact]
    public async Task CorrectingAName_ShowsOldAndNew_AndWritesOnlyAfterConfirming()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.FirstName = "Juan Carlos";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Corrected);
        var change = Assert.Single(viewModel.Pending!.Changes);
        Assert.Equal(("Nombre", "Juan", "Juan Carlos"), (change.Field, change.Old, change.New));
        Assert.Contains("Juan Pérez", viewModel.Pending.Text);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        var written = Assert.Single(port.Corrected);
        Assert.Equal(JuanId, written.Id);
        Assert.Equal("Juan Carlos", written.Draft.FirstName);
        Assert.Null(viewModel.Pending);
        Assert.Null(viewModel.Editing);
        Assert.Equal(string.Empty, viewModel.FirstName);
    }

    /// <summary>Requirement 3: cancelling a correction writes nothing and keeps the form to go on editing.</summary>
    [Fact]
    public async Task CancellingACorrection_WritesNothing()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.LastName = "Perez";
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.Null(viewModel.Pending);
        Assert.Empty(port.Corrected);
        Assert.Equal("Perez", viewModel.LastName);
        Assert.NotNull(viewModel.Editing);
    }

    /// <summary>Requirement 3: DNI and CUIL ask too, and an identifier removed reads as absent.</summary>
    [Fact]
    public async Task CorrectingIdentifiers_ListsEachChange()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "28.111.222";
        viewModel.Cuil = "";

        await viewModel.SaveCommand.ExecuteAsync(null);

        // A DNI with no CUIL warns first (Decision 47); accepted, old and new are asked as before.
        Assert.Equal(PartyWarning.DniWithoutCuil.OperatorText(), viewModel.Warning);
        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        Assert.Equal(
            [("DNI", "30123456", "28111222"), ("CUIL", "20301234563", PartiesViewModel.AbsentValue)],
            viewModel.Pending!.Changes.Select(c => (c.Field, c.Old, c.New)).ToList());
    }

    /// <summary>Requirement 3: a differently written identical DNI, or contact data alone, needs no confirmation.</summary>
    [Fact]
    public async Task ContactDataAlone_IsWrittenWithoutAConfirmation()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "30.123.456";
        viewModel.Email = "nuevo@example.com";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Pending);
        Assert.Equal("nuevo@example.com", Assert.Single(port.Corrected).Draft.Email);
    }

    /// <summary>Requirement 3: what the rules refuse is refused before asking anyone to confirm it.</summary>
    [Fact]
    public async Task AnInvalidCorrection_IsRefusedWithoutAskingToConfirm()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "12345";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Pending);
        Assert.Empty(port.Corrected);
        Assert.Equal(PartyRefusal.DniInvalid.OperatorText(), viewModel.Errors[PartyField.Dni]);
    }

    /// <summary>Requirement 3: correcting to a taken DNI names the holder, offers nothing to use, and stays in the form.</summary>
    [Fact]
    public async Task CorrectingToATakenDni_NamesTheHolder_WithoutOfferingToUseThem()
    {
        var port = Port();
        port.OnCorrect = (_, _, _) => PartyResult.Refused(PartyRefusal.DniTaken, Ana);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "28111222";
        viewModel.Cuil = CuilFor("28111222");
        await viewModel.SaveCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("Ana Álvarez", viewModel.RefusalMessage);
        Assert.Null(viewModel.OfferedHolder);
        Assert.NotNull(viewModel.Editing);
        Assert.Equal("28111222", viewModel.Dni);
    }

    /// <summary>Requirement 3 with Requirement 2's check: removing the last identifier warns, after the old and new were confirmed.</summary>
    [Fact]
    public async Task RemovingTheLastIdentifier_WarnsAfterConfirming()
    {
        var port = Port();
        port.OnCorrect = (id, draft, accepted) => accepted
            ? PartyResult.Saved(new PartyListing(id, draft.FirstName!, draft.LastName!, null, null, null, null, null))
            : PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "";
        viewModel.Cuil = "";
        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.Warning);

        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Corrected.Select(c => c.Accepted).ToList());
        Assert.Null(viewModel.Editing);
    }

    /// <summary>Requirement 3: what is confirmed is what is written, so editing any field withdraws the confirmation.</summary>
    [Theory]
    [InlineData(nameof(PartiesViewModel.FirstName))]
    [InlineData(nameof(PartiesViewModel.LastName))]
    [InlineData(nameof(PartiesViewModel.Dni))]
    [InlineData(nameof(PartiesViewModel.Cuil))]
    [InlineData(nameof(PartiesViewModel.Email))]
    [InlineData(nameof(PartiesViewModel.PhoneNumber))]
    [InlineData(nameof(PartiesViewModel.Domicile))]
    public async Task EditingAFieldAfterSaving_WithdrawsTheConfirmation(string field)
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.LastName = "Perez";
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Pending);

        typeof(PartiesViewModel).GetProperty(field)!.SetValue(viewModel, "27222333");

        Assert.Null(viewModel.Pending);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
        Assert.Empty(port.Corrected);
    }

    /// <summary>Requirement 3: the summary is asked again for the values now in the form, and those are what is written.</summary>
    [Fact]
    public async Task SavingAgainAfterEditing_ConfirmsAndWritesTheNewValues()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.FirstName = "Juan Carlos";
        await viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.FirstName = "Juan Pablo";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var change = Assert.Single(viewModel.Pending!.Changes);
        Assert.Equal(("Nombre", "Juan", "Juan Pablo"), (change.Field, change.Old, change.New));

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Juan Pablo", Assert.Single(port.Corrected).Draft.FirstName);
    }

    /// <summary>Spec Test 6: accepting the warning covers the person it warned about, so editing withdraws it too.</summary>
    [Fact]
    public async Task EditingAFieldAfterTheWarning_WithdrawsIt()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Hans", "Müller");
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Warning);

        viewModel.LastName = "Mueller";

        Assert.Null(viewModel.Warning);
        Assert.False(viewModel.AcceptWarningCommand.CanExecute(null));
        Assert.Single(port.Created);
    }

    /// <summary>A write that fails shows the generic text only, keeps what was typed and the commands, and reads nothing.</summary>
    [Fact]
    public async Task ACorrectionThatFails_ShowsTheGenericMessage_AndKeepsTheForm()
    {
        var port = Port();
        port.OnCorrect = (_, _, _) => throw new InvalidOperationException("Host=secret");
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.FirstName = "Juan Carlos";
        await viewModel.SaveCommand.ExecuteAsync(null);
        var searches = port.Searches.Count;

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Null(viewModel.OfferedHolder);
        Assert.Equal(JuanId, viewModel.Editing!.Id);
        Assert.Equal("Juan Carlos", viewModel.FirstName);
        Assert.Equal(searches, port.Searches.Count);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    /// <summary>Same for a person being created: the form survives, so nothing is typed twice.</summary>
    [Fact]
    public async Task ACreationThatFails_ShowsTheGenericMessage_AndKeepsTheForm()
    {
        var port = Port();
        port.OnCreate = (_, _) => throw new InvalidOperationException("Host=secret");
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Lía", "Sosa", "28111222");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Equal("Lía", viewModel.FirstName);
        Assert.Equal("28111222", viewModel.Dni);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    /// <summary>A write that landed stands even if the list cannot be read after it: the form clears, so it is not sent twice.</summary>
    [Fact]
    public async Task ASavedPerson_ClearsTheForm_EvenIfTheListCannotBeReadAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Lía", "Sosa", "28111222");
        port.SearchFailure = new InvalidOperationException("Host=secret");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(port.Created);
        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.FirstName);
    }

    /// <summary>The next action clears the previous refusal and offer.</summary>
    [Fact]
    public async Task TheNextAction_ClearsThePreviousRefusalAndOffer()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.DniTaken, Juan);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Otro", "Señor", "30123456");
        await viewModel.SaveCommand.ExecuteAsync(null);
        port.OnCreate = null;
        viewModel.Dni = "28111222";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.RefusalMessage);
        Assert.Null(viewModel.OfferedHolder);
    }

    // The screen as the designer drew it (gestion-de-alquileres tasks 12.3d): a list, and a form of its own reached from
    // "Nueva persona" or "Editar", which ends on a saved notice with the way back.

    /// <summary>The list reads "last, first", as its "Apellido y nombre" column says, with the phone and the email.</summary>
    [Fact]
    public async Task TheList_ShowsTheLastNameFirst_ThePhoneAndTheEmail()
    {
        var port = new FakePartyRegistry { Persons = [Juan with { PhoneNumber = "03493 15-0101" }] };

        var viewModel = await LoadedAsync(port);

        var row = Row(viewModel, JuanId);
        Assert.Equal("Pérez, Juan", row.ListName);
        Assert.Equal("03493 15-0101", row.PhoneNumber);
        Assert.Equal("juan@example.com", row.Email);
    }

    /// <summary>The list opens with no form; "Nueva persona" opens an empty one, and the way back closes it, clearing it.</summary>
    [Fact]
    public async Task NewPerson_OpensAnEmptyForm_AndGoingBackClosesIt()
    {
        var viewModel = await LoadedAsync(Port());
        Assert.False(viewModel.IsFormOpen);

        viewModel.NewCommand.Execute(null);

        Assert.True(viewModel.IsFormOpen);
        Assert.Null(viewModel.Editing);
        Assert.Equal("Nueva persona", viewModel.FormTitle);
        Assert.Equal("Complete los datos de identidad y de contacto.", viewModel.FormSubtitle);
        Assert.Equal("Guardar persona", viewModel.SaveText);
        Assert.Equal(
            "Cada persona se carga una sola vez, aunque sea propietaria en un contrato e inquilina en otro. Si ya está cargada, búsquela en Personas en vez de crearla de nuevo.",
            viewModel.IdentityHint);

        viewModel.FirstName = "Lía";
        viewModel.BackCommand.Execute(null);

        Assert.False(viewModel.IsFormOpen);
        Assert.Equal(string.Empty, viewModel.FirstName);
    }

    /// <summary>"Editar" needs a person, and opens the form on them, titled with their name as listed.</summary>
    [Fact]
    public async Task Editing_NeedsAPerson_AndOpensTheFormOnThem()
    {
        var viewModel = await LoadedAsync(Port());
        Assert.False(viewModel.EditCommand.CanExecute(null));

        viewModel.EditCommand.Execute(Row(viewModel, JuanId));

        Assert.True(viewModel.IsFormOpen);
        Assert.Equal("Juan", viewModel.FirstName);
        Assert.Equal("Editar persona", viewModel.FormTitle);
        Assert.Equal("Pérez, Juan", viewModel.FormSubtitle);
        Assert.Equal("Guardar cambios", viewModel.SaveText);
        Assert.Equal(
            "Puede corregir el nombre, el apellido, el DNI y el CUIL si se cargaron mal. El DNI y el CUIL no pueden coincidir con los de otra persona.",
            viewModel.IdentityHint);
    }

    /// <summary>A person created ends on the saved notice, named as the list names them; the form stays open to show it.</summary>
    [Fact]
    public async Task Creating_EndsOnTheSavedNotice()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.NewCommand.Execute(null);
        Fill(viewModel, "Lía", "Sosa", "28111222");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsFormOpen);
        Assert.Equal(("Persona guardada", "Sosa, Lía ya está en la lista de personas."), (viewModel.Saved!.Title, viewModel.Saved.Text));

        viewModel.BackCommand.Execute(null);

        Assert.False(viewModel.IsFormOpen);
        Assert.Null(viewModel.Saved);
    }

    /// <summary>A confirmed correction of identity data says the recibos keep what they printed; contact data alone says so.</summary>
    [Fact]
    public async Task ACorrection_SaysWhichDataItChanged()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.FirstName = "Juan Carlos";
        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(
            ("Corrección guardada", "Se actualizaron los datos de identidad. Los recibos ya emitidos conservan lo que imprimieron."),
            (viewModel.Saved!.Title, viewModel.Saved.Text));

        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        Assert.Null(viewModel.Saved);
        viewModel.Email = "nuevo@example.com";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("Cambios guardados", "Se actualizaron los datos de contacto."), (viewModel.Saved!.Title, viewModel.Saved.Text));
    }

    /// <summary>
    /// The canvas "Editar persona": the saved notice of a correction stays under that title, with the person as saved for
    /// subtitle (the same defect Tomás saw on Propiedades, 2026-11-19). A creation keeps its own title, and the way back
    /// forgets both.
    /// </summary>
    [Fact]
    public async Task TheSavedNotice_KeepsTheTitleOfWhatWasSaved()
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Email = "nuevo@example.com";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyFormStep.Saved, viewModel.Step);
        Assert.Equal("Editar persona", viewModel.FormTitle);
        Assert.Equal("Pérez, Juan", viewModel.FormSubtitle);

        viewModel.BackCommand.Execute(null);
        viewModel.NewCommand.Execute(null);

        Assert.Equal("Nueva persona", viewModel.FormTitle);
        Assert.Equal(PartiesTexts.NewSubtitle, viewModel.FormSubtitle);

        Fill(viewModel, "Lía", "Sosa", "28111222");
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyFormStep.Saved, viewModel.Step);
        Assert.Equal("Nueva persona", viewModel.FormTitle);
        Assert.Equal(PartiesTexts.NewSubtitle, viewModel.FormSubtitle);
    }

    /// <summary>Removing the last identifier is a correction of identity too, once its warning is accepted.</summary>
    [Fact]
    public async Task RemovingTheLastIdentifier_IsSavedAsACorrection()
    {
        var port = Port();
        port.OnCorrect = (id, draft, accepted) => accepted
            ? PartyResult.Saved(new PartyListing(id, draft.FirstName!, draft.LastName!, null, null, null, null, null))
            : PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        viewModel.Dni = "";
        viewModel.Cuil = "";
        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        Assert.Equal("Corrección guardada", viewModel.Saved!.Title);
    }

    /// <summary>A refusal leaves the form open with no saved notice, so the operator can fix it.</summary>
    [Fact]
    public async Task ARefusal_KeepsTheFormOpen_WithNoSavedNotice()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.NewCommand.Execute(null);
        Fill(viewModel, "Juan", "Pérez", cuil: "20-11111111-3");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsFormOpen);
        Assert.Null(viewModel.Saved);
    }

    /// <summary>"Usar esa persona" goes back to the list with the holder selected, as the designer's navigation says.</summary>
    [Fact]
    public async Task UsingTheHolder_GoesBackToTheList_WithThemSelected()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.DniTaken, Juan);
        var viewModel = await LoadedAsync(port);
        viewModel.NewCommand.Execute(null);
        Fill(viewModel, "Otro", "Señor", "30.123.456");
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.UseHolderCommand.Execute(null);

        Assert.False(viewModel.IsFormOpen);
        Assert.Equal(JuanId, viewModel.SelectedPerson!.Id);
    }

    /// <summary>Requirement 26, Test 102 on the screen: every error at once, each beside its field, and the port never called.</summary>
    [Fact]
    public async Task EveryErrorIsShownAtOnce_AndNothingIsSent()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.NewCommand.Execute(null);
        viewModel.FirstName = "2432342";
        viewModel.LastName = "Pérez";
        viewModel.Dni = "wewefwee";
        viewModel.PhoneNumber = "sdeewedwe";
        viewModel.Email = "ana@gmail";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.Errors.Count);
        Assert.Equal(PartyRefusal.NameInvalid.OperatorText(), viewModel.Errors[PartyField.FirstName]);
        Assert.Null(viewModel.Errors[PartyField.LastName]);
        Assert.Equal(PartyRefusal.DniInvalid.OperatorText(), viewModel.Errors[PartyField.Dni]);
        Assert.Equal(PartyRefusal.PhoneInvalid.OperatorText(), viewModel.Errors[PartyField.Phone]);
        Assert.Equal(PartyRefusal.EmailInvalid.OperatorText(), viewModel.Errors[PartyField.Email]);
        Assert.Empty(port.Created);
        Assert.Equal(PartyFormStep.Fields, viewModel.Step);
    }

    /// <summary>Requirement 26: neither phone nor email is an error beside the contact fields.</summary>
    [Fact]
    public async Task NeitherPhoneNorEmail_IsAnError()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Lía", "Sosa", "28111222");
        viewModel.PhoneNumber = "";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyRefusal.ContactMissing.OperatorText(), viewModel.Errors[PartyField.Contact]);
        Assert.Empty(port.Created);
    }

    /// <summary>Decision 46: a CUIL not carrying the DNI warns; accepting it writes both as typed.</summary>
    [Fact]
    public async Task ACuilNotCarryingTheDni_Warns_AndAcceptingWritesIt()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Lía", "Sosa", "30123456", "20-28111222-9");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyWarning.CuilDoesNotCarryDni.OperatorText(), viewModel.Warning);
        Assert.Equal(PartyFormStep.Warning, viewModel.Step);
        Assert.Empty(port.Created);

        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        var created = Assert.Single(port.Created);
        Assert.Equal(("30123456", "20-28111222-9"), (created.Draft.Dni, created.Draft.Cuil));
        Assert.Equal("Persona guardada", viewModel.Saved!.Title);
    }

    /// <summary>Decision 46: what was accepted covers the values it warned about, so editing one asks again.</summary>
    [Fact]
    public async Task EditingAfterAcceptingAWarning_AsksAgain()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.DniTaken, Juan);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Lía", "Sosa", "30123456", "20-28111222-9");
        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.AcceptWarningCommand.ExecuteAsync(null);

        viewModel.Dni = "30123457";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyWarning.CuilDoesNotCarryDni.OperatorText(), viewModel.Warning);
        Assert.Single(port.Created);
    }

    /// <summary>The form shows one step at a time: the fields, the summary to confirm, the warning to accept, or the saved notice.</summary>
    [Fact]
    public async Task TheForm_ShowsOneStepAtATime()
    {
        var port = Port();
        port.OnCorrect = (id, draft, accepted) => accepted
            ? PartyResult.Saved(new PartyListing(id, draft.FirstName!, draft.LastName!, null, null, null, null, null))
            : PartyResult.Refused(PartyRefusal.NoIdentifierNotAccepted);
        var viewModel = await LoadedAsync(port);
        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        Assert.Equal(PartyFormStep.Fields, viewModel.Step);

        viewModel.Dni = "";
        viewModel.Cuil = "";
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(PartyFormStep.Confirm, viewModel.Step);

        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(PartyFormStep.Warning, viewModel.Step);

        await viewModel.AcceptWarningCommand.ExecuteAsync(null);
        Assert.Equal(PartyFormStep.Saved, viewModel.Step);

        viewModel.EditCommand.Execute(Row(viewModel, JuanId));
        Assert.Equal(PartyFormStep.Fields, viewModel.Step);
    }

    /// <summary>The foot of the list counts who is shown, and the roles come one per label, in the listing's order.</summary>
    [Fact]
    public async Task TheList_CountsWhoIsShown_AndNamesEachRoleApart()
    {
        var port = new FakePartyRegistry { Persons = [Juan with { ActiveRoles = [PartyRole.Lessor, PartyRole.Codebtor] }, Ana] };
        var viewModel = await LoadedAsync(port);

        Assert.Equal(["Propietario", "Codeudor"], Row(viewModel, JuanId).RoleNames);
        Assert.Equal("2 personas", viewModel.CountText);
        Assert.False(viewModel.IsEmpty);

        viewModel.RoleFilter = PartyRole.Lessor;
        Assert.Equal("1 persona", viewModel.CountText);

        viewModel.RoleFilter = PartyRole.Tenant;
        Assert.True(viewModel.IsEmpty);
    }
}
