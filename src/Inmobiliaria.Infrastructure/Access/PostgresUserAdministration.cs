using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The Usuarios screen's port over the migration's role functions (gestion-de-usuarios design §3.1). Every
/// password goes through <c>app_create_login_role</c> or <c>app_set_role_password</c> as a real parameter and is
/// escaped server-side with <c>format('%L')</c> (spec "Password DDL Is Never Built by String Concatenation").
/// Each call opens its own context, so the screen never shares one between two operations.
/// </summary>
public sealed class PostgresUserAdministration : IUserAdministration
{
    private const string EmpleadoGroupRole = "inmobiliaria_empleado";

    private readonly ISessionDbContextFactory _sessionFactory;
    private readonly IUserSession _session;
    private readonly Func<string> _generatePassword;
    private readonly Func<string, string> _roleNameFor;

    /// <param name="generatePassword">Defaults to <see cref="ProvisionalPassword.Generate()"/>.</param>
    /// <param name="roleNameFor">
    /// The PostgreSQL role name for a normalized username. Through Supavisor it is the username itself, the
    /// default; a test database without Supavisor composes the project suffix into the role name.
    /// </param>
    public PostgresUserAdministration(
        ISessionDbContextFactory sessionFactory,
        IUserSession session,
        Func<string>? generatePassword = null,
        Func<string, string>? roleNameFor = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(session);

        _sessionFactory = sessionFactory;
        _session = session;
        _generatePassword = generatePassword ?? ProvisionalPassword.Generate;
        _roleNameFor = roleNameFor ?? (username => username);
    }

    public async Task<IReadOnlyList<UserListing>> ListAsync(CancellationToken ct = default)
    {
        await using var context = _sessionFactory.Create();

        // Direct membership through pg_auth_members, not pg_has_role: pg_has_role raises an error for a name with
        // no PostgreSQL role, and the list must still show such a row (design D9).
        // Column names follow the model's snake_case convention, which EF also applies to these unmapped rows.
        var rows = await context.Database
            .SqlQuery<ListingRow>(
                $"""
                SELECT u.id, u.username, u.display_name,
                       u.is_active, u.must_change_password,
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles r ON r.oid = m.member JOIN pg_roles g ON g.oid = m.roleid
                               WHERE r.rolname = u.username AND g.rolname = 'inmobiliaria_admin') AS is_admin,
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles r ON r.oid = m.member JOIN pg_roles g ON g.oid = m.roleid
                               WHERE r.rolname = u.username AND g.rolname = 'inmobiliaria_empleado') AS is_empleado
                FROM app_users u
                """)
            .ToListAsync(ct);

        // Admin wins when a role is in both groups, as at login (NpgsqlAuthenticator).
        return rows
            .Select(row => new UserListing(
                row.Id,
                row.Username,
                row.DisplayName,
                row.IsAdmin ? UserRole.Admin : row.IsEmpleado ? UserRole.Empleado : null,
                row.IsActive,
                row.MustChangePassword))
            .ToList();
    }

    public async Task<UserAdminResult> CreateEmpleadoAsync(
        string typedUsername, string displayName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typedUsername);
        ArgumentNullException.ThrowIfNull(displayName);

        var username = Username.Normalize(typedUsername);
        if (!Username.IsValid(username))
        {
            return UserAdminResult.Refused(UserAdminRefusal.UsernameInvalid);
        }

        var name = displayName.Trim();
        if (name.Length == 0)
        {
            return UserAdminResult.Refused(UserAdminRefusal.DisplayNameMissing);
        }

        var roleName = _roleNameFor(username);
        await using var context = _sessionFactory.Create();

        // A username already assigned to anyone, active or deactivated, is never reused (user-registry "Admin
        // Provisions and Deactivates Users From Inside the Application"). Checked before a password even exists.
        if (await context.AppUsers.AnyAsync(u => u.Username == roleName, ct))
        {
            return UserAdminResult.Refused(UserAdminRefusal.UsernameTaken);
        }

        var password = _generatePassword();
        return await RefusingForeignRolesAsync(async () =>
        {
            // One transaction: the login role and the app_users row land together, or neither does. PostgreSQL's
            // DDL is transactional, so CREATE ROLE rolls back with the row insert.
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT app_create_login_role({roleName}, {password}, {EmpleadoGroupRole})", ct);

            context.AppUsers.Add(new AppUser(
                Guid.CreateVersion7(), roleName, name, isActive: true, mustChangePassword: true));
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return UserAdminResult.Issued(new ProvisionalCredential(roleName, password));
        });
    }

    public async Task<UserAdminResult> ResetPasswordAsync(Guid userId, CancellationToken ct = default)
    {
        await using var context = _sessionFactory.Create();

        var (user, refusal) = await LoadEmpleadoAsync(context, userId, ct);
        if (refusal is not null)
        {
            return UserAdminResult.Refused(refusal.Value);
        }

        if (!user!.IsActive)
        {
            return UserAdminResult.Refused(UserAdminRefusal.AlreadyDeactivated);
        }

        var password = _generatePassword();
        return await RefusingForeignRolesAsync(async () =>
        {
            // PostgreSQL lets ALTER ROLE on an existing role succeed only for that role's creator (42501 otherwise,
            // user-registry). The open session of the user being reset is not disturbed.
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT app_set_role_password({user.Username}, {password})", ct);
            user.RequirePasswordChange();
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return UserAdminResult.Issued(new ProvisionalCredential(user.Username, password));
        });
    }

    public async Task<UserAdminResult> DeactivateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var context = _sessionFactory.Create();

        var (user, refusal) = await LoadEmpleadoAsync(context, userId, ct);
        if (refusal is not null)
        {
            return UserAdminResult.Refused(refusal.Value);
        }

        if (!user!.IsActive)
        {
            return UserAdminResult.Success;
        }

        return await RefusingForeignRolesAsync(async () =>
        {
            // NOLOGIN refuses new connections; it does not end a session already open (spec Finding F1).
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT app_set_role_login({user.Username}, {false})", ct);
            user.Deactivate();
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return UserAdminResult.Success;
        });
    }

    public async Task<UserAdminResult> ReactivateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var context = _sessionFactory.Create();

        var (user, refusal) = await LoadEmpleadoAsync(context, userId, ct);
        if (refusal is not null)
        {
            return UserAdminResult.Refused(refusal.Value);
        }

        if (user!.IsActive)
        {
            return UserAdminResult.Refused(UserAdminRefusal.AlreadyActive);
        }

        var password = _generatePassword();
        return await RefusingForeignRolesAsync(async () =>
        {
            // Login and a new password together, or neither: without the new password the person would come back
            // with the one they had when they left (spec Requirement 5).
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT app_set_role_login({user.Username}, {true})", ct);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT app_set_role_password({user.Username}, {password})", ct);
            user.Activate();
            user.RequirePasswordChange();
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return UserAdminResult.Issued(new ProvisionalCredential(user.Username, password));
        });
    }

    /// <summary>Loads the user and refuses anyone who is not an Empleado, before any password is generated.</summary>
    private static async Task<(AppUser? User, UserAdminRefusal? Refusal)> LoadEmpleadoAsync(
        InmobiliariaDbContext context, Guid userId, CancellationToken ct)
    {
        var user = await context.AppUsers.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return (null, UserAdminRefusal.UserNotFound);
        }

        var membership = await context.Database
            .SqlQuery<MembershipRow>(
                $"""
                SELECT EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles r ON r.oid = m.member JOIN pg_roles g ON g.oid = m.roleid
                               WHERE r.rolname = {user.Username} AND g.rolname = 'inmobiliaria_admin') AS is_admin,
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles r ON r.oid = m.member JOIN pg_roles g ON g.oid = m.roleid
                               WHERE r.rolname = {user.Username} AND g.rolname = 'inmobiliaria_empleado') AS is_empleado
                """)
            .SingleAsync(ct);

        return membership.IsEmpleado && !membership.IsAdmin
            ? (user, null)
            : (user, UserAdminRefusal.NotAnEmpleado);
    }

    /// <summary>
    /// PostgreSQL refuses with 42501 both an Empleado calling an Admin-only function and an Admin changing a role
    /// it did not create; the session's role tells the two apart (design D8). The transaction rolls back.
    /// </summary>
    private async Task<UserAdminResult> RefusingForeignRolesAsync(Func<Task<UserAdminResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (IsInsufficientPrivilege(exception))
        {
            return UserAdminResult.Refused(_session.Role == UserRole.Admin
                ? UserAdminRefusal.CreatedByAnotherAdmin
                : UserAdminRefusal.AdminOnly);
        }
    }

    private static bool IsInsufficientPrivilege(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege };

    private sealed class ListingRow
    {
        public Guid Id { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public bool IsActive { get; set; }

        public bool MustChangePassword { get; set; }

        public bool IsAdmin { get; set; }

        public bool IsEmpleado { get; set; }
    }

    private sealed class MembershipRow
    {
        public bool IsAdmin { get; set; }

        public bool IsEmpleado { get; set; }
    }
}
