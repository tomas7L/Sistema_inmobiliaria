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
    private bool _disposed;

    public SessionDbContextFactory(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
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
