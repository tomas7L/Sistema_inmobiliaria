using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// revocar-permisos-api spec Requirements 1 to 5, Tests 1 to 4, 6 to 8 and 10: the <c>RevokeSupabaseApiRoles</c> migration leaves
/// Supabase's API roles holding nothing in public, now or on what is created later, and closes the operator functions to
/// <c>PUBLIC</c>. Each test migrates a database of its own to the migration before it (design D9), so the before state is real,
/// granted as <see cref="PostgresFixture"/> grants like Supabase. Privileges are read with <c>has_*_privilege</c>, never
/// <c>information_schema</c>, which does not show Postgres 17's <c>MAINTAIN</c> (Requirement 1).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SupabaseApiRoleRevocationTests
{
    /// <summary>The migration just before <c>RevokeSupabaseApiRoles</c> (design F3).</summary>
    private const string BeforeRevocation = "20261112160850_AddTenantNotices";

    private static readonly string[] EveryTablePrivilege =
        ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER", "MAINTAIN"];

    private static readonly string[] EverySequencePrivilege = ["USAGE", "SELECT", "UPDATE"];

    private static readonly string[] GroupRoles = ["inmobiliaria_admin", "inmobiliaria_empleado"];

    /// <summary>The API roles and <c>PUBLIC</c>, which every role inherits: none of them may run an operator function.</summary>
    private static readonly string[] Outsiders = [.. PostgresFixture.SupabaseApiRoles, "public"];

    /// <summary>Requirement 3's table: who runs each operator function. Every API role and <c>PUBLIC</c> is "No".</summary>
    private static readonly (string Function, bool Admin, bool Empleado)[] OperatorFunctions =
    [
        ("app_set_role_password(name, text)", true, true),
        ("app_clear_must_change_password()", true, true),
        ("app_replace_clause_indices(uuid, uuid[])", true, true),
        ("app_create_login_role(name, text, name)", true, false),
        ("app_set_role_login(name, boolean)", true, false),
    ];

    private readonly PostgresFixture _fixture;

    public SupabaseApiRoleRevocationTests(PostgresFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Test1_BeforeTheMigration_TheApiRolesHoldEveryTablePrivilege_AndTheirOwnGrantOnAnOperatorFunction()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        await using var connection = await database.OpenAsync();

        foreach (var role in PostgresFixture.SupabaseApiRoles)
        {
            Assert.Equal(
                EveryTablePrivilege.Select(p => $"{role}|contract_events|{p}").Order(StringComparer.Ordinal),
                (await TablePrivilegesHeldAsync(connection, [role])).Where(h => h.Contains("|contract_events|", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            Assert.Contains(role, await FunctionAclGranteesAsync(connection, "app_set_role_password(name, text)"));
        }
    }

    [SkippableFact]
    public async Task Tests2And3_AfterTheMigration_NoApiRoleHoldsAnythingOnAnyTableOrSequence_UsersAndHistoryIncluded()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);

        // The model has no sequence, so one is made before the migration, granted like Supabase grants them: without it, the
        // sequence half of the assertion below would pass on nothing.
        await using (var before = await database.OpenAsync())
        {
            await ExecuteAsync(before, "CREATE SEQUENCE earlier_sequence");
            Assert.Equal(
                PostgresFixture.SupabaseApiRoles.SelectMany(r => EverySequencePrivilege.Select(p => $"{r}|earlier_sequence|{p}")).Order(StringComparer.Ordinal),
                (await SequencePrivilegesHeldAsync(before, PostgresFixture.SupabaseApiRoles)).Order(StringComparer.Ordinal));
        }

        await database.MigrateAsync();
        await using var connection = await database.OpenAsync();

        var tables = await PublicTablesAsync(connection);
        Assert.NotEmpty(tables);
        Assert.Contains("app_users", tables);
        Assert.Contains("__EFMigrationsHistory", tables);

        await AssertApiRolesHoldNoTableOrSequenceAsync(connection);
    }

    [SkippableFact]
    public async Task Test4_TheGroupRoles_HoldExactlyWhatTheyHeldBefore_OnEveryTable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        List<string> before;
        await using (var connection = await database.OpenAsync())
        {
            before = await TablePrivilegesHeldAsync(connection, GroupRoles);
        }

        await database.MigrateAsync();

        await using (var connection = await database.OpenAsync())
        {
            Assert.NotEmpty(before);
            Assert.Equal(before, await TablePrivilegesHeldAsync(connection, GroupRoles));
        }
    }

    [SkippableFact]
    public async Task Test6_ATableSequenceAndFunctionCreatedAfterwards_AreGrantedToNoApiRole_AndTheGroupsStillReadTheTable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        await database.MigrateAsync();
        await using var connection = await database.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE later_table (id integer);
            CREATE SEQUENCE later_sequence;
            CREATE FUNCTION later_function() RETURNS integer LANGUAGE sql AS 'SELECT 1';
            """);

        Assert.DoesNotContain(await TablePrivilegesHeldAsync(connection, PostgresFixture.SupabaseApiRoles), h => h.Contains("|later_table|", StringComparison.Ordinal));
        Assert.Empty(await SequencePrivilegesHeldAsync(connection, PostgresFixture.SupabaseApiRoles));
        Assert.Equal(
            GroupRoles.Select(r => $"{r}|later_table|SELECT"),
            (await TablePrivilegesHeldAsync(connection, GroupRoles)).Where(h => h.Contains("|later_table|", StringComparison.Ordinal)));
        Assert.Empty((await FunctionAclGranteesAsync(connection, "later_function()")).Intersect(PostgresFixture.SupabaseApiRoles));
    }

    [SkippableFact]
    public async Task Test7_TheOperatorFunctions_RunForExactlyTheGroupRolesRequirement3Names()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        await database.MigrateAsync();
        await using var connection = await database.OpenAsync();

        await AssertOperatorFunctionMatrixAsync(connection);
    }

    [SkippableFact]
    public async Task Test8_NoOperatorFunctionIsOpenToAnOutsider_AndOneCreatedOpenIsNamed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        await database.MigrateAsync();
        await using var connection = await database.OpenAsync();

        Assert.Empty(await OpenOperatorFunctionsAsync(connection));

        // A future migration that forgets to revoke PUBLIC: Postgres grants EXECUTE to PUBLIC on every new function.
        await ExecuteAsync(connection, "CREATE FUNCTION forgot_to_close() RETURNS void LANGUAGE sql AS ''");

        Assert.Equal(["forgot_to_close()"], await OpenOperatorFunctionsAsync(connection));
    }

    [SkippableFact]
    public async Task Test10_RollingTheMigrationBack_ReopensNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await RevocationDatabase.CreateAsync(_fixture);
        await database.MigrateAsync();
        await database.MigrateAsync(BeforeRevocation);
        await using var connection = await database.OpenAsync();

        Assert.Equal(BeforeRevocation, (await database.AppliedMigrationsAsync()).Last());
        await AssertApiRolesHoldNoTableOrSequenceAsync(connection);
        await AssertOperatorFunctionMatrixAsync(connection);
    }

    private static async Task AssertApiRolesHoldNoTableOrSequenceAsync(NpgsqlConnection connection)
    {
        Assert.Empty(await TablePrivilegesHeldAsync(connection, PostgresFixture.SupabaseApiRoles));
        Assert.Empty(await SequencePrivilegesHeldAsync(connection, PostgresFixture.SupabaseApiRoles));
    }

    private static async Task AssertOperatorFunctionMatrixAsync(NpgsqlConnection connection)
    {
        var expected = new List<string>();
        var actual = new List<string>();
        foreach (var (function, admin, empleado) in OperatorFunctions)
        {
            var roles = GroupRoles.Concat(Outsiders);
            foreach (var role in roles)
            {
                var runs = role switch
                {
                    "inmobiliaria_admin" => admin,
                    "inmobiliaria_empleado" => empleado,
                    _ => false,
                };
                expected.Add($"{function}|{role}|{(runs ? "Yes" : "No")}");

                var held = await ListAsync(
                    connection,
                    "SELECT has_function_privilege(@role, @function::regprocedure, 'EXECUTE')::text",
                    ("role", role),
                    ("function", function));
                actual.Add($"{function}|{role}|{(held.Single() == "true" ? "Yes" : "No")}");
            }
        }

        Assert.Equal(expected, actual);
    }

    /// <summary>Every <c>role|table|privilege</c> held on a table in public, in order; empty when <paramref name="roles"/> hold nothing.</summary>
    private static Task<List<string>> TablePrivilegesHeldAsync(NpgsqlConnection connection, IEnumerable<string> roles) =>
        ListAsync(
            connection,
            """
            SELECT r.role || '|' || t.tablename || '|' || p.privilege
            FROM pg_tables t
            CROSS JOIN unnest(@roles) AS r(role)
            CROSS JOIN unnest(@privileges) AS p(privilege)
            WHERE t.schemaname = 'public'
              AND has_table_privilege(r.role, format('public.%I', t.tablename), p.privilege)
            ORDER BY 1
            """,
            ("roles", roles.ToArray()),
            ("privileges", EveryTablePrivilege));

    private static Task<List<string>> SequencePrivilegesHeldAsync(NpgsqlConnection connection, IEnumerable<string> roles) =>
        ListAsync(
            connection,
            """
            SELECT r.role || '|' || s.sequencename || '|' || p.privilege
            FROM pg_sequences s
            CROSS JOIN unnest(@roles) AS r(role)
            CROSS JOIN unnest(@privileges) AS p(privilege)
            WHERE s.schemaname = 'public'
              AND has_sequence_privilege(r.role, format('public.%I', s.sequencename), p.privilege)
            ORDER BY 1
            """,
            ("roles", roles.ToArray()),
            ("privileges", EverySequencePrivilege));

    private static Task<List<string>> PublicTablesAsync(NpgsqlConnection connection) =>
        ListAsync(connection, "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public' ORDER BY 1");

    /// <summary>The roles a function's own ACL names (<c>public</c> for <c>PUBLIC</c>); empty when the ACL is the built-in default.</summary>
    private static Task<List<string>> FunctionAclGranteesAsync(NpgsqlConnection connection, string function) =>
        ListAsync(
            connection,
            """
            SELECT CASE WHEN a.grantee = 0 THEN 'public' ELSE a.grantee::regrole::text END
            FROM pg_proc p, aclexplode(p.proacl) a
            WHERE p.oid = @function::regprocedure
            ORDER BY 1
            """,
            ("function", function));

    /// <summary>
    /// Every function in public that is not a trigger function and that an API role or <c>PUBLIC</c> can execute (spec Decision 10).
    /// Trigger functions are left open to <c>PUBLIC</c> on purpose (design D6), and cannot be called directly.
    /// </summary>
    private static Task<List<string>> OpenOperatorFunctionsAsync(NpgsqlConnection connection) =>
        ListAsync(
            connection,
            """
            SELECT p.oid::regprocedure::text
            FROM pg_proc p
            WHERE p.pronamespace = 'public'::regnamespace
              AND p.prorettype <> 'trigger'::regtype
              AND EXISTS (SELECT 1 FROM unnest(@roles) AS r(role) WHERE has_function_privilege(r.role, p.oid, 'EXECUTE'))
            ORDER BY 1
            """,
            ("roles", Outsiders));

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A database of its own, granted like Supabase and migrated to <see cref="BeforeRevocation"/>, dropped on dispose. The connection
    /// is the container's superuser, the role that runs the migrations, as <c>postgres</c> does on Supabase (design D4).
    /// </summary>
    private sealed class RevocationDatabase : IAsyncDisposable
    {
        private readonly InmobiliariaDbContext _admin;
        private readonly InmobiliariaDbContext _context;
        private readonly string _name;

        private RevocationDatabase(InmobiliariaDbContext admin, InmobiliariaDbContext context, string name)
        {
            _admin = admin;
            _context = context;
            _name = name;
        }

        public static async Task<RevocationDatabase> CreateAsync(PostgresFixture fixture)
        {
            var admin = fixture.CreateDbContext();
            var name = $"revocation_{Guid.NewGuid():N}";
            await ExecuteConstantSqlAsync(admin, $"CREATE DATABASE {name}");

            var connection = new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()!)
            {
                Database = name,
                Pooling = false,
            };
            var context = new InmobiliariaDbContext(
                new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(connection.ConnectionString).Options);

            var database = new RevocationDatabase(admin, context, name);
            await PostgresFixture.GrantLikeSupabaseAsync(context);
            await database.MigrateAsync(BeforeRevocation);
            return database;
        }

        /// <summary>Migrates to <paramref name="target"/>, or to the latest migration when it is null.</summary>
        public Task MigrateAsync(string? target = null) => _context.GetService<IMigrator>().MigrateAsync(target);

        public async Task<string[]> AppliedMigrationsAsync() => [.. await _context.Database.GetAppliedMigrationsAsync()];

        public async Task<NpgsqlConnection> OpenAsync()
        {
            var connection = new NpgsqlConnection(_context.Database.GetConnectionString());
            await connection.OpenAsync();
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await ExecuteConstantSqlAsync(_admin, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
            await _admin.DisposeAsync();
        }

        /// <summary>A statement built from a generated database name, never user input; the one EF1002 suppression lives here.</summary>
        private static Task<int> ExecuteConstantSqlAsync(InmobiliariaDbContext context, string sql)
        {
#pragma warning disable EF1002
            return context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
        }
    }
}
