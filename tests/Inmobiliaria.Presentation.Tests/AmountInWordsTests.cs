using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec test 24b and Decision 36, design D14: the amount in words on every recibo.</summary>
public class AmountInWordsTests
{
    [Theory]
    [InlineData(47_860, "cuarenta y siete mil ochocientos sesenta")]
    [InlineData(507_000, "quinientos siete mil")]
    [InlineData(598_260, "quinientos noventa y ocho mil doscientos sesenta")]
    [InlineData(21_000, "veintiún mil")]
    [InlineData(31_001, "treinta y un mil uno")]
    [InlineData(1_000_000, "un millón")]
    [InlineData(100, "cien")]
    [InlineData(101, "ciento uno")]
    [InlineData(0, "cero")]
    public void TheSpecsAmounts_ReadAsTheSpecSays(long amount, string words) =>
        Assert.Equal(words, AmountInWords.Words(amount));

    [Theory]
    [InlineData(1, "uno")]
    [InlineData(15, "quince")]
    [InlineData(16, "dieciséis")]
    [InlineData(21, "veintiuno")]
    [InlineData(22, "veintidós")]
    [InlineData(1_000, "mil")]
    [InlineData(1_001, "mil uno")]
    [InlineData(101_000, "ciento un mil")]
    [InlineData(200_000, "doscientos mil")]
    [InlineData(2_000_000, "dos millones")]
    [InlineData(21_000_000, "veintiún millones")]
    [InlineData(1_000_000_000, "mil millones")]
    [InlineData(999_999_999_999, "novecientos noventa y nueve mil novecientos noventa y nueve millones novecientos noventa y nueve mil novecientos noventa y nueve")]
    public void TheApocopeAndTheLargeGroups_FollowSpanish(long amount, string words) =>
        Assert.Equal(words, AmountInWords.Words(amount));

    [Fact]
    public void TheLine_CarriesSonPesos_AndTheCentavosSuffix() =>
        Assert.Equal("Son Pesos quinientos noventa y ocho mil doscientos sesenta con 00/100.-", AmountInWords.Line(598_260m));

    [Fact]
    public void ANegativeTotal_IsPrefixedMenos() =>
        Assert.Equal("Son Pesos menos cuarenta y siete mil ochocientos sesenta con 00/100.-", AmountInWords.Line(-47_860m));

    [Fact]
    public void Centavos_AreWrittenAsTwoDigits() =>
        Assert.Equal("Son Pesos cien con 50/100.-", AmountInWords.Line(100.50m));

    [Fact]
    public void AnAmountBeyondTheBound_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Words(1_000_000_000_000));
}
