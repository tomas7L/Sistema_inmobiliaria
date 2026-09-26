using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Shared provisioning for every Testcontainers-backed Access test (Authentication,
/// PasswordDdl, RolePermission, and the SchemaConstraintTests append-only re-run). There is no
/// Supavisor pooler in front of the Testcontainers instance, so <see cref="SupavisorUsername.For"/>'s
/// composed value IS the literal PostgreSQL role name here — the pooler's own suffix-stripping
/// is a separately VERIFIED FACT (spec Decision 9) this test suite does not need to reproduce.
/// </summary>
internal static class AccessTestSupport
{
    public const string TestProjectRef = "test-ref";
    public const string DefaultPassword = "TestRolePassword123!";

    public static ConnectionEndpoint BuildEndpoint(InmobiliariaDbContext superuserContext)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            superuserContext.Database.GetDbConnection().ConnectionString);

        return new ConnectionEndpoint(builder.Host!, builder.Port, builder.Database!, TestProjectRef, SslMode.Disable);
    }

    /// <summary>
    /// Creates a real PostgreSQL LOGIN role named exactly as <see cref="SupavisorUsername.For"/>
    /// would compose it, granted membership in the given group role, and a matching
    /// <c>app_users</c> row whose <c>username</c> is that same composed value (so
    /// <c>NpgsqlAuthenticator</c>'s composed connection username matches this role's actual
    /// <c>rolname</c> with no pooler in between to strip it back down).
    /// </summary>
    public static async Task<Guid> ProvisionUserAsync(
        InmobiliariaDbContext superuserContext,
        string rawUsername,
        UserRole role,
        string? password = null,
        bool isActive = true,
        bool mustChangePassword = false)
    {
        var composedUsername = SupavisorUsername.For(rawUsername, TestProjectRef);
        var groupRole = role == UserRole.Admin ? "inmobiliaria_admin" : "inmobiliaria_empleado";
        var actualPassword = password ?? DefaultPassword;

        // Test-only DDL over a caller-supplied prefix and a hard-coded constant password —
        // never end-user input. Built into a plain `string` first, not passed as an
        // interpolated string literal, so EF1002 does not apply (it is not an EF API call).
        string createRoleSql = $"CREATE ROLE \"{composedUsername}\" LOGIN PASSWORD '{actualPassword}';";
        string grantRoleSql = $"GRANT {groupRole} TO \"{composedUsername}\";";
        await superuserContext.Database.ExecuteSqlRawAsync(createRoleSql);
        await superuserContext.Database.ExecuteSqlRawAsync(grantRoleSql);

        var userId = Guid.NewGuid();
        var user = new AppUser(userId, composedUsername, rawUsername, isActive, mustChangePassword);
        superuserContext.AppUsers.Add(user);
        await superuserContext.SaveChangesAsync();

        return userId;
    }

    /// <summary>
    /// Opens a raw <see cref="NpgsqlConnection"/> as the composed role — bypassing
    /// <see cref="IAuthenticator"/> entirely, for tests that must prove database-level
    /// enforcement independent of the application (spec tests 19, 22–28, 34).
    /// </summary>
    public static NpgsqlConnection BuildRawConnectionAs(
        InmobiliariaDbContext superuserContext, string rawUsername, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            superuserContext.Database.GetDbConnection().ConnectionString)
        {
            Username = SupavisorUsername.For(rawUsername, TestProjectRef),
            Password = password ?? DefaultPassword,
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    /// <summary>
    /// Builds a fresh EF context authenticated as the composed role, for tests that write
    /// through the domain model as a specific role rather than through raw SQL (spec tests
    /// 25, 26, 33).
    /// </summary>
    public static InmobiliariaDbContext BuildDbContextAs(
        InmobiliariaDbContext superuserContext, string rawUsername, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            superuserContext.Database.GetDbConnection().ConnectionString)
        {
            Username = SupavisorUsername.For(rawUsername, TestProjectRef),
            Password = password ?? DefaultPassword,
        };

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new InmobiliariaDbContext(options);
    }

    /// <summary>
    /// Wraps a role's connection in an <see cref="ISessionDbContextFactory"/> — the same shape
    /// <see cref="NpgsqlAuthenticator"/> hands a real login through — so
    /// <see cref="PostgresUserProvisioning"/> and <see cref="PostgresPasswordService"/> can be
    /// exercised exactly as PR 5's ViewModels eventually will, without going through a full
    /// login handshake for a role the test already knows exists.
    /// </summary>
    public static ISessionDbContextFactory BuildSessionFactoryAs(
        InmobiliariaDbContext superuserContext, string rawUsername, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            superuserContext.Database.GetDbConnection().ConnectionString)
        {
            Username = SupavisorUsername.For(rawUsername, TestProjectRef),
            Password = password ?? DefaultPassword,
        };

        var dataSource = new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
        return new SessionDbContextFactory(dataSource);
    }

    /// <summary>
    /// Grants the exact bootstrap-runbook shape
    /// (<c>docs/runbooks/bootstrap-first-admin.md</c>) to an already-provisioned Admin test
    /// user: <c>CREATEROLE</c> plus <c>ADMIN OPTION</c> on BOTH group roles, granted directly by
    /// the superuser connection. Required before that Admin can successfully call
    /// <c>app_create_login_role</c> or <c>app_set_role_password</c> against ANOTHER role — task
    /// 3.12 (PR 2b) proved a bare, non-admin-option Admin membership is not enough
    /// (<c>RolePermissionTests.AdminOptionInheritance_ProvenNotAssumed</c>), and
    /// <c>RolePermissionTests.Admin_DirectAppUsersWriteAndProvisioningFunction_BothSucceed</c>
    /// (PR 3) already reproduces this same grant shape for the same reason.
    /// </summary>
    public static async Task GrantProvisioningCapabilityAsync(
        InmobiliariaDbContext superuserContext, string rawAdminUsername)
    {
        var composedUsername = SupavisorUsername.For(rawAdminUsername, TestProjectRef);
        string grantCreateRoleSql = $"ALTER ROLE \"{composedUsername}\" CREATEROLE;";
        string grantAdminOptionSql =
            $"GRANT inmobiliaria_admin, inmobiliaria_empleado TO \"{composedUsername}\" WITH ADMIN OPTION;";
        await superuserContext.Database.ExecuteSqlRawAsync(grantCreateRoleSql);
        await superuserContext.Database.ExecuteSqlRawAsync(grantAdminOptionSql);
    }
}
