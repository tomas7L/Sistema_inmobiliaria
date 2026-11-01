using System.Text.RegularExpressions;
using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-usuarios spec Test 11, Requirement 6 and Decision 13; design D6.</summary>
public class ProvisionalPasswordTests
{
    private static readonly Regex Shape = new("^[a-z]{3,6}-[a-z]{3,6}-[a-z]{3,6}-[0-9]{5}$");

    [Fact]
    public void TheWordList_Has256DistinctPlainWords()
    {
        Assert.Equal(256, ProvisionalPassword.Words.Count);
        Assert.Equal(256, ProvisionalPassword.Words.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ProvisionalPassword.Words, word => Assert.Matches("^[a-z]{3,6}$", word));
    }

    [Fact]
    public void AGeneratedPassword_HasTheDictatableShape()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = ProvisionalPassword.Generate();
            Assert.Matches(Shape, password);
            Assert.True(password.Length >= 8);
        }
    }

    [Fact]
    public void AThousandGeneratedPasswords_AreAllDifferent()
    {
        var passwords = Enumerable.Range(0, 1000).Select(_ => ProvisionalPassword.Generate()).ToList();
        Assert.Equal(1000, passwords.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AGeneratedPassword_PassesTheRuleForAChosenPassword()
    {
        // The words never form a valid username on their own: hyphens are not allowed in one.
        for (var i = 0; i < 200; i++)
        {
            Assert.Null(PasswordRule.Check("sofia", "TestRolePassword123!", ProvisionalPassword.Generate()));
        }
    }

    [Fact]
    public void TheChoices_AreThreeWordsThenFiveDigits()
    {
        var calls = new List<int>();
        var password = ProvisionalPassword.Generate(bound =>
        {
            calls.Add(bound);
            return bound - 1;
        });

        Assert.Equal([256, 256, 256, 10, 10, 10, 10, 10], calls);
        var last = ProvisionalPassword.Words[^1];
        Assert.Equal($"{last}-{last}-{last}-99999", password);
    }
}
