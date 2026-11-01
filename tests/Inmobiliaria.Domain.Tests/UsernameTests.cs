using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-usuarios spec Test 4 and Decision 8: how a typed username is read and which ones are valid.</summary>
public class UsernameTests
{
    [Theory]
    [InlineData("sofia", "sofia")]
    [InlineData("Sofia", "sofia")]
    [InlineData("  sofia ", "sofia")]
    [InlineData("NUÑEZ", "nuñez")]
    [InlineData("Nuñez ", "nuñez")]
    // "n" followed by U+0303 (combining tilde): the same username as the precomposed "ñ".
    [InlineData("nuñez", "nuñez")]
    public void Normalize_TrimsLowercasesAndComposes(string typed, string expected)
    {
        Assert.Equal(expected, Username.Normalize(typed));
    }

    [Theory]
    [InlineData("sofia")]
    [InlineData("nuñez")]
    [InlineData("acuña")]
    [InlineData("ana.perez")]
    [InlineData("juan_2")]
    [InlineData("abc")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234")]
    public void IsValid_AcceptsLettersEnyeDigitsDotAndUnderscore(string username)
    {
        Assert.True(Username.IsValid(username));
    }

    [Theory]
    [InlineData("josé")]
    [InlineData("jose perez")]
    [InlineData("2juan")]
    [InlineData(".juan")]
    [InlineData("_juan")]
    [InlineData("juan-perez")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    [InlineData("Sofia")]
    [InlineData("")]
    public void IsValid_RefusesEverythingElse(string username)
    {
        Assert.False(Username.IsValid(username));
    }

    [Fact]
    public void ANormalizedEnye_IsValid()
    {
        Assert.True(Username.IsValid(Username.Normalize("Nuñez")));
    }
}
