using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The only means of reaching the database for an authenticated session (design Decision 1).
/// Disposing it disposes the underlying <see cref="Npgsql.NpgsqlDataSource"/> and every pooled
/// physical connection with it — call this exactly once, when the session ends.
/// </summary>
public interface ISessionDbContextFactory : IAsyncDisposable
{
    InmobiliariaDbContext Create();

    /// <summary>
    /// Makes every physical connection opened from now on authenticate with <paramref name="newPassword"/>.
    /// Called after the session's own password changed; connections already open are unaffected.
    /// </summary>
    void UsePassword(string newPassword);
}
