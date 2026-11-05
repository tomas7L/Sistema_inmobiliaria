using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Requirement 3 on the entity, and the behaviour it must leave alone.</summary>
public class PartyTests
{
    private static Party Juan() =>
        new(Guid.NewGuid(), "Juan", "Perez", "30123456", null, "juan@example.com", "3493 000000", "Bv. Belgrano 123");

    [Fact]
    public void CorrectIdentity_ChangesNameAndIdentifiers_StoresDigitsOnly_AndLeavesContactAlone()
    {
        var party = Juan();
        var id = party.Id;

        party.CorrectIdentity(" Juan Carlos ", " Pérez ", "30.123.457", "20-11111111-2");

        Assert.Equal(id, party.Id);
        Assert.Equal("Juan Carlos", party.FirstName);
        Assert.Equal("Pérez", party.LastName);
        Assert.Equal("30123457", party.Dni);
        Assert.Equal("20111111112", party.Cuil);
        Assert.Equal("juan@example.com", party.Email);
        Assert.Equal("3493 000000", party.PhoneNumber);
        Assert.Equal("Bv. Belgrano 123", party.Domicile);
    }

    [Fact]
    public void CorrectIdentity_MakesABlankIdentifierAbsent()
    {
        var party = Juan();

        party.CorrectIdentity("Juan", "Perez", " ", null);

        Assert.Null(party.Dni);
        Assert.Null(party.Cuil);
    }

    [Theory]
    [InlineData(" ", "Perez", "30123456", null)]
    [InlineData("Juan", " ", "30123456", null)]
    [InlineData("Juan", "Perez", "12345", null)]
    [InlineData("Juan", "Perez", null, "20111111113")]
    public void CorrectIdentity_RefusesWhatTheRulesRefuse_AndChangesNothing(string first, string last, string? dni, string? cuil)
    {
        var party = Juan();

        Assert.Throws<ArgumentException>(() => party.CorrectIdentity(first, last, dni, cuil));

        Assert.Equal(("Juan", "Perez", "30123456", (string?)null), (party.FirstName, party.LastName, party.Dni, party.Cuil));
    }

    /// <summary>Existing behaviour: the constructor stores what it is given and <c>UpdateContactInfo</c> touches contact only.</summary>
    [Fact]
    public void TheConstructorAndUpdateContactInfo_BehaveAsBefore()
    {
        var unchecked_ = new Party(Guid.NewGuid(), "Ana", "Gomez", "30.123.456", "no-es-un-cuil");
        var noIdentifier = new Party(Guid.NewGuid(), "Ana", "Gomez");
        var party = Juan();

        party.UpdateContactInfo("otro@example.com", null, "Otra 1");

        Assert.Equal("30.123.456", unchecked_.Dni);
        Assert.Equal("no-es-un-cuil", unchecked_.Cuil);
        Assert.Null(noIdentifier.Dni);
        Assert.Null(noIdentifier.Cuil);
        Assert.Throws<ArgumentException>(() => new Party(Guid.NewGuid(), " ", "Gomez"));
        Assert.Equal(("otro@example.com", (string?)null, "Otra 1"), (party.Email, party.PhoneNumber, party.Domicile));
        Assert.Equal(("Juan", "Perez", "30123456"), (party.FirstName, party.LastName, party.Dni));
    }
}
