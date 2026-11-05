using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 3, 5 and 6 as pure rules (Decision 28). The CUILs are fictional: repeated digits
/// with the check digit computed by the design's weights (§2.1), so no real person's data reaches this public repository.
/// </summary>
public class PartyIdentifiersTests
{
    /// <summary>Spec Test 3: stored digits only, whichever way it was typed; empty means absent.</summary>
    [Theory]
    [InlineData("30.123.456", "30123456")]
    [InlineData("30123456", "30123456")]
    [InlineData(" 30-123-456 ", "30123456")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" .- ", null)]
    [InlineData(null, null)]
    public void NormalizeDni_LeavesDigitsOnly_AndEmptyIsAbsent(string? typed, string? expected)
    {
        Assert.Equal(expected, PartyIdentifiers.NormalizeDni(typed));
    }

    [Theory]
    [InlineData("20-11111111-2", "20111111112")]
    [InlineData("20.11111111.2", "20111111112")]
    [InlineData("20111111112", "20111111112")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeCuil_LeavesDigitsOnly_AndEmptyIsAbsent(string? typed, string? expected)
    {
        Assert.Equal(expected, PartyIdentifiers.NormalizeCuil(typed));
    }

    /// <summary>Letters are not stripped, or "abc" would read as absent and be accepted.</summary>
    [Fact]
    public void NormalizingKeepsLetters_SoTheyAreRefusedAndNotTakenForAbsent()
    {
        Assert.Equal("abc", PartyIdentifiers.NormalizeDni("abc"));
        Assert.False(PartyIdentifiers.IsValidDni(PartyIdentifiers.NormalizeDni("abc")));
        Assert.False(PartyIdentifiers.IsValidCuil(PartyIdentifiers.NormalizeCuil("abc")));
    }

    /// <summary>Spec Test 5: DNI length bounds.</summary>
    [Theory]
    [InlineData("12345", false)]
    [InlineData("123456", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)]
    [InlineData("1234567a", false)]
    [InlineData("١٢٣٤٥٦٧", false)]
    public void IsValidDni_TakesSixToEightDigits(string normalized, bool expected)
    {
        Assert.Equal(expected, PartyIdentifiers.IsValidDni(normalized));
    }

    /// <summary>Spec Test 5: the check digit, including 11 meaning 0.</summary>
    [Theory]
    [InlineData("20111111112", true)]
    [InlineData("20222222223", true)]
    [InlineData("27333333339", true)]
    [InlineData("20555555556", true)]
    [InlineData("20000000060", true)]
    [InlineData("20111111113", false)]
    [InlineData("20000000061", false)]
    [InlineData("2011111111", false)]
    [InlineData("201111111120", false)]
    [InlineData("2011111111a", false)]
    public void IsValidCuil_ChecksLengthAndCheckDigit(string normalized, bool expected)
    {
        Assert.Equal(expected, PartyIdentifiers.IsValidCuil(normalized));
    }

    /// <summary>Spec Test 5: when the formula gives 10 no check digit is valid, whichever is written.</summary>
    [Theory]
    [InlineData("2000000001")]
    [InlineData("2744444444")]
    public void IsValidCuil_RefusesEveryDigit_WhenTheFormulaGivesTen(string firstTenDigits)
    {
        Assert.All(
            Enumerable.Range(0, 10),
            digit => Assert.False(PartyIdentifiers.IsValidCuil($"{firstTenDigits}{digit}")));
    }

    /// <summary>Spec Test 5: each refusal in order, and Test 6: neither identifier is refused until accepted.</summary>
    [Theory]
    [InlineData(" ", "Pérez", "30123456", null, false, PartyRefusal.NameMissing)]
    [InlineData("Juan", null, "30123456", null, false, PartyRefusal.NameMissing)]
    [InlineData("Juan", "Pérez", "12345", null, false, PartyRefusal.DniInvalid)]
    [InlineData("Juan", "Pérez", "abc", null, true, PartyRefusal.DniInvalid)]
    [InlineData("Juan", "Pérez", null, "20-11111111-3", false, PartyRefusal.CuilInvalid)]
    [InlineData("Juan", "Pérez", null, null, false, PartyRefusal.NoIdentifierNotAccepted)]
    [InlineData("Juan", "Pérez", " ", "-", false, PartyRefusal.NoIdentifierNotAccepted)]
    [InlineData("Juan", "Pérez", null, null, true, null)]
    [InlineData("Juan", "Pérez", "30.123.456", null, false, null)]
    [InlineData("Juan", "Pérez", null, "20-11111111-2", false, null)]
    public void Check_GivesTheFirstRefusal(
        string? first, string? last, string? dni, string? cuil, bool accept, PartyRefusal? expected)
    {
        Assert.Equal(expected, PartyDraftRules.Check(new PartyDraft(first, last, dni, cuil), accept));
    }

    [Fact]
    public void Normalized_TrimsNames_NormalizesIdentifiers_AndBlankContactIsAbsent()
    {
        var draft = new PartyDraft(" Juan ", " Pérez ", "30.123.456", "20-11111111-2", " ", " a@example.com ", null);

        Assert.Equal(
            new PartyDraft("Juan", "Pérez", "30123456", "20111111112", null, "a@example.com", null),
            draft.Normalized());
    }

    /// <summary>Spec Requirement 2: each refusal has its own text, and the "taken" ones name the holder.</summary>
    [Fact]
    public void EveryRefusal_HasItsOwnText_AndTheTakenOnesNameTheHolder()
    {
        var texts = Enum.GetValues<PartyRefusal>().Select(refusal => refusal.OperatorText("Juan Pérez")).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Contains("Juan Pérez", PartyRefusal.DniTaken.OperatorText("Juan Pérez"));
        Assert.Contains("Juan Pérez", PartyRefusal.CuilTaken.OperatorText("Juan Pérez"));
    }
}
