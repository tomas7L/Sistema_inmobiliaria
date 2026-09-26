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

    public async Task ChangeOwnPasswordAsync(
        string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        // Spec test 17 ("Re-entering the provisional password is rejected"): an ordinal
        // comparison, checked BEFORE newPassword is ever bound to app_set_role_password — this
        // rejection never reaches the database at all, so it carries none of the residual
        // log_parameter_max_length risk task 3.13 flags (a rejected call here is not a FAILED
        // SQL statement; it is never issued). This method does not itself re-verify that
        // currentPassword is the genuine live password — the caller already holds an
        // authenticated session, and design Decision 6 assigns that separate proof (the
        // voluntary-change dialog's own extra confirmation, which "proves it by opening a
        // throwaway connection with it") to the UI layer, PR 5's ChangePasswordViewModel.
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The new password must be different from the current one.");
        }

        await using var context = _sessionFactory.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // EF parameterizes every interpolated hole here (@p0, @p1); `format('%I','%L')` inside
        // app_set_role_password escapes server-side. Neither value ever touches a client-built
        // SQL string — there is no parse boundary for an injection payload to cross (spec
        // tests 20, 21).
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_set_role_password({_session.Username}, {newPassword})", ct);

        // Any self-service change — voluntary (spec test 18) or completing a forced one (spec
        // tests 15, 16) — clears the pending requirement (spec "Setting It MUST Clear The
        // Requirement"). The database function is SECURITY DEFINER but scoped to exactly the
        // caller's own row via current_user (design Decision 6); idempotent when already false.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_clear_must_change_password()", ct);

        await transaction.CommitAsync(ct);

        _session.ClearMustChangePassword();
    }
}
