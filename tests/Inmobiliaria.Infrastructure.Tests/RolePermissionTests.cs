using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves the GRANT set of design.md Decision 9 against a real Postgres engine. This PR (2b)
/// covers only what is provable immediately after the migration runs, with no
/// <c>Infrastructure/Access</c> authenticator to log in through yet: spec test 29 (every table
/// reachable per its intended baseline grant) and task 3.12's ADMIN OPTION inheritance
/// experiment. Tests 22–28, 33 (PR 3, Phase 4) extend this same file once an authenticator
/// exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RolePermissionTests
{
    private readonly PostgresFixture _fixture;

    public RolePermissionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>All thirteen tables this change's migrations create, by their SQL name.</summary>
    private static readonly string[] AllTables =
    [
        "app_users", "parties", "units", "contracts", "contract_parties", "contract_units",
        "contract_documents", "economic_indices", "index_values", "adjustment_clauses",
        "adjustment_clause_indices", "rent_adjustments", "rent_adjustment_index_values",
    ];

    /// <summary>
    /// Creates a throwaway LOGIN role granted membership in <paramref name="groupRole"/>,
    /// executed through the fixture's superuser connection (the Testcontainers image's
    /// initial user is a superuser, matching how <c>ALTER DEFAULT PRIVILEGES</c> binds to
    /// the migration owner in the migration itself — design.md Decision 9). Returns the role
    /// name and password so the caller can open a fresh connection authenticated as it.
    /// </summary>
    private static async Task<(string RoleName, string Password)> CreateTestLoginRoleAsync(
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
    private static NpgsqlConnection BuildConnectionAs(
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

    [SkippableFact]
    public async Task AllThirteenTables_AreSelectableByBothRoles()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();

        var (empleadoRole, empleadoPassword) =
            await CreateTestLoginRoleAsync(superuserContext, "inmobiliaria_empleado");
        var (adminRole, adminPassword) =
            await CreateTestLoginRoleAsync(superuserContext, "inmobiliaria_admin");

        foreach (var (roleName, password) in new[]
                 {
                     (empleadoRole, empleadoPassword),
                     (adminRole, adminPassword),
                 })
        {
            await using var connection = BuildConnectionAs(superuserContext, roleName, password);
            await connection.OpenAsync();

            foreach (var table in AllTables)
            {
                await using var command = connection.CreateCommand();
                // A table absent from `GRANT SELECT ON ALL TABLES IN SCHEMA public` (spec
                // "Every New Table Ships With Its GRANTs in the Same Migration") would raise
                // 42501 (insufficient_privilege) here — the regression net for "forgot the
                // GRANT" (spec test 29).
                command.CommandText = $"SELECT 1 FROM {table} LIMIT 0";
                await command.ExecuteScalarAsync();
            }
        }
    }

    /// <summary>
    /// GATE — design.md Open Questions / Decision 9. The bootstrap runbook (task 3.7) grants
    /// the FIRST Admin ADMIN OPTION on both group roles <b>directly</b> — that case is not in
    /// question. The open question is what happens to every <b>subsequent</b> Admin, who is
    /// provisioned from inside the application by <c>app_create_login_role</c>: that function
    /// (task 3.5) issues only <c>GRANT %I TO %I</c> — no <c>WITH ADMIN OPTION</c> — because it
    /// runs as the calling Admin, not as a superuser deciding to escalate the new role's own
    /// privileges. This test reproduces exactly that shape: an Admin holding ADMIN OPTION only
    /// on <c>inmobiliaria_admin</c> (never on <c>inmobiliaria_empleado</c>) provisions a second
    /// Admin the way the application would, then asks whether that second Admin — a plain
    /// member of <c>inmobiliaria_admin</c> with default INHERIT, no ADMIN OPTION of its own, no
    /// CREATEROLE — can still grant <c>inmobiliaria_empleado</c> to a role it creates.
    /// </summary>
    [SkippableFact]
    public async Task AdminOptionInheritance_ProvenNotAssumed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();

        // Step 1 — the bootstrap runbook's exact shape for the FIRST admin: ADMIN OPTION on
        // BOTH group roles, granted directly by the superuser/migration owner, plus
        // CREATEROLE (task 3.7's `ALTER ROLE ... CREATEROLE`) so it can create the second
        // admin at all.
        var (firstAdminRole, firstAdminPassword) =
            await CreateTestLoginRoleAsync(superuserContext, "inmobiliaria_admin", withAdminOption: true);
        string grantEmpleadoToFirstAdminSql = $"GRANT inmobiliaria_empleado TO {firstAdminRole} WITH ADMIN OPTION;";
        await superuserContext.Database.ExecuteSqlRawAsync(grantEmpleadoToFirstAdminSql);
        string grantCreateRoleSql = $"ALTER ROLE {firstAdminRole} CREATEROLE;";
        await superuserContext.Database.ExecuteSqlRawAsync(grantCreateRoleSql);

        // Step 2 — the FIRST admin provisions a SECOND admin exactly the way
        // app_create_login_role (task 3.5) would: CREATE ROLE ... LOGIN, then a bare
        // `GRANT inmobiliaria_admin TO second_admin` with no ADMIN OPTION clause at all.
        var secondAdminRole = $"test_admin2_{Guid.NewGuid():N}"[..40];
        const string secondAdminPassword = "TestRolePassword123!";

        await using (var asFirstAdmin = BuildConnectionAs(superuserContext, firstAdminRole, firstAdminPassword))
        {
            await asFirstAdmin.OpenAsync();

            await using (var createCommand = asFirstAdmin.CreateCommand())
            {
                createCommand.CommandText = $"CREATE ROLE {secondAdminRole} LOGIN PASSWORD '{secondAdminPassword}';";
                await createCommand.ExecuteNonQueryAsync();
            }

            await using var grantAdminCommand = asFirstAdmin.CreateCommand();
            grantAdminCommand.CommandText = $"GRANT inmobiliaria_admin TO {secondAdminRole};";
            await grantAdminCommand.ExecuteNonQueryAsync();
        }

        // Step 3 — the actual open question: authenticated as the SECOND admin (a plain
        // member of inmobiliaria_admin, no ADMIN OPTION of its own on either group role),
        // attempt to grant inmobiliaria_empleado to a brand-new role.
        var provisionedRoleName = $"test_provisioned_{Guid.NewGuid():N}"[..40];
        string createProvisionedRoleSql = $"CREATE ROLE {provisionedRoleName} NOLOGIN;";
        await superuserContext.Database.ExecuteSqlRawAsync(createProvisionedRoleSql);

        await using var asSecondAdmin = BuildConnectionAs(superuserContext, secondAdminRole, secondAdminPassword);
        await asSecondAdmin.OpenAsync();

        await using var grantEmpleadoCommand = asSecondAdmin.CreateCommand();
        grantEmpleadoCommand.CommandText = $"GRANT inmobiliaria_empleado TO {provisionedRoleName};";

        var exception = await Record.ExceptionAsync(() => grantEmpleadoCommand.ExecuteNonQueryAsync());

        // TESTED OUTCOME (recorded in apply-progress.md task 3.12, not assumed): PostgreSQL 17
        // does NOT let a plain, non-admin-option member of inmobiliaria_admin grant a
        // DIFFERENT role (inmobiliaria_empleado) to anyone, even though the first Admin who
        // created it holds ADMIN OPTION on inmobiliaria_admin itself. ADMIN OPTION is scoped
        // per (role, member) grant edge, not transitively inherited through nested membership
        // or through the INHERIT attribute — INHERIT propagates ordinary privileges (like the
        // baseline SELECT/INSERT grants), never the right to administer role membership. The
        // failure is 42501 (insufficient_privilege).
        //
        // CONSEQUENCE: PR 4's provisioning function cannot rely on inheritance. The bootstrap
        // runbook (task 3.7) and PR 4's provisioning path must instead grant
        // `inmobiliaria_empleado ... WITH ADMIN OPTION` directly to every individual Admin
        // login role at the moment it is created — not only the first one. See
        // tasks.md H.3 and docs/runbooks/bootstrap-first-admin.md's "If task 3.12 failed"
        // section.
        Assert.NotNull(exception);
        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
    }
}
