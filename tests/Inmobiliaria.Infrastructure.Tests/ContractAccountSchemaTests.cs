using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The database half of cuenta-corriente: the append-only trigger, the accrual uniqueness that
/// settles two readers materialising the same period, the one-account-per-contract rule, the
/// due_day column and its grants. Constraints an in-memory provider cannot enforce.
///
/// Every fact skips (not fails) when Docker is unreachable locally, and the ubuntu-latest CI job
/// always runs them. A skipped test here is an unrun test, never a passing one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractAccountSchemaTests
{
    private readonly PostgresFixture _fixture;

    public ContractAccountSchemaTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// A persisted account on a fresh contract. Reuses the contract seeding the adjustment tests
    /// already own rather than growing a second builder beside it.
    /// </summary>
    private static async Task<ContractAccount> SeedAccountAsync(InmobiliariaDbContext context)
    {
        var (contract, _) = SchemaConstraintTests.SeedContractWithClause(context);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return account;
    }

    /// <summary>
    /// Spec tests 3 and 4: a recorded movement cannot be altered, "including by raw SQL".
    ///
    /// Raw SQL deliberately bypasses both the change tracker and AccountMovement's lack of any
    /// public mutator, so this proves the DATABASE refuses the mutation — not the domain. The
    /// domain is the first layer and this trigger is the one that survives somebody opening a
    /// query window.
    /// </summary>
    [SkippableFact]
    public async Task AppendOnlyTrigger_RejectsRawUpdateAndRawDelete()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        var movement = AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10));
        account.Append(movement);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE account_movements SET amount = 1 WHERE id = {movement.Id}"));

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM account_movements WHERE id = {movement.Id}"));
    }

    /// <summary>
    /// The row is still there, and still says what it said. A trigger that raised but let the
    /// change through would pass the test above and corrupt the ledger.
    /// </summary>
    [SkippableFact]
    public async Task ARefusedMutation_LeavesTheMovementExactlyAsItWas()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        var movement = AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10));
        account.Append(movement);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE account_movements SET amount = 1 WHERE id = {movement.Id}"));

        var amounts = await context.Database
            .SqlQuery<decimal>($"SELECT amount AS \"Value\" FROM account_movements WHERE id = {movement.Id}")
            .ToListAsync();

        Assert.Equal(500_000m, Assert.Single(amounts));
    }

    /// <summary>
    /// Design Decision 1: two readers materialising the same period at the same moment. The
    /// second insert must fail so that caller can re-read — a constraint and a retry, not a lock.
    /// </summary>
    [SkippableFact]
    public async Task TwoAccrualsForTheSamePeriod_AreRefusedByTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        var july = new DateOnly(2026, 7, 1);
        account.Append(AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, july, new DateOnly(2026, 7, 10)));
        await context.SaveChangesAsync();

        // A second reader, writing the same period through a different context — exactly the race
        // the index exists for.
        await using var second = _fixture.CreateDbContext();
        var reloaded = await second.ContractAccounts
            .Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id);

        reloaded.Append(AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, july, new DateOnly(2026, 7, 10)));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    /// <summary>
    /// The same index must NOT forbid a period being corrected twice. The task that specified it
    /// dropped the design's "for accrual movements" qualifier and wrote it over every kind
    /// carrying a period, which would have made this impossible — and would also have rejected a
    /// second payment against one period, which is precisely what a payment plan is.
    /// </summary>
    [SkippableFact]
    public async Task APeriodCanBeCorrectedMoreThanOnce()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        var july = new DateOnly(2026, 7, 1);
        var accrual = AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, july, new DateOnly(2026, 7, 10));
        account.Append(accrual);

        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -30_000m, new DateOnly(2026, 7, 20), accrual.Id, july));
        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -20_000m, new DateOnly(2026, 7, 25), accrual.Id, july));

        await context.SaveChangesAsync();

        var stored = await context.AccountMovements
            .CountAsync(m => m.Kind == MovementKind.Correction && m.Period == july);

        Assert.Equal(2, stored);
    }

    /// <summary>
    /// Spec test 1, database half: each contract has exactly one account. The domain cannot see
    /// this — a second ContractAccount is a valid object on its own — so only the table can.
    /// </summary>
    [SkippableFact]
    public async Task ASecondAccountForTheSameContract_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        context.ContractAccounts.Add(new ContractAccount(Guid.NewGuid(), account.ContractId));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Spec test 2, schema half: no table stores a balance keyed by tenant. What a tenant owes is
    /// summed over their contracts for display and is never written anywhere.
    /// </summary>
    [SkippableFact]
    public async Task NoTableStoresABalanceKeyedByTenant()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        // Any account-like or balance-like table must be keyed by contract, never by party.
        var offenders = await context.Database
            .SqlQuery<string>($"""
                SELECT c.table_name AS "Value"
                FROM information_schema.columns c
                WHERE c.table_schema = 'public'
                  AND (c.table_name LIKE '%account%' OR c.table_name LIKE '%balance%')
                  AND c.column_name IN ('party_id', 'tenant_id')
                """)
            .ToListAsync();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Spec test 37: every contract that predates this migration carries a due day of 10.
    ///
    /// Testcontainers builds the schema from nothing, so there are no genuinely pre-existing rows
    /// to inspect. What makes the claim true is the column's DEFAULT, so that is what is asserted
    /// — plus an insert that omits the column entirely, which is exactly what the migration does
    /// to every existing row.
    /// </summary>
    [SkippableFact]
    public async Task DueDay_DefaultsToTenForARowThatDoesNotSupplyIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var defaults = await context.Database
            .SqlQuery<string>($"""
                SELECT column_default AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'contracts' AND column_name = 'due_day'
                """)
            .ToListAsync();

        Assert.Contains("10", Assert.Single(defaults));

        // And the column is NOT NULL, so no row can escape the default by storing nothing.
        var nullable = await context.Database
            .SqlQuery<string>($"""
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'contracts' AND column_name = 'due_day'
                """)
            .ToListAsync();

        Assert.Equal("NO", Assert.Single(nullable));
    }

    /// <summary>The database refuses a due day that is not a day of the month, independently of the domain.</summary>
    [SkippableFact]
    public async Task TheDatabase_RefusesADueDayOutsideTheMonth()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, _) = SchemaConstraintTests.SeedContractWithClause(context);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contracts SET due_day = 0 WHERE id = {contract.Id}"));

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contracts SET due_day = 32 WHERE id = {contract.Id}"));
    }

    /// <summary>
    /// Spec test 33: both application roles can do exactly what they are meant to on both new
    /// tables, and nothing more. Asked of Postgres's own privilege catalogue, which is the thing
    /// that will actually decide at runtime.
    ///
    /// The point is twofold. Neither table may be silently unreachable — a new table whose grants
    /// arrive in a later migration ships broken. And neither role may hold UPDATE or DELETE on
    /// the ledger: that is the append-only rule enforced a third time, by never granting the
    /// privilege at all.
    /// </summary>
    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task BothRoles_CanReadAndAppendButNeverChangeTheLedger(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        foreach (var table in new[] { "contract_accounts", "account_movements" })
        {
            Assert.True(
                await HasPrivilegeAsync(context, role, table, "SELECT"),
                $"{role} cannot read {table}: the table is unreachable at runtime.");

            Assert.True(
                await HasPrivilegeAsync(context, role, table, "INSERT"),
                $"{role} cannot append to {table}.");

            Assert.False(
                await HasPrivilegeAsync(context, role, table, "UPDATE"),
                $"{role} holds UPDATE on {table}, which an append-only ledger must never grant.");

            Assert.False(
                await HasPrivilegeAsync(context, role, table, "DELETE"),
                $"{role} holds DELETE on {table}, which an append-only ledger must never grant.");
        }
    }

    private static async Task<bool> HasPrivilegeAsync(
        InmobiliariaDbContext context, string role, string table, string privilege)
    {
        // Explicit casts: has_table_privilege is overloaded, and a bare parameter leaves Postgres
        // unable to choose between the name and oid forms.
        var results = await context.Database
            .SqlQuery<bool>(
                $"SELECT has_table_privilege({role}::name, {table}::text, {privilege}::text) AS \"Value\"")
            .ToListAsync();

        return Assert.Single(results);
    }

    /// <summary>
    /// A movement round-trips with every column intact, including the shadow account_id that
    /// AccountMovement deliberately does not carry as a property.
    /// </summary>
    [SkippableFact]
    public async Task AMovement_RoundTripsThroughTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var account = await SeedAccountAsync(context);

        var july = new DateOnly(2026, 7, 1);
        var movement = AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, july, new DateOnly(2026, 7, 10));
        account.Append(movement);
        await context.SaveChangesAsync();

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await fresh.ContractAccounts
            .Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id);

        var stored = Assert.Single(reloaded.Movements);
        Assert.Equal(movement.Id, stored.Id);
        Assert.Equal(MovementKind.RentAccrual, stored.Kind);
        Assert.Equal(500_000m, stored.Amount);
        Assert.Equal(july, stored.Period);
        Assert.Equal(new DateOnly(2026, 7, 10), stored.OccurredOn);
        Assert.Null(stored.CorrectsMovementId);
    }
}
