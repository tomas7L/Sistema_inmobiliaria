using Inmobiliaria.Domain.Notices;

namespace Inmobiliaria.Domain.Tests;

/// <summary>notificaciones design §2.1 and spec Requirement 6: the credentials never print their password.</summary>
public class MailboxCredentialsTests
{
    [Fact]
    public void ToString_PrintsTheAddressOnly()
    {
        var credentials = new MailboxCredentials("dellago.avisos@gmail.com", "abcd efgh ijkl mnop");

        Assert.Equal("dellago.avisos@gmail.com", credentials.ToString());
        Assert.DoesNotContain("abcd", $"{credentials}");
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("a@b.c", " ")]
    public void ABlankAddressOrPassword_IsRefused(string address, string password) =>
        Assert.ThrowsAny<ArgumentException>(() => new MailboxCredentials(address, password));
}
