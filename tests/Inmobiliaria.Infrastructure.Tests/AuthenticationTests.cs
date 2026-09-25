using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves design Decision 1's data flow end to end: login is the connection attempt, role is
/// derived from <c>pg_has_role</c>, and the session survives its own password change. Tests 3–5,
/// 7, 8, 13 need a real engine (Testcontainers); test 2 does not (design.md's own Testing
/// Strategy table) and runs unconditionally.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthenticationTests
{
    private readonly PostgresFixture _fixture;

    public AuthenticationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Spec test 2 (package-graph half). Supabase Auth was considered and rejected (spec Decision 2) — never merely unimplemented.</summary>
    [Fact]
    public void ProjectGraph_HasNoSupabaseAuthPackageReference()
    {
        var referencedAssemblyNames = typeof(NpgsqlAuthenticator).Assembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();

        Assert.DoesNotContain(referencedAssemblyNames, name =>
            name.Contains("Supabase", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Gotrue", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Spec test 2 (connection-count half). Lexical, not runtime — counts how many times
    /// <c>NpgsqlAuthenticator</c>'s source opens a physical connection, which is exactly what
    /// "the login path opens exactly one Npgsql connection" means at the no-container layer
    /// design.md assigns this test to.
    /// </summary>
    [Fact]
    public void LoginPath_OpensExactlyOneNpgsqlConnection()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var sourceFile = Path.Combine(
            repoRoot, "src", "Inmobiliaria.Infrastructure", "Access", "NpgsqlAuthenticator.cs");
        var content = File.ReadAllText(sourceFile);

        var openCount = content.Split("OpenConnectionAsync(", StringSplitOptions.None).Length - 1;

        Assert.Equal(1, openCount);
    }

    [SkippableFact]
    public async Task CorrectCredentials_Succeed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "maria", UserRole.Empleado);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var result = await authenticator.AuthenticateAsync("maria", AccessTestSupport.DefaultPassword);

        var success = Assert.IsType<AuthenticationResult.Success>(result);
        Assert.Equal(UserRole.Empleado, success.Session.Role);

        await success.Factory.DisposeAsync();
    }

    [SkippableFact]
    public async Task WrongPassword_RejectedWithGenericResult()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "maria-wrongpw", UserRole.Empleado);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var result = await authenticator.AuthenticateAsync("maria-wrongpw", "definitely-the-wrong-password");

        Assert.IsType<AuthenticationResult.Rejected>(result);
    }

    /// <summary>Spec test 5: the SAME result type, with no distinguishing data on either — that identity IS the "identical wording" guarantee once a UI renders it.</summary>
    [SkippableFact]
    public async Task UnknownUsername_RejectedIdenticallyToWrongPassword()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var result = await authenticator.AuthenticateAsync($"nosuchuser{Guid.NewGuid():N}", "whatever-password");

        Assert.IsType<AuthenticationResult.Rejected>(result);
    }

    /// <summary>Spec tests 7 and 8, combined per tasks.md task 4.13: role comes from <c>pg_has_role</c>, and a database-side membership change takes effect on the very next login with no <c>app_users</c> update.</summary>
    [SkippableFact]
    public async Task RoleIsDerivedFromPgHasRole_AndADatabaseSideChangeTakesEffectNextLogin()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "sofia", UserRole.Empleado);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var firstLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia", AccessTestSupport.DefaultPassword));
        Assert.Equal(UserRole.Empleado, firstLogin.Session.Role);
        await firstLogin.Factory.DisposeAsync();

        // Database-side membership change, directly — no app_users column to update. Built
        // into a plain `string` first (not passed as an interpolated string literal), so
        // EF1002 does not flag this test-only DDL over a hard-coded role name.
        var composedUsername = SupavisorUsername.For("sofia", AccessTestSupport.TestProjectRef);
        string revokeEmpleadoSql = $"REVOKE inmobiliaria_empleado FROM \"{composedUsername}\";";
        string grantAdminSql = $"GRANT inmobiliaria_admin TO \"{composedUsername}\";";
        await superuserContext.Database.ExecuteSqlRawAsync(revokeEmpleadoSql);
        await superuserContext.Database.ExecuteSqlRawAsync(grantAdminSql);

        var secondLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia", AccessTestSupport.DefaultPassword));
        Assert.Equal(UserRole.Admin, secondLogin.Session.Role);
        await secondLogin.Factory.DisposeAsync();
    }

    /// <summary>Spec test 13: a self password change succeeds and the already-open session keeps working — no re-authentication.</summary>
    [SkippableFact]
    public async Task SelfPasswordChange_KeepsTheAlreadyOpenSessionWorking()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "lucia", UserRole.Empleado);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("lucia", AccessTestSupport.DefaultPassword));

        var passwordService = new PostgresPasswordService(login.Factory, login.Session);
        const string newPassword = "BrandNewPassword456!";
        await passwordService.ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, newPassword);

        // The already-open session keeps working, no re-authentication.
        await using var context = login.Factory.Create();
        var appUserCount = await context.AppUsers.CountAsync();
        Assert.True(appUserCount >= 1);

        await login.Factory.DisposeAsync();

        // A brand-new connection now requires the NEW password — proves the change was real,
        // not a no-op that happened to leave the already-open session alive.
        await using var freshConnection = AccessTestSupport.BuildRawConnectionAs(
            superuserContext, "lucia", newPassword);
        await freshConnection.OpenAsync();
    }
}
