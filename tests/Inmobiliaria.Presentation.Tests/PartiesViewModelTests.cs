using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Presentation.Parties;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirements 1 to 3 and Tests 4 to 6 on the screen's logic, with the port faked.</summary>
public class PartiesViewModelTests
{
    private static readonly Guid JuanId = Guid.NewGuid();
    private static readonly Guid AnaId = Guid.NewGuid();

    private static readonly PartyListing Juan =
        new(JuanId, "Juan", "Pérez", "30123456", "20111111112", "juan@example.com", null, null);

    private static readonly PartyListing Ana = new(AnaId, "Ana", "Álvarez", null, null, null, null, null);

    private static FakePartyRegistry Port() => new() { Persons = [Ana, Juan] };

    private static async Task<PartiesViewModel> LoadedAsync(FakePartyRegistry port)
    {
        var viewModel = new PartiesViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static PartyRow Row(PartiesViewModel viewModel, Guid id) => viewModel.Persons.Single(p => p.Id == id);

    private static void Fill(PartiesViewModel viewModel, string first, string last, string dni = "", string cuil = "")
    {
        viewModel.FirstName = first;
        viewModel.LastName = last;
        viewModel.Dni = dni;
        viewModel.Cuil = cuil;
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

    /// <summary>Spec Test 5: an invalid identifier shows its own text and the port is still what refuses on create.</summary>
    [Fact]
    public async Task AWrongCheckDigit_ShowsItsOwnText()
    {
        var port = Port();
        port.OnCreate = (_, _) => PartyResult.Refused(PartyRefusal.CuilInvalid);
        var viewModel = await LoadedAsync(port);
        Fill(viewModel, "Juan", "Pérez", cuil: "20-11111111-3");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(PartyRefusal.CuilInvalid.OperatorText(), viewModel.RefusalMessage);
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

        Assert.Equal(PartyRefusal.NoIdentifierNotAccepted.OperatorText(), viewModel.NoIdentifierWarning);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("Hans", viewModel.FirstName);

        await viewModel.AcceptNoIdentifierCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Created.Select(c => c.Accepted).ToList());
        Assert.Null(viewModel.NoIdentifierWarning);
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

        Assert.Null(viewModel.NoIdentifierWarning);
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

        Assert.Equal(
            [("DNI", "30123456", "28111222"), ("CUIL", "20111111112", PartiesViewModel.AbsentValue)],
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
        Assert.Equal(PartyRefusal.DniInvalid.OperatorText(), viewModel.RefusalMessage);
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

        Assert.NotNull(viewModel.NoIdentifierWarning);

        await viewModel.AcceptNoIdentifierCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Corrected.Select(c => c.Accepted).ToList());
        Assert.Null(viewModel.Editing);
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
}
