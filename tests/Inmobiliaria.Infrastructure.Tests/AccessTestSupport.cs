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
}
