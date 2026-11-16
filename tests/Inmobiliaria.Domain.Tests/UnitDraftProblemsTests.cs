using Inmobiliaria.Domain.Units;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirements 27 and 28, Decisions 44, 48 and 49, Tests 108, 111, 112 and 119: every problem of an
/// address at once, each with its field, the lengths of Decision 48, the fixed list of provinces and the postal code's two forms.
/// </summary>
public class UnitDraftProblemsTests
{
    private static UnitDraft Valid() => new("Alem", "1258", "Sunchales", "Santa Fe", "2322", "2", "B");

    [Fact]
    public void AValidAddress_HasNoProblems()
    {
        Assert.Empty(UnitDraftRules.Problems(Valid()));
    }

    /// <summary>Decision 44: all of them together, in the form's order, not the first one alone.</summary>
    [Fact]
    public void EveryProblem_IsReturnedAtOnce_InTheFormsOrder()
    {
        var draft = new UnitDraft("", new string('9', 11), "", "Sta Fe", "", new string('P', 21), null);

        Assert.Equal(
            [
                new UnitProblem(UnitField.Street, UnitProblemKind.Missing),
                new UnitProblem(UnitField.Number, UnitProblemKind.TooLong),
                new UnitProblem(UnitField.Floor, UnitProblemKind.TooLong),
                new UnitProblem(UnitField.City, UnitProblemKind.Missing),
                new UnitProblem(UnitField.Province, UnitProblemKind.NotInList),
                new UnitProblem(UnitField.PostalCode, UnitProblemKind.Missing),
            ],
            UnitDraftRules.Problems(draft));
    }

    /// <summary>Test 108: each free text passes at its maximum and is refused one character past it.</summary>
    [Theory]
    [InlineData(UnitField.Street, 80)]
    [InlineData(UnitField.Number, 10)]
    [InlineData(UnitField.Floor, 20)]
    [InlineData(UnitField.Apartment, 20)]
    [InlineData(UnitField.City, 60)]
    public void EachFreeText_PassesAtItsMaximum_AndIsRefusedOnePast(UnitField field, int maximum)
    {
        Assert.Equal(maximum, UnitDraftRules.MaximumLength(field));
        Assert.Empty(UnitDraftRules.Problems(With(field, new string('a', maximum))));
        Assert.Equal(
            [new UnitProblem(field, UnitProblemKind.TooLong)],
            UnitDraftRules.Problems(With(field, new string('a', maximum + 1))));
    }

    /// <summary>The length is read after trimming, as what is stored.</summary>
    [Fact]
    public void TheLength_IsReadAfterTrimming()
    {
        Assert.Empty(UnitDraftRules.Problems(With(UnitField.City, "  " + new string('a', 60) + "  ")));
    }

    /// <summary>Test 119: the old four digits and a CPA of the chosen province pass, spaces around them and a lower-case CPA too.</summary>
    [Theory]
    [InlineData("2322")]
    [InlineData("1000")]
    [InlineData("S2324XAA")]
    [InlineData(" s2324xaa ")]
    public void APostalCodeInEitherForm_Passes(string postalCode)
    {
        Assert.Empty(UnitDraftRules.Problems(With(UnitField.PostalCode, postalCode)));
        Assert.Null(UnitDraftRules.Check(With(UnitField.PostalCode, postalCode)));
    }

    /// <summary>A CPA is stored in capitals, as it is printed.</summary>
    [Fact]
    public void ACpa_IsStoredInCapitals()
    {
        Assert.Equal("S2324XAA", With(UnitField.PostalCode, " s2324xaa ").Normalized().PostalCode);
    }

    /// <summary>Test 119: anything in neither form is refused by the form and the port.</summary>
    [Theory]
    [InlineData("23")]
    [InlineData("23222")]
    [InlineData("0322")]
    [InlineData("abcd")]
    [InlineData("S2324")]
    [InlineData("S2324XA1")]
    [InlineData("2322 ABC")]
    [InlineData("12345678901")]
    public void APostalCodeInNeitherForm_IsRefused(string postalCode)
    {
        var draft = With(UnitField.PostalCode, postalCode);

        Assert.Equal([new UnitProblem(UnitField.PostalCode, UnitProblemKind.Malformed)], UnitDraftRules.Problems(draft));
        Assert.Equal(UnitRefusal.PostalCodeMalformed, UnitDraftRules.Check(draft));
    }

    /// <summary>Test 119: a CPA whose letter is another province's is refused, naming both.</summary>
    [Fact]
    public void ACpaOfAnotherProvince_IsRefused()
    {
        var draft = With(UnitField.PostalCode, "X5000ABC");

        var problem = Assert.Single(UnitDraftRules.Problems(draft));
        Assert.Equal(new UnitProblem(UnitField.PostalCode, UnitProblemKind.OtherProvince), problem);
        Assert.Equal(UnitRefusal.PostalCodeOfAnotherProvince, UnitDraftRules.Check(draft));
        Assert.Equal("Un código postal de Santa Fe empieza con S, no con X.", problem.OperatorText(draft));
    }

    /// <summary>Every jurisdiction has its own CPA letter, the ISO 3166-2:AR one.</summary>
    [Theory]
    [InlineData("Ciudad Autónoma de Buenos Aires", 'C')]
    [InlineData("Buenos Aires", 'B')]
    [InlineData("Córdoba", 'X')]
    [InlineData("Santa Fe", 'S')]
    [InlineData("Tierra del Fuego, Antártida e Islas del Atlántico Sur", 'V')]
    public void EachProvince_HasItsCpaLetter(string province, char letter)
    {
        Assert.Equal(letter, Provinces.CpaLetter(province));
        Assert.Equal(24, Provinces.All.Select(Provinces.CpaLetter).Distinct().Count());
        Assert.Null(UnitDraftRules.Check(With(UnitField.Province, province) with { PostalCode = $"{letter}1000ABC" }));
    }

    /// <summary>A province off the list says so alone: the CPA's letter is not compared with it.</summary>
    [Fact]
    public void AProvinceOffTheList_DoesNotAlsoRefuseTheCpa()
    {
        var draft = With(UnitField.Province, "Sta Fe") with { PostalCode = "S2324XAA" };

        Assert.Equal([new UnitProblem(UnitField.Province, UnitProblemKind.NotInList)], UnitDraftRules.Problems(draft));
    }

    /// <summary>Test 111: exactly the 24 jurisdictions, with Santa Fe the one preselected.</summary>
    [Fact]
    public void TheProvinces_AreTheTwentyFourJurisdictions_WithSantaFePreselected()
    {
        Assert.Equal(24, Provinces.All.Count);
        Assert.Equal(24, Provinces.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Ciudad Autónoma de Buenos Aires", Provinces.All);
        Assert.Contains("Buenos Aires", Provinces.All);
        Assert.Contains("Tierra del Fuego, Antártida e Islas del Atlántico Sur", Provinces.All);
        Assert.Equal("Santa Fe", Provinces.Default);
        Assert.Contains(Provinces.Default, Provinces.All);
    }

    /// <summary>Test 111: the port refuses any province that is not on the list, as the audit found stored.</summary>
    [Theory]
    [InlineData("Sta Fe")]
    [InlineData("santa fe")]
    [InlineData("SF")]
    public void AProvinceOffTheList_IsRefused_ByTheFormAndThePort(string province)
    {
        var draft = With(UnitField.Province, province);

        Assert.Equal([new UnitProblem(UnitField.Province, UnitProblemKind.NotInList)], UnitDraftRules.Problems(draft));
        Assert.Equal(UnitRefusal.ProvinceNotInList, UnitDraftRules.Check(draft));
    }

    /// <summary>Every province on the list passes, spaces around it included.</summary>
    [Fact]
    public void EveryProvinceOnTheList_Passes()
    {
        Assert.All(Provinces.All, p => Assert.Null(UnitDraftRules.Check(With(UnitField.Province, $" {p} "))));
    }

    /// <summary>The port's guard: a text past its length is refused with its own refusal, not as incomplete.</summary>
    [Fact]
    public void ThePort_RefusesATextPastItsLength()
    {
        Assert.Equal(UnitRefusal.AddressTooLong, UnitDraftRules.Check(With(UnitField.Street, new string('a', 81))));
    }

    /// <summary>Test 112: "PB", "Local 3" and "S/N" are written as typed.</summary>
    [Fact]
    public void FloorApartmentAndNumber_AsTheAgencyWritesThem_Pass()
    {
        var draft = new UnitDraft("Alem", "S/N", "Sunchales", "Santa Fe", "2322", "PB", "Local 3");

        Assert.Empty(UnitDraftRules.Problems(draft));
        Assert.Null(UnitDraftRules.Check(draft));
        Assert.Equal(draft, draft.Normalized());
    }

    /// <summary>Decision 50: each problem says what is wrong with its own field.</summary>
    [Fact]
    public void EachProblem_HasItsOwnText()
    {
        var texts = Enum.GetValues<UnitField>()
            .SelectMany(f => Enum.GetValues<UnitProblemKind>().Select(k => new UnitProblem(f, k)))
            .Where(p => p.Kind != UnitProblemKind.NotInList || p.Field == UnitField.Province)
            .Where(p => p.Kind is not (UnitProblemKind.Malformed or UnitProblemKind.OtherProvince) || p.Field == UnitField.PostalCode)
            .Where(p => p.Kind != UnitProblemKind.TooLong || p.Field is not (UnitField.Province or UnitField.PostalCode))
            .Where(p => p.Kind != UnitProblemKind.Missing || (p.Field != UnitField.Floor && p.Field != UnitField.Apartment))
            .Select(p => p.OperatorText(Valid() with { PostalCode = "X5000ABC" }))
            .ToList();

        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("La calle admite hasta 80 caracteres.", new UnitProblem(UnitField.Street, UnitProblemKind.TooLong).OperatorText(Valid()));
    }

    private static UnitDraft With(UnitField field, string value) => field switch
    {
        UnitField.Street => Valid() with { Street = value },
        UnitField.Number => Valid() with { Number = value },
        UnitField.Floor => Valid() with { Floor = value },
        UnitField.Apartment => Valid() with { Apartment = value },
        UnitField.City => Valid() with { City = value },
        UnitField.Province => Valid() with { Province = value },
        UnitField.PostalCode => Valid() with { PostalCode = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}
