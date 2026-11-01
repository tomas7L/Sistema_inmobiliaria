using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-usuarios spec Test 18, Requirement 9 and Decisions P6 and 14.</summary>
public class PasswordRuleTests
{
    private const string Current = "TestRolePassword123!";

    [Fact]
    public void TheCurrentPassword_IsRefusedAsSameAsCurrent()
    {
        Assert.Equal(PasswordRefusal.SameAsCurrent, PasswordRule.Check("sofia", Current, Current));
    }

    [Fact]
    public void SevenCharacters_AreTooShort()
    {
        Assert.Equal(PasswordRefusal.TooShort, PasswordRule.Check("sofia", Current, "1234567"));
    }

    [Theory]
    [InlineData("girasoles")]
    [InlineData("12345678")]
    [InlineData("abcdefgh")]
    public void EightCharacters_WithNoCompositionDemanded_AreAccepted(string newPassword)
    {
        Assert.Null(PasswordRule.Check("sofia", Current, newPassword));
    }

    [Theory]
    [InlineData("sofia1234")]
    [InlineData("Sofia1234")]
    [InlineData("SOFIA1234")]
    public void TheUsername_InAnyCase_IsRefused(string newPassword)
    {
        Assert.Equal(PasswordRefusal.EqualsUsername, PasswordRule.Check("sofia1234", Current, newPassword));
    }

    [Fact]
    public void Spaces_AreAcceptedAndCount()
    {
        Assert.Null(PasswordRule.Check("sofia", Current, "la casa del lago "));
        Assert.Null(PasswordRule.Check("sofia", Current, "a b c d "));
    }

    [Fact]
    public void SixtyFourCharacters_AreAccepted()
    {
        Assert.Null(PasswordRule.Check("sofia", Current, new string('x', 64)));
    }

    [Fact]
    public void Characters_AreCountedAsTyped()
    {
        // "piñatas" with a decomposed "ñ" is eight UTF-16 units but seven characters as typed.
        Assert.Equal(PasswordRefusal.TooShort, PasswordRule.Check("sofia", Current, "pin\u0303atas"));
        Assert.Null(PasswordRule.Check("sofia", Current, "pin\u0303atas1"));
    }

    [Theory]
    [InlineData(PasswordRefusal.SameAsCurrent, "La nueva contraseña debe ser distinta de la actual.")]
    [InlineData(PasswordRefusal.TooShort, "La nueva contraseña debe tener al menos 8 caracteres.")]
    [InlineData(PasswordRefusal.EqualsUsername, "La nueva contraseña no puede ser igual al nombre de usuario.")]
    public void EachRefusal_HasItsOperatorText(PasswordRefusal refusal, string text)
    {
        Assert.Equal(text, refusal.OperatorText());
    }
}
