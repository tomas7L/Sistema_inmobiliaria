using Inmobiliaria.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// <c>app_clear_must_change_password</c> runs as its owner. The query of 2026-11-07 found, on both databases, a
/// <c>search_path</c> without <c>pg_temp</c> and Supabase's API roles able to run it (tasks.md, S8 notes, 8.2).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClearMustChangePasswordHardeningTests
{
    private const string Function = "app_clear_must_change_password()";

    private readonly PostgresFixture _fixture;

    public ClearMustChangePasswordHardeningTests(PostgresFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A search_path that does not name pg_temp searches it first, so a temporary table named like the real one took the update:
    /// the caller's flag stayed set, and a trigger on that table would have run as the owner.
    /// </summary>
    [SkippableFact]
    public async Task TheFunction_ClearsTheRealRow_AndIgnoresATemporaryTableNamedLikeIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var superuser = _fixture.CreateDbContext();
        var rawUsername = $"hard{Guid.NewGuid():N}"[..12];
        var userId = await AccessTestSupport.ProvisionUserAsync(superuser, rawUsername, UserRole.Empleado, mustChangePassword: true);

        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuser, rawUsername);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "CREATE TEMP TABLE app_users (username text, must_change_password boolean)");
        await ExecuteAsync(connection, "INSERT INTO pg_temp.app_users VALUES (session_user, true)");

        await ExecuteAsync(connection, $"SELECT {Function}");

        await using var read = _fixture.CreateDbContext();
        Assert.False((await read.AppUsers.AsNoTracking().SingleAsync(u => u.Id == userId)).MustChangePassword);
        await using var temporary = new NpgsqlCommand("SELECT must_change_password FROM pg_temp.app_users", connection);
        Assert.Equal(true, await temporary.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task TheFunction_StaysDefinerForTheTwoRoles_AndSupabasesApiRolesCannotRunIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();

        Assert.Equal(1, await CountAsync(context,
            "SELECT count(*) FROM pg_proc WHERE proname = 'app_clear_must_change_password' AND prosecdef AND array_to_string(proconfig, ',') = 'search_path=pg_catalog, public, pg_temp'"));
        foreach (var role in new[] { "inmobiliaria_admin", "inmobiliaria_empleado" })
        {
            Assert.Equal(1, await CountAsync(context, $"SELECT count(*) WHERE has_function_privilege('{role}', '{Function}', 'EXECUTE')"));
        }

        foreach (var role in PostgresFixture.SupabaseApiRoles.Append("public"))
        {
            Assert.Equal(0, await CountAsync(context, $"SELECT count(*) WHERE has_function_privilege('{role}', '{Function}', 'EXECUTE')"));
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(Persistence.InmobiliariaDbContext context, string sql)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
