using Inmobiliaria.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Calls the migration's <c>app_create_login_role</c>, <c>app_set_role_password</c> and
/// <c>app_set_role_login</c> functions (design Decisions 3 and 9) with real EF/Npgsql
/// parameters — the same escaping guarantee <see cref="PostgresPasswordService"/> already
/// relies on. Every method here validates its non-secret inputs (a target username's shape,
/// that the target row exists and is in the expected state) BEFORE binding a password to any
/// call, because a FAILED call to one of these functions could otherwise log that password in
/// full: the live project logs failing statements by default
/// (<c>log_min_error_statement = error</c>) with unbounded bind-parameter length
/// (<c>log_parameter_max_length = -1</c>) — see tasks.md task 3.13. A call that fails only
/// because of something these checks already ruled out never reaches the server with the
/// secret bound.
/// </summary>
public sealed class PostgresUserProvisioning : IUserProvisioning
{
    /// <summary>
    /// The ONLY group role this type ever grants a newly created login role — see
    /// <see cref="IUserProvisioning.CreateUserAsync"/>'s remarks for why creating an Admin
    /// in-app is out of scope.
    /// </summary>
    private const string EmpleadoGroupRole = "inmobiliaria_empleado";

    private readonly ISessionDbContextFactory _sessionFactory;

    public PostgresUserProvisioning(ISessionDbContextFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task<Guid> CreateUserAsync(
        string username, string password, string displayName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // Same rule AppUser's own constructor enforces (CHECK (username = lower(username))).
        // Checked here too, and FIRST, so a mixed-case username is rejected before the
        // password is ever bound to app_create_login_role's call below — not merely at the
        // AppUser construction that happens after it.
        if (!string.Equals(username, username.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Username must be lowercase; it equals the PostgreSQL role name.", nameof(username));
        }

        await using var context = _sessionFactory.Create();

        // Cheap, password-free pre-check (spec test 12): a username already assigned to
        // ANYONE — active or deactivated — is refused here, before app_create_login_role
        // (and the password it would bind) is ever called. Usernames are never reused (spec
        // "Deactivate, Never Delete"), so this check alone is the whole of that guarantee on
        // the app_users side; the matching PostgreSQL role, if one already exists, makes
        // CREATE ROLE fail on its own (42710) with no password bound to that failure either,
        // since app_create_login_role's arguments are still exactly these two non-conflicting
        // values up to that point.
        var usernameTaken = await context.AppUsers.AnyAsync(u => u.Username == username, ct);
        if (usernameTaken)
        {
            throw new InvalidOperationException(
                $"The username '{username}' is already assigned and can never be reused (spec " +
                "\"Deactivate, Never Delete\").");
        }

        // One transaction: the login role and the app_users row land together, or neither
        // does (spec test 9's forced-rollback proof). PostgreSQL's DDL is transactional, so a
        // CREATE ROLE inside this same transaction rolls back exactly like any other statement
        // if the row insert below fails.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_create_login_role({username}, {password}, {EmpleadoGroupRole})", ct);

        // MustChangePassword = true: this password was chosen by the Admin provisioning the
        // account, not by the person who will use it (spec "A Password Set by Another Person
        // Must Be Changed Before Anything Else"; spec test 15's database half).
        var user = new AppUser(Guid.CreateVersion7(), username, displayName, isActive: true, mustChangePassword: true);
        context.AppUsers.Add(user);
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return user.Id;
    }

    /// <summary>
    /// Resets a DIFFERENT user's password. <b>Discovered running this slice's own tests, not
    /// previously documented anywhere in this change:</b> PostgreSQL restricts who may
    /// <c>ALTER ROLE ... PASSWORD</c> on an EXISTING role to that role's own creator (or a
    /// superuser) — holding <c>CREATEROLE</c> and even <c>ADMIN OPTION</c> on the group role is
    /// not, by itself, enough for a role you did not create. In this application every user is
    /// created by <see cref="CreateUserAsync"/>, called by whichever Admin is logged in at the
    /// time, so in practice this method only succeeds when the CALLING Admin is the same one who
    /// originally provisioned <paramref name="username"/> — consistent with this project having
    /// exactly one Admin (design.md, Decision 8's own reasoning), but worth stating plainly
    /// rather than leaving as a silent assumption.
    /// </summary>
    public async Task ResetPasswordAsync(string username, string newPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        await using var context = _sessionFactory.Create();

        // Load and validate BEFORE the password is bound to anything: an unknown or already
        // deactivated target turns into a clean application-level rejection here, never a
        // failed app_set_role_password call carrying the new password in its bind parameters.
        var user = await context.AppUsers.SingleOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            throw new ArgumentException($"No user named '{username}' exists.", nameof(username));
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException(
                $"'{username}' is deactivated; a deactivated user's password cannot be reset.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // app_set_role_password is NOT SECURITY DEFINER (design Decision 3): it runs as the
        // calling Admin, whose CREATEROLE (granted only by the bootstrap runbook — task 3.7)
        // is what lets ALTER ROLE reach a role other than the caller's own — but only a role
        // that same Admin created (see this method's remarks above).
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_set_role_password({username}, {newPassword})", ct);

        // Spec test 16: an Admin reset always raises the requirement again, even if the
        // affected user had nothing pending before.
        user.RequirePasswordChange();
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Deactivates a user. Same caller constraint as <see cref="ResetPasswordAsync"/>: PostgreSQL
    /// only lets a role's creator (or a superuser) <c>ALTER ROLE ... NOLOGIN</c> it, so this only
    /// succeeds when the calling Admin is the one who originally provisioned
    /// <paramref name="username"/>.
    /// </summary>
    public async Task DeactivateAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await using var context = _sessionFactory.Create();

        var user = await context.AppUsers.SingleOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            throw new ArgumentException($"No user named '{username}' exists.", nameof(username));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // ALTER ROLE ... NOLOGIN, never DROP ROLE (spec "Deactivate, Never Delete") — the row
        // and the role both survive, so every existing FK reference keeps resolving.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_set_role_login({username}, {false})", ct);

        user.Deactivate();
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }
}
