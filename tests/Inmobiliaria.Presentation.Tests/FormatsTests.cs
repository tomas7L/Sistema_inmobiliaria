using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>How a typed amount is read (S3b review R3-dot-decimal-misread): an ambiguous dot is refused, never multiplied.</summary>
public class FormatsTests
{
    [Theory]
    [InlineData("598.260", 598_260)]
    [InlineData("598260", 598_260)]
    [InlineData("1.200", 1_200)]
    [InlineData("1250,80", 1_250.80)]
    [InlineData("1.250,80", 1_250.80)]
    [InlineData("-500", -500)]
    [InlineData(" 4000 ", 4_000)]
    public void AnAmountInTheAgencysConvention_IsRead(string text, double expected)
    {
        Assert.True(Formats.TryParseMoney(text, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("1250.80")]
    [InlineData("12.5")]
    [InlineData("1.2345")]
    [InlineData("598,260.00")]
    [InlineData("cuatro mil")]
    public void AnAmbiguousOrForeignAmount_IsRefused(string text) =>
        Assert.False(Formats.TryParseMoney(text, out _));

    [Fact]
    public void AnEmptyField_IsNoAmount()
    {
        Assert.True(Formats.TryParseMoney("  ", out var amount));
        Assert.Null(amount);
    }

    // 12.3d: every amount on a screen and on the printed recibo carries the peso sign; a debt keeps its minus first.
    [Theory]
    [InlineData(598_260, "$ 598.260")]
    [InlineData(1_250.80, "$ 1.250,8")]
    [InlineData(0, "$ 0")]
    [InlineData(-500, "-$ 500")]
    public void AnAmount_IsWrittenWithThePesoSign(double amount, string expected) =>
        Assert.Equal(expected, Formats.Money((decimal)amount));

    // A figure written back into a field the operator edits carries no sign, so the field still reads as typed.
    [Fact]
    public void AFigureForAField_HasNoPesoSign() =>
        Assert.Equal("1.250,8", Formats.Number(1_250.80m));

    // Tomás 2026-11-16: an amount typed without its dots gets them when the field is left; nothing else changes.
    [Theory]
    [InlineData("1611000", "1.611.000")]
    [InlineData(" 1611000 ", "1.611.000")]
    [InlineData("1250,80", "1.250,80")]
    [InlineData("1234567,5", "1.234.567,5")]
    [InlineData("-500000", "-500.000")]
    [InlineData("1.611.000", "1.611.000")]
    [InlineData("500", "500")]
    public void ATypedAmount_IsTidiedToTheAgencysConvention_KeepingEveryDecimal(string typed, string tidy) =>
        Assert.Equal(tidy, Formats.TidyMoney(typed));

    [Theory]
    [InlineData("1250.80")]
    [InlineData("cuatro mil")]
    [InlineData("")]
    [InlineData("  ")]
    public void WhatIsNotAnAmount_IsLeftAsTyped(string typed) =>
        Assert.Equal(typed, Formats.TidyMoney(typed));
}
