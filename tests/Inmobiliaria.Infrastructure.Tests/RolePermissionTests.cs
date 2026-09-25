using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Access;
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

    /// <summary>Spec test 22: the ONLY asymmetry left. A direct write to <c>app_users</c> and a call to the provisioning function are both refused for Empleado, with no row and no role created.</summary>
    [SkippableFact]
    public async Task Empleado_DirectAppUsersWriteAndProvisioningFunction_BothRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "empleado-gov", UserRole.Empleado);

        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, "empleado-gov");
        await connection.OpenAsync();

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText =
                $"INSERT INTO app_users (id, username, display_name) " +
                $"VALUES ('{Guid.NewGuid()}'::uuid, 'should-not-exist-{Guid.NewGuid():N}', 'nope')";
            var exception = await Record.ExceptionAsync(() => insertCommand.ExecuteNonQueryAsync());
            var postgresException = Assert.IsType<PostgresException>(exception);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
        }

        await using (var provisionCommand = connection.CreateCommand())
        {
            provisionCommand.CommandText =
                $"SELECT app_create_login_role('should-not-exist-{Guid.NewGuid():N}', 'x', 'inmobiliaria_empleado')";
            var exception = await Record.ExceptionAsync(() => provisionCommand.ExecuteNonQueryAsync());
            var postgresException = Assert.IsType<PostgresException>(exception);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
        }
    }

    /// <summary>Spec test 23: the Admin's equivalent of both test-22 operations succeeds — the grant, not the application, is what allows it.</summary>
    [SkippableFact]
    public async Task Admin_DirectAppUsersWriteAndProvisioningFunction_BothSucceed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "admin-gov", UserRole.Admin);

        // app_create_login_role is NOT SECURITY DEFINER (design Decision 3): it runs as the
        // caller, so its internal CREATE ROLE and GRANT <group_role> still need the caller's
        // own privileges — exactly the consequence task 3.12 proved (a bare, non-admin-option
        // Admin membership is NOT enough) and the reason the bootstrap runbook grants its first
        // Admin CREATEROLE plus ADMIN OPTION on BOTH group roles directly. Reproduced here
        // verbatim so this test proves test 23's positive case using the same grant shape the
        // runbook actually ships, rather than re-proving task 3.12's already-recorded negative
        // one with a weaker setup.
        var composedAdminUsername = SupavisorUsername.For("admin-gov", AccessTestSupport.TestProjectRef);
        string grantCreateRoleSql = $"ALTER ROLE \"{composedAdminUsername}\" CREATEROLE;";
        string grantAdminOptionSql =
            $"GRANT inmobiliaria_admin, inmobiliaria_empleado TO \"{composedAdminUsername}\" WITH ADMIN OPTION;";
        await superuserContext.Database.ExecuteSqlRawAsync(grantCreateRoleSql);
        await superuserContext.Database.ExecuteSqlRawAsync(grantAdminOptionSql);

        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, "admin-gov");
        await connection.OpenAsync();

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText =
                $"INSERT INTO app_users (id, username, display_name) " +
                $"VALUES ('{Guid.NewGuid()}'::uuid, 'created-by-admin-{Guid.NewGuid():N}', 'created by admin')";
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var provisionCommand = connection.CreateCommand())
        {
            provisionCommand.CommandText =
                $"SELECT app_create_login_role('admin-created-{Guid.NewGuid():N}', 'x', 'inmobiliaria_empleado')";
            await provisionCommand.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Spec test 24: neither role owns any table, so neither can disable the append-only trigger — no extra revoke, only the absence of ownership.</summary>
    [SkippableFact]
    public async Task NeitherRole_CanDisableTheAppendOnlyTrigger()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "trigger24-empleado", UserRole.Empleado);
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "trigger24-admin", UserRole.Admin);

        foreach (var rawUsername in new[] { "trigger24-empleado", "trigger24-admin" })
        {
            await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, rawUsername);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE rent_adjustments DISABLE TRIGGER ALL";

            var exception = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());
            Assert.IsType<PostgresException>(exception);
        }
    }

    /// <summary>Spec test 28: no Row-Level Security anywhere — both roles, granted SELECT on the same table, see every row.</summary>
    [SkippableFact]
    public async Task BothRoles_SeeEveryRowOfATableTheyMayRead()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var partyA = new Party(Guid.NewGuid(), "Row", $"Visible-{Guid.NewGuid():N}");
        var partyB = new Party(Guid.NewGuid(), "Row", $"Visible-{Guid.NewGuid():N}");
        superuserContext.Parties.AddRange(partyA, partyB);
        await superuserContext.SaveChangesAsync();

        await AccessTestSupport.ProvisionUserAsync(superuserContext, "rls-empleado", UserRole.Empleado);
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "rls-admin", UserRole.Admin);

        foreach (var rawUsername in new[] { "rls-empleado", "rls-admin" })
        {
            await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, rawUsername);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM parties WHERE id IN ('{partyA.Id}', '{partyB.Id}')";
            var visibleCount = (long)(await command.ExecuteScalarAsync())!;

            Assert.Equal(2, visibleCount);
        }
    }

    /// <summary>
    /// Spec test 25 — POSITIVE assertion, deliberately not a smoke test. As Empleado,
    /// <c>Contract.GiveNotice</c> AND <c>Contract.End</c> both succeed end-to-end, writing
    /// <c>status</c>, <c>actual_end_date</c> and <c>end_reason</c>. This is the standing guard
    /// against the removed column-level termination restriction quietly returning.
    /// </summary>
    [SkippableFact]
    public async Task Empleado_CanTerminateAContract_NoticeAndEndBothSucceed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "empleado-term", UserRole.Empleado);

        await using var empleadoContext = AccessTestSupport.BuildDbContextAs(superuserContext, "empleado-term");

        var unit = new PropertyUnit(
            Guid.NewGuid(), new Address("Fake Street", "1", "Springfield", "Buenos Aires", "1000"));
        empleadoContext.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31),
            100_000m, [new UnitShare(unit.Id, 100m)]);
        empleadoContext.Contracts.Add(contract);
        await empleadoContext.SaveChangesAsync();

        contract.GiveNotice(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));
        contract.End(EndReason.EarlyTerminationByTenant, new DateOnly(2026, 7, 1));
        await empleadoContext.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var reloaded = await readContext.Contracts.SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(ContractStatus.Ended, reloaded.Status);
        Assert.Equal(new DateOnly(2026, 7, 1), reloaded.ActualEndDate);
        Assert.Equal(EndReason.EarlyTerminationByTenant, reloaded.EndReason);
    }

    /// <summary>
    /// Spec test 26 — POSITIVE assertion, mirroring test 25. As Empleado, confirming a rent
    /// adjustment succeeds and the new row records her in <c>confirmed_by</c>; both
    /// <c>rent_adjustments</c> and <c>rent_adjustment_index_values</c> insert in one
    /// transaction, proving the paired grant of design Decision 9. The standing guard against
    /// the removed money clause returning as an <c>INSERT</c> grant quietly withheld.
    /// </summary>
    [SkippableFact]
    public async Task Empleado_CanConfirmARentAdjustment_WithConfirmedByRecorded()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var empleadoUserId = await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "empleado-adjust", UserRole.Empleado);

        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(superuserContext);
        superuserContext.IndexValues.Add(
            new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 9_440m));
        await superuserContext.SaveChangesAsync();

        await using var empleadoContext = AccessTestSupport.BuildDbContextAs(superuserContext, "empleado-adjust");
        var trackedContract = await empleadoContext.Contracts.SingleAsync(c => c.Id == contract.Id);

        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            trackedContract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, empleadoUserId);

        // Same EF gotcha as PR 2b: a newly-appeared entity carrying a client-assigned key is
        // not inferred as Added from a collection-diff alone.
        empleadoContext.RentAdjustments.Add(adjustment);
        await empleadoContext.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();

        // Loaded with `.Include`, deliberately. This test previously queried the child table by
        // its shadow FK to work around RentAdjustment's materialization constructor handing EF a
        // fixed-size array it could not append to. That is fixed at the source, and eager-loading
        // here is what keeps it fixed: if the navigation is ever handed a fixed-size collection
        // again, this line throws instead of the defect resurfacing in the collection change.
        var reloadedAdjustment = await readContext.RentAdjustments
            .Include(a => a.IndexValues)
            .SingleAsync(a => a.Id == adjustment.Id);

        Assert.Equal((Guid?)empleadoUserId, reloadedAdjustment.ConfirmedBy);
        Assert.Equal(1, reloadedAdjustment.IndexValues.Count);
    }

    /// <summary>Spec test 27: the exclusion from aggregate business reporting is application-enforced only — no GRANT can forbid an aggregate over rows a role may already read.</summary>
    [SkippableFact]
    public async Task Empleado_AggregateOverContracts_SucceedsBecauseNoGrantCanForbidIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "empleado-agg", UserRole.Empleado);

        await using var connection = AccessTestSupport.BuildRawConnectionAs(superuserContext, "empleado-agg");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sum(monthly_rent) FROM contracts";

        // No exception: this is the whole point of the test (spec "Aggregate Business
        // Reporting Is Separated By The Application Only").
        await command.ExecuteScalarAsync();
    }

    /// <summary>Spec test 33: confirming an adjustment as ANY authenticated user (here, Admin — test 26 already covers Empleado) stores that user's reference in <c>confirmed_by</c>.</summary>
    [SkippableFact]
    public async Task ConfirmingAnAdjustment_AsAnyAuthenticatedUser_StoresHerAppUserReference()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var adminUserId = await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "admin-adjust", UserRole.Admin);

        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(superuserContext);
        await superuserContext.SaveChangesAsync();

        await using var adminContext = AccessTestSupport.BuildDbContextAs(superuserContext, "admin-adjust");
        var trackedContract = await adminContext.Contracts.SingleAsync(c => c.Id == contract.Id);

        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            trackedContract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_200m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, adminUserId);

        adminContext.RentAdjustments.Add(adjustment);
        await adminContext.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var reloaded = await readContext.RentAdjustments.SingleAsync(a => a.Id == adjustment.Id);
        Assert.Equal((Guid?)adminUserId, reloaded.ConfirmedBy);
    }
}
