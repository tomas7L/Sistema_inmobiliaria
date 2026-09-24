namespace Inmobiliaria.Domain.Access;

/// <summary>
/// One row per personal PostgreSQL login role: the FK target every other table points at
/// when it needs to name a person (design Decision 8). Identity IS the login role —
/// <see cref="Username"/> equals the PostgreSQL <c>rolname</c> — so this entity stores no
/// password and no role column at all. The password lives only in PostgreSQL itself (spec
/// "No Database Credential Stored on Disk, and No Supabase Auth"); the role is read back
/// at login via <c>pg_has_role</c> and never mirrored here (spec "Application Role Read
/// From PostgreSQL, Never Mirrored" / design Decision 5), so nothing on this row can ever
/// disagree with what the database actually grants.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; }

    public string Username { get; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// Paired with <c>ALTER ROLE ... NOLOGIN</c> at deactivation — the login role and this
    /// row are never dropped, only disabled (spec "Deactivate, Never Delete").
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Set whenever a password was chosen by someone other than its owner — at
    /// provisioning or by an Admin reset — and cleared only by that owner's own next
    /// successful password change (spec "A Password Set by Another Person Must Be Changed
    /// Before Anything Else"). This is credential hygiene, not a security boundary: it
    /// confines nothing at the database (design Decision 6).
    /// </summary>
    public bool MustChangePassword { get; private set; }

    public AppUser(
        Guid id,
        string username,
        string displayName,
        bool isActive = true,
        bool mustChangePassword = false)
    {
        // Username equals the PostgreSQL rolname; an empty or whitespace-only value could
        // never correspond to a real login role, so it is rejected here rather than
        // surfacing as an opaque database error later.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // The same reasoning, one step further. `app_users` carries
        // CHECK (username = lower(username)) and the rolname itself is lowercase, so a
        // mixed-case value is not a username this system can ever hold. Rejected rather
        // than silently lowercased: a caller passing "Maria" believes something about the
        // identity it is creating, and quietly changing it would hide that mistake until
        // a login failed for a reason nobody could see.
        if (!string.Equals(username, username.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Username must be lowercase; it equals the PostgreSQL role name.",
                nameof(username));
        }

        // The display name is the one thing Postgres does not already know about this
        // person (design Decision 8); a blank value would defeat the entire reason the
        // column exists.
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        Username = username;
        DisplayName = displayName;
        IsActive = isActive;
        MustChangePassword = mustChangePassword;
    }
}
