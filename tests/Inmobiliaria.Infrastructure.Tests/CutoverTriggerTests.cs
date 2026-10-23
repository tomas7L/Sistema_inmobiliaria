using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// ui-de-cobranza spec tests 35 to 39 (Requirement 22, design D6 and section 6.2): changing
/// <c>contracts.cutover_date</c> is refused by PostgreSQL to anyone outside <c>inmobiliaria_admin</c>, on
/// top of the existing lock after a movement. Every statement here runs as a real login role, never as
/// the container's superuser, because a superuser is a member of every role and would pass the check.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CutoverTriggerTests
{
    private const string RaiseException = "P0001";
    private const string PreviousMigration = "20261015204506_AddCollectionsAndRecibos";
    private const string BeforeTheCutoverTrigger = "20261010145210_AddContractAccounts";

    private const string FunctionBodySql =
        "SELECT prosrc FROM pg_proc WHERE proname = 'reject_cutover_change_after_movement'";

    private readonly PostgresFixture _fixture;

    public CutoverTriggerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid ContractId, Guid AccountId)> SeedAccountAsync(InmobiliariaDbContext context)
    {
        var (contract, _) = SchemaConstraintTests.SeedContractWithClause(context);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return (contract.Id, account.Id);
    }

    private static Task<int> InsertMovementAsync(InmobiliariaDbContext context, Guid accountId) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES ({Guid.NewGuid()}, {accountId}, 'OpeningBalance', 0, DATE '2026-07-10', NULL)
            """);

    private static async Task<NpgsqlConnection> OpenAsAsync(InmobiliariaDbContext superuser, string groupRole)
    {
        var (roleName, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, roleName, password);
        await connection.OpenAsync();
        return connection;
    }

    private static Task<int> SetCutoverAsync(NpgsqlConnection connection, Guid contractId, DateOnly date) =>
        ReciboTestSupport.ExecAsync(
            connection,
            "UPDATE contracts SET cutover_date = @date WHERE id = @id",
            ("date", date), ("id", contractId));

    private static async Task<DateOnly?> StoredCutoverAsync(InmobiliariaDbContext superuser, Guid contractId)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(superuser);
        var value = await ReciboTestSupport.ScalarAsync<object>(
            connection, "SELECT cutover_date FROM contracts WHERE id = @id", ("id", contractId));
        return value is DateOnly date ? date : null;
    }

    /// <summary>Spec test 35: an Admin changes the date and succeeds; an Empleado's raw UPDATE is refused.</summary>
    [SkippableFact]
    public async Task AnAdminChangesTheCutoverDate_AndAnEmpleadoIsRefusedByTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuser = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAccountAsync(superuser);
        await using var admin = await OpenAsAsync(superuser, "inmobiliaria_admin");
        await using var empleado = await OpenAsAsync(superuser, "inmobiliaria_empleado");

        await SetCutoverAsync(admin, contractId, new DateOnly(2026, 6, 30));
        await SetCutoverAsync(admin, contractId, new DateOnly(2026, 5, 31));
        Assert.Equal(new DateOnly(2026, 5, 31), await StoredCutoverAsync(superuser, contractId));

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            SetCutoverAsync(empleado, contractId, new DateOnly(2026, 6, 30)));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("contracts_cutover_admin_only", refused.ConstraintName);
        Assert.Equal(new DateOnly(2026, 5, 31), await StoredCutoverAsync(superuser, contractId));
    }

    /// <summary>
    /// Spec test 36: an Empleado still ends a contract, updates the honorarios percentage, and updates a
    /// contract naming <c>cutover_date</c> without changing it.
    /// </summary>
    [SkippableFact]
    public async Task AnEmpleadosOtherUpdates_StillWork()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuser = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAccountAsync(superuser);
        await using var admin = await OpenAsAsync(superuser, "inmobiliaria_admin");
        await SetCutoverAsync(admin, contractId, new DateOnly(2026, 6, 30));
        await using var empleado = await OpenAsAsync(superuser, "inmobiliaria_empleado");

        await ReciboTestSupport.ExecAsync(
            empleado,
            "UPDATE contracts SET honorarios_percentage = 9 WHERE id = @id",
            ("id", contractId));
        await SetCutoverAsync(empleado, contractId, new DateOnly(2026, 6, 30));
        await ReciboTestSupport.ExecAsync(
            empleado,
            """
            UPDATE contracts
               SET status = 'Ended', end_reason = 'Expiry', actual_end_date = DATE '2026-10-01'
             WHERE id = @id
            """,
            ("id", contractId));

        await using var connection = await ReciboTestSupport.OpenAsync(superuser);
        Assert.Equal(
            "Ended|9|2026-06-30",
            await ReciboTestSupport.ScalarAsync<string>(
                connection,
                "SELECT status || '|' || honorarios_percentage::int || '|' || cutover_date FROM contracts WHERE id = @id",
                ("id", contractId)));
    }

    /// <summary>Spec test 37 (and 34, raw SQL half): the lock after a movement still refuses both roles.</summary>
    [SkippableFact]
    public async Task TheLockAfterAMovement_StillRefusesBothRoles()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuser = _fixture.CreateDbContext();
        var (contractId, accountId) = await SeedAccountAsync(superuser);
        await InsertMovementAsync(superuser, accountId);
        await using var admin = await OpenAsAsync(superuser, "inmobiliaria_admin");
        await using var empleado = await OpenAsAsync(superuser, "inmobiliaria_empleado");

        foreach (var connection in new[] { admin, empleado })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                SetCutoverAsync(connection, contractId, new DateOnly(2026, 6, 30)));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("contracts_cutover_locked", refused.ConstraintName);
        }

        Assert.Null(await StoredCutoverAsync(superuser, contractId));
    }

    /// <summary>
    /// Spec test 38, the named limit: an Empleado can insert a contract that carries a cutover date, and
    /// is then refused when changing it.
    /// </summary>
    [SkippableFact]
    public async Task AnEmpleadoInsertsAContractWithADate_AndCannotChangeIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuser = _fixture.CreateDbContext();
        await using var empleado = await OpenAsAsync(superuser, "inmobiliaria_empleado");
        var contractId = Guid.NewGuid();

        await ReciboTestSupport.ExecAsync(
            empleado,
            """
            INSERT INTO contracts (id, start_date, nominal_end_date, monthly_rent, honorarios_percentage, status, due_day, cutover_date)
            VALUES (@id, DATE '2025-01-01', DATE '2026-12-31', 450000, 8, 'Active', 5, DATE '2026-06-30')
            """,
            ("id", contractId));
        Assert.Equal(new DateOnly(2026, 6, 30), await StoredCutoverAsync(superuser, contractId));

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            SetCutoverAsync(empleado, contractId, new DateOnly(2026, 5, 31)));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("contracts_cutover_admin_only", refused.ConstraintName);
        Assert.Equal(new DateOnly(2026, 6, 30), await StoredCutoverAsync(superuser, contractId));
    }

    /// <summary>
    /// Spec test 39: <c>Down</c> restores the previous function body, verbatim, and changes no data. The
    /// previous body is not copied into this test: it is rebuilt by rolling back past the migration that
    /// first created it and applying that one again, so the comparison is against its own source.
    /// </summary>
    [SkippableFact]
    public async Task TheMigrationsDown_RestoresThePreviousFunctionBody()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = database.Context.GetService<IMigrator>();
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        var hardened = await ReciboTestSupport.ScalarAsync<string>(connection, FunctionBodySql);
        Assert.Contains("contracts_cutover_admin_only", hardened, StringComparison.Ordinal);

        var (contractId, _) = await SeedAccountAsync(database.Context);
        const string RowSql = "SELECT to_jsonb(c)::text FROM contracts c WHERE id = @id";
        var rowBefore = await ReciboTestSupport.ScalarAsync<string>(connection, RowSql, ("id", contractId));

        await migrator.MigrateAsync(PreviousMigration);
        var restored = await ReciboTestSupport.ScalarAsync<string>(connection, FunctionBodySql);
        Assert.Equal(rowBefore, await ReciboTestSupport.ScalarAsync<string>(connection, RowSql, ("id", contractId)));

        await migrator.MigrateAsync(BeforeTheCutoverTrigger);
        await migrator.MigrateAsync(PreviousMigration);
        var original = await ReciboTestSupport.ScalarAsync<string>(connection, FunctionBodySql);

        Assert.Equal(original, restored);
        Assert.DoesNotContain("pg_has_role", restored, StringComparison.Ordinal);
    }
}
