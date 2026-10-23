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
}
