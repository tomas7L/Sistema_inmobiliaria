using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Testcontainers-backed Postgres fixture shared by the whole collection (design.md
/// Decision 4). The image is pinned to <c>postgres:17.6</c> — the exact version the
/// provisioned Supabase project reports (openspec/config.yaml, resolved decision
/// <c>postgres-version</c>) — never a floating tag, so the tested engine matches production.
///
/// Calls <see cref="DbContext.Database"/>'s <c>MigrateAsync</c>, never <c>EnsureCreated</c>:
/// <c>EnsureCreated</c> builds the schema straight from the EF model and would silently skip
/// the hand-written trigger SQL added in the migration, letting an invariant test pass
/// against a database that enforces nothing.
///
/// Docker Desktop is not guaranteed on the office Windows PC. If the container fails to
/// start (daemon unreachable), tests using this fixture skip with an explicit reason instead
/// of failing the whole run; the ubuntu-latest CI job always has Docker, so the gate cannot
/// be bypassed on the way to main.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsDockerAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // Both Build() (it validates Docker endpoint availability) and StartAsync() can
        // throw when the daemon is unreachable, so both are guarded — not just the latter.
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder("postgres:17.6")
                .WithDatabase("inmobiliaria_test")
                .WithUsername("inmobiliaria_test")
                .WithPassword("inmobiliaria_test")
                .Build();

            await container.StartAsync();
        }
        catch (Exception ex)
        {
            SkipReason =
                $"Docker is unreachable locally — skipping Testcontainers-backed tests " +
                $"({ex.GetType().Name}: {ex.Message}).";
            return;
        }

        _container = container;
        IsDockerAvailable = true;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Builds a fresh <see cref="InmobiliariaDbContext"/> against the running container.</summary>
    public InmobiliariaDbContext CreateDbContext()
    {
        if (_container is null)
        {
            throw new InvalidOperationException(
                "The Postgres container never started; check IsDockerAvailable/SkipReason " +
                "before calling this.");
        }

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new InmobiliariaDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>One container shared by every test in <see cref="SchemaConstraintTests"/>.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
