using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// revocar-permisos-api spec Test 5: on a database where Supabase's API roles do not exist, every migration, the revocation
/// included, applies without error. Roles belong to the server and every other test needs them, so this test starts a plain
/// container of its own (design D11).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationWithoutSupabaseRolesTests
{
    private readonly PostgresFixture _fixture;

    public MigrationWithoutSupabaseRolesTests(PostgresFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Test5_WithoutTheApiRoles_EveryMigrationApplies_AndNoDefaultPrivilegeNamesThem()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var container = new PostgreSqlBuilder("postgres:17.6")
            .WithDatabase("plain")
            .WithUsername("plain_owner")
            .WithPassword("plain_owner")
            .Build();
        await container.StartAsync();

        await using var context = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(container.GetConnectionString()).Options);
        var apiRoles = PostgresFixture.SupabaseApiRoles.ToArray();
        Assert.Equal(0, await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_roles WHERE rolname = ANY({apiRoles})")
            .SingleAsync());

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_RevokeSupabaseApiRoles", StringComparison.Ordinal));
        Assert.Equal(0, await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_default_acl d, aclexplode(d.defaclacl) a WHERE a.grantee <> 0 AND pg_get_userbyid(a.grantee) = ANY({apiRoles})")
            .SingleAsync());
    }
}
