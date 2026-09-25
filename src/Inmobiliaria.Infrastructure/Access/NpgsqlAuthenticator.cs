using Inmobiliaria.Domain.Access;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Login IS the connection attempt (spec "Login Is the Connection Attempt"; design Decision
/// 1's data flow). Opens exactly one physical connection to read identity (spec test 2), then
/// hands the underlying <see cref="NpgsqlDataSource"/> to <see cref="SessionDbContextFactory"/>
/// — nothing else in the process can produce one (spec test 6).
/// </summary>
public sealed class NpgsqlAuthenticator : IAuthenticator
{
    private readonly ConnectionEndpoint _endpoint;

    public NpgsqlAuthenticator(ConnectionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoint = endpoint;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string username, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = _endpoint.Host,
            Port = _endpoint.Port,
            Database = _endpoint.Database,
            SslMode = _endpoint.SslMode,
            Username = SupavisorUsername.For(username, _endpoint.ProjectRef),
            Password = password,
        }.ConnectionString;

        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

        NpgsqlConnection connection;
        try
        {
            // The one and only physical connection this method ever opens (spec test 2).
            connection = await dataSource.OpenConnectionAsync(ct);
        }
        catch (PostgresException ex) when (
            ex.SqlState == PostgresErrorCodes.InvalidPassword ||
            ex.SqlState == PostgresErrorCodes.InvalidAuthorizationSpecification)
        {
            // Wrong password and unknown username fail identically here — neither case is
            // told apart from the other (spec "Wrong Password Is Rejected Without Revealing
            // Whether The Username Exists").
            await dataSource.DisposeAsync();
            return new AuthenticationResult.Rejected();
        }

        await using (connection)
        {
            var (roleName, isAdmin, isEmpleado) = await ReadIdentityAsync(connection, ct);

            if (!isAdmin && !isEmpleado)
            {
                await dataSource.DisposeAsync();
                return new AuthenticationResult.NotProvisioned(
                    "This role is not a member of either application role.");
            }

            var appUser = await LoadAppUserAsync(connection, roleName, ct);

            if (appUser is null)
            {
                await dataSource.DisposeAsync();
                return new AuthenticationResult.NotProvisioned(
                    "No app_users row exists for this account.");
            }

            if (!appUser.IsActive)
            {
                await dataSource.DisposeAsync();
                return new AuthenticationResult.NotProvisioned(
                    "This account has been deactivated.");
            }

            // Precedence when both memberships return true: Admin wins (design Decision 2).
            var role = isAdmin ? UserRole.Admin : UserRole.Empleado;
            var session = new UserSession(appUser, role);
            var factory = new SessionDbContextFactory(dataSource);

            return new AuthenticationResult.Success(session, factory);
        }
    }

    private static async Task<(string RoleName, bool IsAdmin, bool IsEmpleado)> ReadIdentityAsync(
        NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT current_user::text AS role_name,
                   pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER') AS is_admin,
                   pg_has_role(current_user, 'inmobiliaria_empleado', 'MEMBER') AS is_empleado
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task<AppUser?> LoadAppUserAsync(
        NpgsqlConnection connection, string username, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, username, display_name, is_active, must_change_password " +
            "FROM app_users WHERE username = @username";
        command.Parameters.AddWithValue("username", username);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new AppUser(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }
}
