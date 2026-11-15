using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 26 and Tests 96 to 103 as pure rules (Decisions 43 to 47). The CUILs are fictional,
/// with their check digit computed, so no real person's data reaches this public repository.
/// </summary>
public class PartyDraftProblemsTests
{
    private const string Dni = "30123456";
    private const string CuilOfDni = "20-30123456-3";
    private const string Phone = "03493 421234";

    private static PartyDraft Valid(
        string first = "Juan", string last = "Pérez", string? dni = Dni, string? cuil = CuilOfDni,
        string? email = null, string? phone = Phone, string? domicile = null) =>
        new(first, last, dni, cuil, email, phone, domicile);

    private static IReadOnlyList<PartyProblem> Problems(PartyDraft draft) => PartyDraftRules.Problems(draft);

    [Fact]
    public void AValidPerson_HasNoProblem()
    {
        Assert.Empty(Problems(Valid()));
    }

    /// <summary>Test 96: letters of any language, space, apostrophe, hyphen and dot; at least one letter; 60 characters.</summary>
    [Theory]
    [InlineData("Ma. José", true)]
    [InlineData("D'Angelo-Pérez", true)]
    [InlineData("D’Angelo", true)]
    [InlineData("Ñandú Müller", true)]
    [InlineData("2432342", false)]
    [InlineData("Juan2", false)]
    [InlineData("Juan_", false)]
    [InlineData("'-.", false)]
    public void ANameTakesLettersAndAFewSigns(string name, bool accepted)
    {
        var expected = accepted ? [] : new[] { new PartyProblem(PartyField.FirstName, PartyRefusal.NameInvalid) };

        Assert.Equal(expected, Problems(Valid(first: name)));
    }

    [Fact]
    public void ANameOf60CharactersPasses_And61DoNot()
    {
        Assert.Empty(Problems(Valid(last: new string('a', 60))));
        Assert.Equal(
            [new PartyProblem(PartyField.LastName, PartyRefusal.NameTooLong)],
            Problems(Valid(last: new string('a', 61))));
    }

    [Fact]
    public void AMissingName_IsAProblemOfItsOwnField()
    {
        Assert.Equal(
            [new PartyProblem(PartyField.FirstName, PartyRefusal.NameMissing), new PartyProblem(PartyField.LastName, PartyRefusal.NameMissing)],
            Problems(Valid(first: " ", last: "")));
    }

    /// <summary>Test 97: a DNI with a letter is refused, not read as absent; dots and spaces still pass.</summary>
    [Theory]
    [InlineData("wewefwee", false)]
    [InlineData("30.123.456", true)]
    [InlineData("30 123 456", true)]
    public void ADniTakesDigitsOnly(string dni, bool accepted)
    {
        var expected = accepted ? [] : new[] { new PartyProblem(PartyField.Dni, PartyRefusal.DniInvalid) };

        Assert.Equal(expected, Problems(Valid(dni: dni)));
    }

    /// <summary>Test 107 for the CUIL: a wrong length says so, apart from a wrong check digit.</summary>
    [Theory]
    [InlineData("20-3012345-6", PartyRefusal.CuilWrongLength)]
    [InlineData("20-30123456-4", PartyRefusal.CuilInvalid)]
    [InlineData("20-3012345a-3", PartyRefusal.CuilWrongLength)]
    public void ACuilSaysWhatIsWrongWithIt(string cuil, PartyRefusal refusal)
    {
        Assert.Equal([new PartyProblem(PartyField.Cuil, refusal)], Problems(Valid(cuil: cuil)));
    }

    /// <summary>Test 98: digits, spaces, a leading +, hyphens and parentheses, with 8 to 15 digits.</summary>
    [Theory]
    [InlineData("03493 15-421234", true)]
    [InlineData("+54 9 3493 421234", true)]
    [InlineData("(03493) 421234", true)]
    [InlineData("12345678", true)]
    [InlineData("1234567", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("sdeewedwe", false)]
    [InlineData("03493 4212a4", false)]
    [InlineData("54+3493421234", false)]
    public void APhoneIsWrittenTheUsualWays(string phone, bool accepted)
    {
        var expected = accepted ? [] : new[] { new PartyProblem(PartyField.Phone, PartyRefusal.PhoneInvalid) };

        Assert.Equal(expected, Problems(Valid(phone: phone)));
    }

    /// <summary>Test 99: an email needs a dot in its domain, besides being an address at all.</summary>
    [Theory]
    [InlineData("ana@gmail.com", true)]
    [InlineData("ana.perez@mail.example.com.ar", true)]
    [InlineData("ana@gmail", false)]
    [InlineData("dcedd", false)]
    [InlineData("ana @gmail.com", false)]
    [InlineData("ana@.com", false)]
    [InlineData("ana@gmail.", false)]
    public void AnEmailNeedsADomainWithADot(string email, bool accepted)
    {
        var expected = accepted ? [] : new[] { new PartyProblem(PartyField.Email, PartyRefusal.EmailInvalid) };

        Assert.Equal(expected, Problems(Valid(email: email)));
    }

    /// <summary>Test 99's premise: .NET's own check, the one the notices use, accepts the dotless domain this rule refuses.</summary>
    [Fact]
    public void TheNoticesCheckAlone_AcceptsADotlessDomain()
    {
        Assert.True(System.Net.Mail.MailAddress.TryCreate("ana@gmail", out _));
    }

    /// <summary>Test 100: a domicile of 120 characters passes and of 121 does not; an empty one passes.</summary>
    [Fact]
    public void ADomicileHasAMaximumLength()
    {
        Assert.Empty(Problems(Valid(domicile: new string('a', 120))));
        Assert.Empty(Problems(Valid(domicile: " ")));
        Assert.Equal(
            [new PartyProblem(PartyField.Domicile, PartyRefusal.DomicileTooLong)],
            Problems(Valid(domicile: new string('a', 121))));
    }

    /// <summary>Test 101 and Decision 47: neither phone nor email is refused; either alone passes.</summary>
    [Fact]
    public void APersonNeedsAPhoneOrAnEmail()
    {
        Assert.Equal([new PartyProblem(PartyField.Contact, PartyRefusal.ContactMissing)], Problems(Valid(phone: null)));
        Assert.Empty(Problems(Valid(phone: null, email: "ana@gmail.com")));
    }

    /// <summary>Test 102: every problem at once, each with its field, in the form's order.</summary>
    [Fact]
    public void EveryProblemIsReturned_NotOnlyTheFirst()
    {
        var draft = new PartyDraft("2432342", "Pérez", "wewefwee", "20-30123456-4", "dcedd", "sdeewedwe");

        Assert.Equal(
            [PartyField.FirstName, PartyField.Dni, PartyField.Cuil, PartyField.Phone, PartyField.Email],
            Problems(draft).Select(p => p.Field));
    }

    /// <summary>Test 103: a CUIL not carrying the DNI warns, and so does a DNI without a CUIL; neither refuses.</summary>
    [Theory]
    [InlineData(Dni, "20-28111222-9", PartyWarning.CuilDoesNotCarryDni)]
    [InlineData(Dni, null, PartyWarning.DniWithoutCuil)]
    public void MismatchedOrMissingIdentifiers_Warn(string? dni, string? cuil, PartyWarning warning)
    {
        var draft = Valid(dni: dni, cuil: cuil);

        Assert.Empty(Problems(draft));
        Assert.Equal([warning], PartyDraftRules.Warnings(draft));
    }

    [Theory]
    [InlineData(Dni, CuilOfDni)]
    [InlineData("1234567", "20-01234567-5")]
    [InlineData(null, CuilOfDni)]
    [InlineData(null, null)]
    public void MatchingIdentifiers_OrNoDni_DoNotWarn(string? dni, string? cuil)
    {
        Assert.Empty(PartyDraftRules.Warnings(Valid(dni: dni, cuil: cuil)));
    }

    /// <summary>The port's guard: format problems refuse there too; the phone-or-email rule is the form's (task 12.3e).</summary>
    [Fact]
    public void Check_RefusesAFormatProblem_ButNotAMissingContact()
    {
        Assert.Equal(PartyRefusal.PhoneInvalid, PartyDraftRules.Check(Valid(phone: "abc"), acceptNoIdentifier: false));
        Assert.Null(PartyDraftRules.Check(Valid(phone: null), acceptNoIdentifier: false));
    }

    /// <summary>Each new refusal and warning has its own text.</summary>
    [Fact]
    public void EachNewRefusalAndWarning_HasItsOwnText()
    {
        var texts = new[]
        {
            PartyRefusal.NameInvalid, PartyRefusal.NameTooLong, PartyRefusal.CuilWrongLength, PartyRefusal.CuilInvalid,
            PartyRefusal.PhoneInvalid, PartyRefusal.EmailInvalid, PartyRefusal.DomicileTooLong, PartyRefusal.ContactMissing,
        }.Select(r => r.OperatorText()).Concat(
            [PartyWarning.CuilDoesNotCarryDni.OperatorText(), PartyWarning.DniWithoutCuil.OperatorText()]).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }
}
