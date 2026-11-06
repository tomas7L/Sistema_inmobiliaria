using System.Globalization;
using Inmobiliaria.Presentation.Indices;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Test 31 (Requirement 12): the agency's number format, read exactly.</summary>
public class IndexLevelParserTests
{
    [Theory]
    [InlineData("1.915.878,76", "1915878.76")]
    [InlineData("1915878,76", "1915878.76")]
    [InlineData("9.440", "9440")]
    [InlineData("9440", "9440")]
    [InlineData("9193,5678", "9193.5678")]
    [InlineData("25,08", "25.08")]
    [InlineData("  25,08 ", "25.08")]
    [InlineData("0,000001", "0.000001")]
    public void TheAgencysFormat_IsReadExactly(string text, string expected)
    {
        Assert.True(IndexLevelParser.TryParse(text, out var level));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1915878.76")]
    [InlineData("12.5")]
    [InlineData("1.91")]
    [InlineData("1.915.87")]
    [InlineData("1,915,878.76")]
    [InlineData("1,2,3")]
    [InlineData("1,")]
    [InlineData(",5")]
    [InlineData("-5")]
    [InlineData("٣")]
    [InlineData("99999999999999999999999999999999")]
    public void AnythingElse_IsRefused_RatherThanGuessed(string? text)
    {
        Assert.False(IndexLevelParser.TryParse(text, out _));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-AR")]
    [InlineData("de-DE")]
    public void TheMachinesCulture_ChangesNothing(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.True(IndexLevelParser.TryParse("1.915.878,76", out var level));
            Assert.Equal(1_915_878.76m, level);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
