using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves design Decision 3's password-DDL escaping and spec Decision 14's honest limit
/// (MustChangePassword confines nothing at the database). All three tests need a real engine.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PasswordDdlTests
{
    private readonly PostgresFixture _fixture;

    public PasswordDdlTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Spec test 19: a pending MustChangePassword does not confine a direct database client — the honest limit, not enforcement.</summary>
    [SkippableFact]
    public async Task PendingMustChangePassword_DoesNotConfineADirectDatabaseClient()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "pendingchange", UserRole.Empleado, mustChangePassword: true);

        // Bypasses the application entirely: her privileges are identical to any other time —
        // MustChangePassword is application bookkeeping only, never a database check.
        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, "pendingchange");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM parties LIMIT 0";
        await command.ExecuteScalarAsync();
    }

    /// <summary>Spec tests 20, 21: each payload becomes the exact literal password, `app_users` is untouched, and the payload authenticates afterward.</summary>
    [SkippableTheory]
    [InlineData("o'brien55")]
    [InlineData("x'; DROP TABLE app_users; --")]
    public async Task PasswordDdl_SetsTheExactLiteralPasswordWithNoInjection(string payload)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var rawUsername = $"payload-{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(superuserContext, rawUsername, UserRole.Empleado);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync(rawUsername, AccessTestSupport.DefaultPassword));

        var passwordService = new PostgresPasswordService(login.Factory, login.Session);
        await passwordService.ChangeOwnPasswordAsync(payload);
        await login.Factory.DisposeAsync();

        // app_users still exists and is unaffected — no other table or statement executed
        // (spec "An Injection Payload In The Password Field Is Neutralized").
        var appUsersTableCount = await superuserContext.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = 'app_users'")
            .SingleAsync();
        Assert.Equal(1, appUsersTableCount);

        // The literal payload string now authenticates (spec "A Password Containing A Quote
        // Character Is Set Correctly").
        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, rawUsername, payload);
        await connection.OpenAsync();
    }
}
