namespace Inmobiliaria.Domain.Access;

/// <summary>
/// The identity and role of the currently authenticated user, held for the lifetime of
/// the process's active session (design Decision 1). Implementations carry no EF Core or
/// Npgsql type — the session's actual database access is a separate infrastructure
/// concern (<c>ISessionDbContextFactory</c>), never reachable from this interface.
/// </summary>
public interface IUserSession
{
    Guid UserId { get; }

    /// <summary>Equals the PostgreSQL <c>rolname</c> this session authenticated as.</summary>
    string Username { get; }

    string DisplayName { get; }

    /// <summary>
    /// Derived once at login from the database's own role membership (spec "Application
    /// Role Read From PostgreSQL, Never Mirrored") and held for this session only — it is
    /// never re-derived mid-session.
    /// </summary>
    UserRole Role { get; }

    bool MustChangePassword { get; }

    /// <summary>
    /// Clears the pending password-change requirement for this session only. Idempotent —
    /// calling it again once already cleared is a no-op, never an error.
    /// </summary>
    void ClearMustChangePassword();
}
