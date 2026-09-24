using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

public class UserSessionTests
{
    [Fact]
    public void Constructor_CopiesIdentityFromAppUserAndRoleFromLogin()
    {
        var user = new AppUser(Guid.NewGuid(), "maria", "Maria Lopez");

        var session = new UserSession(user, UserRole.Admin);

        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(user.Username, session.Username);
        Assert.Equal(user.DisplayName, session.DisplayName);
        Assert.Equal(UserRole.Admin, session.Role);
    }

    [Fact]
    public void Constructor_WithNullAppUser_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UserSession(null!, UserRole.Empleado));
    }

    [Fact]
    public void ClearMustChangePassword_FlipsFlagFromTrueToFalse()
    {
        var user = new AppUser(Guid.NewGuid(), "sofia", "Sofia Martinez", mustChangePassword: true);
        var session = new UserSession(user, UserRole.Empleado);

        session.ClearMustChangePassword();

        Assert.False(session.MustChangePassword);
    }

    [Fact]
    public void ClearMustChangePassword_CalledTwice_IsIdempotent()
    {
        var user = new AppUser(Guid.NewGuid(), "sofia", "Sofia Martinez", mustChangePassword: true);
        var session = new UserSession(user, UserRole.Empleado);

        session.ClearMustChangePassword();
        session.ClearMustChangePassword();

        Assert.False(session.MustChangePassword);
    }
}
