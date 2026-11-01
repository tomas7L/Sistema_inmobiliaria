using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-usuarios spec Tests 13 (the real exception), 19, 24 and 25, against a real Postgres engine:
/// the rule on a chosen password, the login reading the username the way it was created, and a failed
/// password statement never reaching the log with its password.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChosenPasswordAndLoginTests
{
    private readonly PostgresFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ChosenPasswordAndLoginTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Spec Test 19: a too-short password is refused before the database, and a forced change stays pending.</summary>
    [SkippableFact]
    public async Task AForcedChange_ToATooShortPassword_IsRefused_AndStaysPending()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "rule19a", UserRole.Empleado, mustChangePassword: true);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("rule19a", AccessTestSupport.DefaultPassword));
        var refused = await Assert.ThrowsAsync<PasswordChangeRefusedException>(
            () => new PostgresPasswordService(login.Factory, login.Session)
                .ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, "1234567"));
        await login.Factory.DisposeAsync();

        Assert.Equal(PasswordRefusal.TooShort, refused.Refusal);
        Assert.Equal("La nueva contraseña debe tener al menos 8 caracteres.", refused.Message);
        Assert.True(login.Session.MustChangePassword);

        // Still pending in the database, and the old password still works.
        var again = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("rule19a", AccessTestSupport.DefaultPassword));
        Assert.True(again.Session.MustChangePassword);
        await again.Factory.DisposeAsync();
    }

    /// <summary>
    /// Spec Test 19 against a real login: the session's username, in another case, is refused and a forced change
    /// stays pending. Here the session name is the test database's role name, suffix included; production's
    /// plain name is <see cref="WithAPlainSessionUsername_TheUsernameIsRefusedAsAPassword"/>.
    /// </summary>
    [SkippableFact]
    public async Task TheSessionUsername_IsRefusedAsAPassword_AndAForcedChangeStaysPending()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "rule19b", UserRole.Empleado, mustChangePassword: true);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("rule19b", AccessTestSupport.DefaultPassword));
        var asTyped = login.Session.Username.ToUpperInvariant();
        var refused = await Assert.ThrowsAsync<PasswordChangeRefusedException>(
            () => new PostgresPasswordService(login.Factory, login.Session)
                .ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, asTyped));
        await login.Factory.DisposeAsync();

        Assert.Equal(PasswordRefusal.EqualsUsername, refused.Refusal);
        Assert.True(login.Session.MustChangePassword);
    }

    /// <summary>
    /// Spec Test 19 in production's shape: through Supavisor the session's username is the plain role name
    /// ("sofiaperez", no project suffix), and a password equal to it is refused before any database call.
    /// </summary>
    [Theory]
    [InlineData("sofiaperez")]
    [InlineData("SofiaPerez")]
    public async Task WithAPlainSessionUsername_TheUsernameIsRefusedAsAPassword(string newPassword)
    {
        var session = new UserSession(new AppUser(Guid.NewGuid(), "sofiaperez", "Sofía Pérez"), UserRole.Empleado);
        var service = new PostgresPasswordService(new NeverConnectingFactory(), session);

        var refused = await Assert.ThrowsAsync<PasswordChangeRefusedException>(
            () => service.ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, newPassword));

        Assert.Equal(PasswordRefusal.EqualsUsername, refused.Refusal);
    }

    /// <summary>Spec Test 19: the rule also applies to a voluntary change, and eight characters with no symbols pass.</summary>
    [SkippableFact]
    public async Task AVoluntaryChange_FollowsTheSameRule()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "rule19c", UserRole.Empleado);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("rule19c", AccessTestSupport.DefaultPassword));
        var service = new PostgresPasswordService(login.Factory, login.Session);

        var refused = await Assert.ThrowsAsync<PasswordChangeRefusedException>(
            () => service.ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, "abc"));
        Assert.Equal(PasswordRefusal.TooShort, refused.Refusal);

        await service.ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, "girasoles");
        await login.Factory.DisposeAsync();

        var withNew = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("rule19c", "girasoles"));
        await withNew.Factory.DisposeAsync();
    }

    /// <summary>Spec Test 24: capitals, surrounding spaces and a decomposed "ñ" still log in as the created name.</summary>
    [SkippableTheory]
    [InlineData("nuñez24", "Nuñez24 ")]
    [InlineData("nuñez24", "  NUÑEZ24")]
    [InlineData("nuñez24", "nuñez24")]
    [InlineData("sofia24", "Sofia24")]
    public async Task TheLoginUsername_IsReadTheWayItWasCreated(string created, string typed)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await EnsureProvisionedAsync(superuserContext, created);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync(typed, AccessTestSupport.DefaultPassword));
        await login.Factory.DisposeAsync();

        Assert.Equal(SupavisorUsername.For(created, AccessTestSupport.TestProjectRef), login.Session.Username);
    }

    /// <summary>Spec Test 24: the password is never normalized.</summary>
    [SkippableFact]
    public async Task APasswordDifferingOnlyInCase_IsRejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "case24", UserRole.Empleado);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var result = await authenticator.AuthenticateAsync(
            "Case24", AccessTestSupport.DefaultPassword.ToLowerInvariant());

        Assert.IsType<AuthenticationResult.Rejected>(result);
    }

    /// <summary>Spec Test 25: after normalization, a wrong password and an unknown username still fail identically.</summary>
    [SkippableFact]
    public async Task AfterNormalization_WrongPasswordAndUnknownUser_FailIdentically()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "fail25", UserRole.Empleado);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));

        var wrongPassword = await authenticator.AuthenticateAsync("FAIL25 ", "not-the-password");
        var unknownUser = await authenticator.AuthenticateAsync($"NoSuch{Guid.NewGuid():N}"[..20], "not-the-password");

        Assert.IsType<AuthenticationResult.Rejected>(wrongPassword);
        Assert.IsType<AuthenticationResult.Rejected>(unknownUser);
    }

    /// <summary>
    /// Spec Test 13, design Finding F5: an Admin resetting a role it did not create fails inside
    /// <c>app_set_role_password</c>. Whatever the real exception carries, the masked text written to the log
    /// holds no password.
    /// </summary>
    [SkippableFact]
    public async Task AFailedPasswordStatement_NeverReachesTheLogWithItsPassword()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "mask13admin", UserRole.Admin);
        await AccessTestSupport.GrantProvisioningCapabilityAsync(superuserContext, "mask13admin");
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "mask13target", UserRole.Empleado);
        await using var adminFactory = AccessTestSupport.BuildSessionFactoryAs(superuserContext, "mask13admin");

        const string secret = "lago-sol-pan-48017";
        var target = SupavisorUsername.For("mask13target", AccessTestSupport.TestProjectRef);
        await using var context = adminFactory.Create();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT app_set_role_password({target}, {secret})"));

        var unmasked = failure.ToString();
        _output.WriteLine($"SqlState {failure.SqlState}; unmasked text carries the password: {unmasked.Contains(secret)}");
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        // Observed on 2026-11-01 (design Finding F5): PostgreSQL quotes the failed statement, password included.
        // Asserted first so the mask below is proven against the real text, not against a text with nothing to hide.
        Assert.Contains(secret, unmasked);
        Assert.DoesNotContain(secret, SecretMask.Mask(unmasked));
    }

    /// <summary>Fails the test if a refused change ever reaches the database.</summary>
    private sealed class NeverConnectingFactory : ISessionDbContextFactory
    {
        public InmobiliariaDbContext Create() =>
            throw new InvalidOperationException("A refused password change must not open a database context.");

        public void UsePassword(string newPassword) =>
            throw new InvalidOperationException("A refused password change must not replace the session password.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task EnsureProvisionedAsync(InmobiliariaDbContext superuserContext, string rawUsername)
    {
        var composed = SupavisorUsername.For(rawUsername, AccessTestSupport.TestProjectRef);
        if (!await superuserContext.AppUsers.AnyAsync(u => u.Username == composed))
        {
            await AccessTestSupport.ProvisionUserAsync(superuserContext, rawUsername, UserRole.Empleado);
        }
    }
}
