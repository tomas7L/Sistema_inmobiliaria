using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Wraps the single <see cref="NpgsqlDataSource"/> a successful login produced (design
/// Decision 1's data flow). Every <see cref="InmobiliariaDbContext"/> this creates shares that
/// data source's connection pool, so every later query re-authenticates through the session's
/// already-open handshake — never a fresh credential prompt.
/// </summary>
public sealed class SessionDbContextFactory : ISessionDbContextFactory
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SessionPassword? _password;
    private bool _disposed;

    /// <param name="password">
    /// The password <paramref name="dataSource"/> reads for each new physical connection, when it was built
    /// with one (<see cref="NpgsqlAuthenticator"/>); null for a data source with a fixed password.
    /// </param>
    public SessionDbContextFactory(NpgsqlDataSource dataSource, SessionPassword? password = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        _password = password;
    }

    public void UsePassword(string newPassword)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_password is null)
        {
            throw new InvalidOperationException("This session's data source has a fixed password; it cannot be replaced.");
        }

        _password.Replace(newPassword);
    }

    public InmobiliariaDbContext Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        return new InmobiliariaDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _dataSource.DisposeAsync();
    }
}
