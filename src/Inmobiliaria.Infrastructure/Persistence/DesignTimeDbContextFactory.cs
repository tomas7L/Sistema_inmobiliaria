using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` run offline, with no credential present. Reads the
/// connection string from the <c>INMOBILIARIA_DB</c> environment variable; falls back to an
/// offline placeholder that points at nothing real. No connection string here is a secret —
/// see design.md Decision 6. This factory is a development-time path only: the running
/// application never reads <c>INMOBILIARIA_DB</c> and never calls <c>Database.Migrate()</c>
/// (design Decision 7's source guard; users-and-roles task 4.8).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InmobiliariaDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=inmobiliaria_design_time;Username=placeholder;Password=placeholder";

    public InmobiliariaDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("INMOBILIARIA_DB") ?? PlaceholderConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(connectionString);

        return new InmobiliariaDbContext(optionsBuilder.Options);
    }
}
