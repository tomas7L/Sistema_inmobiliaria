using Inmobiliaria.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Calls the migration's <c>app_set_role_password</c> function (design Decision 3) with real
/// EF/Npgsql parameters — the value never enters a SQL string on the client, so password DDL
/// is never built by string concatenation (spec "Password DDL Is Never Built by String
/// Concatenation"). The function is deliberately NOT <c>SECURITY DEFINER</c>; it runs as the
/// caller, and PostgreSQL always lets a role change its own password, which is the only thing
/// this type ever asks it to do.
/// </summary>
public sealed class PostgresPasswordService : IPasswordService
{
    private readonly ISessionDbContextFactory _sessionFactory;
    private readonly IUserSession _session;

    public PostgresPasswordService(ISessionDbContextFactory sessionFactory, IUserSession session)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(session);

        _sessionFactory = sessionFactory;
        _session = session;
    }

    public async Task ChangeOwnPasswordAsync(string newPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        await using var context = _sessionFactory.Create();

        // EF parameterizes every interpolated hole here (@p0, @p1); `format('%I','%L')` inside
        // app_set_role_password escapes server-side. Neither value ever touches a client-built
        // SQL string — there is no parse boundary for an injection payload to cross (spec
        // tests 20, 21).
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_set_role_password({_session.Username}, {newPassword})", ct);
    }
}
