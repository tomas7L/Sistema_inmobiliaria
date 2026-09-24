using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

public class AppUserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNullOrWhitespaceUsername_IsRejected(string? username)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for a null
        // argument specifically (a subclass of ArgumentException) and plain ArgumentException
        // for empty/whitespace — ThrowsAny accepts either, since both are the same guard.
        Assert.ThrowsAny<ArgumentException>(() => new AppUser(Guid.NewGuid(), username!, "Maria Lopez"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNullOrWhitespaceDisplayName_IsRejected(string? displayName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AppUser(Guid.NewGuid(), "maria", displayName!));
    }

    [Theory]
    [InlineData("Maria")]
    [InlineData("MARIA")]
    [InlineData("mariaL")]
    public void Constructor_WithMixedCaseUsername_IsRejected(string username)
    {
        // The database carries CHECK (username = lower(username)) and the username equals
        // the PostgreSQL rolname, so mixed case is caught here instead of arriving as an
        // opaque constraint violation at save time. Rejected, never silently lowercased.
        Assert.Throws<ArgumentException>(() => new AppUser(Guid.NewGuid(), username, "Maria Lopez"));
    }

    [Fact]
    public void Constructor_DefaultsToActiveWithNoPendingPasswordChange()
    {
        // No role or password column exists to default (design Decision 5); only these two
        // flags have defaults, matching the column defaults design Decision 8 names for
        // `is_active` and `must_change_password`.
        var user = new AppUser(Guid.NewGuid(), "maria", "Maria Lopez");

        Assert.True(user.IsActive);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void Constructor_SetsEveryProvidedValue()
    {
        var id = Guid.NewGuid();

        var user = new AppUser(id, "sofia", "Sofia Martinez", isActive: false, mustChangePassword: true);

        Assert.Equal(id, user.Id);
        Assert.Equal("sofia", user.Username);
        Assert.Equal("Sofia Martinez", user.DisplayName);
        Assert.False(user.IsActive);
        Assert.True(user.MustChangePassword);
    }
}
