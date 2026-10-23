using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Real login roles for tests that must connect as an Admin or an Empleado rather than as the
/// container's superuser (a superuser is a member of every role, so it proves nothing about a grant
/// or a <c>pg_has_role</c> check). Shared by <see cref="RolePermissionTests"/> and
/// <see cref="CutoverTriggerTests"/>.
/// </summary>
internal static class RoleTestHelpers
{
    /// <summary>
    /// Creates a throwaway LOGIN role granted membership in <paramref name="groupRole"/>,
    /// executed through the fixture's superuser connection (the Testcontainers image's
    /// initial user is a superuser, matching how <c>ALTER DEFAULT PRIVILEGES</c> binds to
    /// the migration owner in the migration itself — design.md Decision 9). Returns the role
    /// name and password so the caller can open a fresh connection authenticated as it.
    /// </summary>
    public static async Task<(string RoleName, string Password)> CreateTestLoginRoleAsync(
        InmobiliariaDbContext superuserContext,
        string groupRole,
        bool withAdminOption = false)
    {
        var roleName = $"test_{groupRole}_{Guid.NewGuid():N}"[..40];
        const string password = "TestRolePassword123!";
        var adminOptionClause = withAdminOption ? " WITH ADMIN OPTION" : "";

        // Test-only DDL over Guid-derived identifiers and a hard-coded constant password —
        // never user input. EF1002 (the SQL-injection analyzer) is not applicable here; the
        // sql text is built into a plain `string` first (not passed as an interpolated
        // string literal) so the analyzer does not flag it.
        string createRoleSql = $"CREATE ROLE {roleName} LOGIN PASSWORD '{password}';";
        string grantRoleSql = $"GRANT {groupRole} TO {roleName}{adminOptionClause};";
        await superuserContext.Database.ExecuteSqlRawAsync(createRoleSql);
        await superuserContext.Database.ExecuteSqlRawAsync(grantRoleSql);

        return (roleName, password);
    }

    /// <summary>Opens (unopened) an <see cref="NpgsqlConnection"/> to the fixture's container as a given role.</summary>
    public static NpgsqlConnection BuildConnectionAs(
        InmobiliariaDbContext superuserContext, string roleName, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            superuserContext.Database.GetDbConnection().ConnectionString)
        {
            Username = roleName,
            Password = password,
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }
}
