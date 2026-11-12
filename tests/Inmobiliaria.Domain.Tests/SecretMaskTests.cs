using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-usuarios spec Test 13 (the mask half) and design D10 / Finding F5.</summary>
public class SecretMaskTests
{
    [Fact]
    public void ABareSecret_IsMasked_InAnyCase_WithOrWithoutItsSpaces_AndThePatternsStillApply()
    {
        var masked = SecretMask.Mask(
            "login failed for abcd efgh ijkl mnop, ABCDEFGHIJKLMNOP and Password=hunter2", "abcd efgh ijkl mnop");

        Assert.Equal("login failed for ***, *** and Password=***", masked);
    }

    [Fact]
    public void AConnectionStringPassword_IsMasked_AndTheRestKept()
    {
        var masked = SecretMask.Mask("Host=db.internal;Username=nicolas;Password=hunter2;SSL Mode=Require");

        Assert.DoesNotContain("hunter2", masked);
        Assert.Contains("Password=***", masked);
        Assert.Contains("Host=db.internal", masked);
    }

    [Fact]
    public void AQuotedAssignment_IsMasked()
    {
        Assert.DoesNotContain("other secret", SecretMask.Mask("password = 'other secret' rejected"));
    }

    [Theory]
    [InlineData("SQL statement \"ALTER ROLE sofia PASSWORD 'lago-sol-pan-48017'\"", "lago-sol-pan-48017")]
    [InlineData("SQL statement \"CREATE ROLE sofia LOGIN PASSWORD 'lago-sol-pan-48017'\"", "lago-sol-pan-48017")]
    [InlineData("ALTER ROLE \"maria\" PASSWORD 'o''brien55'", "brien55")]
    [InlineData("ALTER ROLE maria PASSWORD E'back\\\\slash'", "slash")]
    [InlineData("alter role maria password 'lowercase-too'", "lowercase-too")]
    public void APasswordClauseInSql_IsMasked(string text, string secret)
    {
        var masked = SecretMask.Mask(text);

        Assert.DoesNotContain(secret, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void TheStatementAroundTheClause_IsKept()
    {
        var masked = SecretMask.Mask("SQL statement \"ALTER ROLE sofia PASSWORD 'x1'\" at EXECUTE");

        Assert.Contains("ALTER ROLE sofia PASSWORD ***", masked);
        Assert.EndsWith("\" at EXECUTE", masked);
    }

    [Theory]
    [InlineData("ALTER ROLE sofia PASSWORD 'lago-sol-pa", "lago-sol-pa")]
    [InlineData("ALTER ROLE sofia PASSWORD E'lago-sol-pa", "lago-sol-pa")]
    public void ALiteralCutOffBeforeItsClosingQuote_IsMasked(string text, string secret)
    {
        Assert.DoesNotContain(secret, SecretMask.Mask(text));
    }

    [Fact]
    public void ACutOffLiteral_DoesNotSwallowTheNextLine()
    {
        var masked = SecretMask.Mask("ALTER ROLE sofia PASSWORD 'cut-off\n   at Npgsql.Internal.Read()");

        Assert.DoesNotContain("cut-off", masked);
        Assert.Contains("at Npgsql.Internal.Read()", masked);
    }

    [Fact]
    public void TextWithoutAPassword_IsUnchanged()
    {
        const string text = "42501: permission denied to alter role";
        Assert.Equal(text, SecretMask.Mask(text));
    }
}
