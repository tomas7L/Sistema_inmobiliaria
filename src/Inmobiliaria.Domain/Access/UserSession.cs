namespace Inmobiliaria.Domain.Access;

/// <summary>
/// The identity and role of the currently authenticated user, built once at login from
/// the loaded <see cref="AppUser"/> row and the role derived from <c>pg_has_role</c>
/// (design Decision 10). Neither value is re-read from the database again for this
/// session's lifetime.
/// </summary>
public sealed class UserSession : IUserSession
{
    public Guid UserId { get; }

    public string Username { get; }

    public string DisplayName { get; }

    public UserRole Role { get; }

    public bool MustChangePassword { get; private set; }

    public UserSession(AppUser user, UserRole role)
    {
        ArgumentNullException.ThrowIfNull(user);

        UserId = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        Role = role;
        MustChangePassword = user.MustChangePassword;
    }

    /// <summary>
    /// Clears the pending password-change requirement for this session only. Idempotent:
    /// once already <see langword="false"/>, calling this again is a no-op rather than an
    /// error — the forced-change flow (design Decision 10) calls this exactly once on
    /// success, but nothing about the flag's meaning breaks if it were called again.
    /// </summary>
    public void ClearMustChangePassword()
    {
        MustChangePassword = false;
    }
}
