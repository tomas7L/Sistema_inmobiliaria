using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Migration A of cobranza-y-recibos against a real engine: the kinds the ledger accepts, the sign
/// and period rules, the one-charge-per-period indexes, the fee-plan columns and the contracts
/// trigger that makes the plan and the cutover date one-way. (Spec Tests 57 and 64, plus the
/// unnumbered constraint tests of task 2.6.)
///
/// Every refusal here is a raw INSERT or UPDATE, because the point is the database refusing what the
/// domain would already have refused: raw SQL is the path that bypasses the domain.
///
/// Every fact skips (not fails) when Docker is unreachable locally, and the ubuntu-latest CI job
/// always runs them. A skipped test here is an unrun test, never a passing one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerConstraintTests
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string RaiseException = "P0001";

    private readonly PostgresFixture _fixture;

    public LedgerConstraintTests(PostgresFixture fixture)
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

    private static Task<int> InsertMovementAsync(
        InmobiliariaDbContext context,
        Guid accountId,
        string kind,
        decimal amount,
        string? period,
        Guid? id = null)
    {
        // The period is passed as text and cast, so one helper can insert a null period too.
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES ({id ?? Guid.NewGuid()}, {accountId}, {kind}, {amount}, DATE '2026-07-10', {period}::date)
            """);
    }

    /// <summary>
    /// Executes a statement assembled from test-controlled constants (a generated database name, a
    /// column assignment taken from an InlineData row). Never user input, so the injection warning
    /// EF raises for string-built SQL does not apply; the one suppression lives here.
    /// </summary>
    private static Task<int> ExecuteConstantSqlAsync(InmobiliariaDbContext context, string sql)
    {
#pragma warning disable EF1002
        return context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private static async Task<PostgresException> RefusedAsync(Func<Task> statement) =>
        await Assert.ThrowsAsync<PostgresException>(statement);

    /// <summary>
    /// Spec test 57: the database accepts the two new kinds and refuses to update or delete them.
    /// </summary>
    [SkippableFact]
    public async Task TheDatabase_AcceptsPaymentAndRecargoCharged_AndRefusesToChangeThem()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);
        var paymentId = Guid.NewGuid();
        var recargoId = Guid.NewGuid();

        await InsertMovementAsync(context, accountId, "Payment", -500_000m, "2026-07-01", paymentId);
        await InsertMovementAsync(context, accountId, "RecargoCharged", 7_500m, "2026-07-01", recargoId);

        foreach (var id in new[] { paymentId, recargoId })
        {
            await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE account_movements SET amount = 1 WHERE id = {id}"));
            await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM account_movements WHERE id = {id}"));
        }

        var stored = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM account_movements WHERE account_id = {accountId}")
            .ToListAsync();
        Assert.Equal(2, Assert.Single(stored));
    }

    [SkippableFact]
    public async Task AKindOutsideTheList_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        var refused = await RefusedAsync(() => InsertMovementAsync(context, accountId, "Bogus", 100m, "2026-07-01"));

        Assert.Equal(CheckViolation, refused.SqlState);
        Assert.Equal("ck_account_movements_kind", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task APayment_WithAPositiveOrZeroAmount_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        foreach (var amount in new[] { 1m, 0m })
        {
            var refused = await RefusedAsync(() =>
                InsertMovementAsync(context, accountId, "Payment", amount, "2026-07-01"));

            Assert.Equal(CheckViolation, refused.SqlState);
            Assert.Equal("ck_account_movements_payment_sign", refused.ConstraintName);
        }
    }

    [SkippableFact]
    public async Task ARecargoCharged_WithANegativeOrZeroAmount_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        foreach (var amount in new[] { -1m, 0m })
        {
            var refused = await RefusedAsync(() =>
                InsertMovementAsync(context, accountId, "RecargoCharged", amount, "2026-07-01"));

            Assert.Equal(CheckViolation, refused.SqlState);
            Assert.Equal("ck_account_movements_recargo_charged_sign", refused.ConstraintName);
        }
    }

    /// <summary>
    /// The period-required rule now covers every kind that is about one month, not only the rent.
    /// </summary>
    [SkippableTheory]
    [InlineData("RentAccrual", 500000)]
    [InlineData("ContractualFeeInstalment", 120000)]
    [InlineData("Payment", -500000)]
    [InlineData("RecargoCharged", 7500)]
    public async Task AMonthlyKind_WithNoPeriod_IsRefused(string kind, int amount)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        var refused = await RefusedAsync(() => InsertMovementAsync(context, accountId, kind, amount, period: null));

        Assert.Equal(CheckViolation, refused.SqlState);
        Assert.Equal("ck_account_movements_period_required", refused.ConstraintName);
    }

    /// <summary>A non-zero opening balance needs its month; a zero may stand without one.</summary>
    [SkippableFact]
    public async Task ANonZeroOpeningBalance_WithNoPeriod_IsRefused_ButAZeroOneIsAccepted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        var refused = await RefusedAsync(() =>
            InsertMovementAsync(context, accountId, "OpeningBalance", 160_000m, period: null));
        Assert.Equal(CheckViolation, refused.SqlState);
        Assert.Equal("ck_account_movements_opening_balance_period", refused.ConstraintName);

        await InsertMovementAsync(context, accountId, "OpeningBalance", 0m, period: null);
    }

    /// <summary>
    /// One charge per period across rent and opening balance, in either order (design section 5):
    /// which is also "a period holding an opening balance takes no accrual".
    /// </summary>
    [SkippableTheory]
    [InlineData("RentAccrual", "OpeningBalance")]
    [InlineData("OpeningBalance", "RentAccrual")]
    [InlineData("RentAccrual", "RentAccrual")]
    [InlineData("OpeningBalance", "OpeningBalance")]
    public async Task ASecondChargeForOnePeriod_AcrossRentAndOpeningBalance_IsRefused(string first, string second)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        await InsertMovementAsync(context, accountId, first, 500_000m, "2026-07-01");

        var refused = await RefusedAsync(() => InsertMovementAsync(context, accountId, second, 90_000m, "2026-07-01"));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_account_movements_period_charge", refused.ConstraintName);
    }

    /// <summary>The same index is per account and per period: another month and another account are unaffected.</summary>
    [SkippableFact]
    public async Task TheChargeIndex_DoesNotRefuseAnotherMonthOrAnotherAccount()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);
        var (_, otherAccountId) = await SeedAccountAsync(context);

        await InsertMovementAsync(context, accountId, "RentAccrual", 500_000m, "2026-07-01");
        await InsertMovementAsync(context, accountId, "RentAccrual", 500_000m, "2026-08-01");
        await InsertMovementAsync(context, otherAccountId, "RentAccrual", 500_000m, "2026-07-01");
    }

    [SkippableFact]
    public async Task ASecondInstalmentForOnePeriod_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        await InsertMovementAsync(context, accountId, "ContractualFeeInstalment", 120_000m, "2026-07-01");

        var refused = await RefusedAsync(() =>
            InsertMovementAsync(context, accountId, "ContractualFeeInstalment", 120_000m, "2026-07-01"));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_account_movements_instalment_period", refused.ConstraintName);

        // A different month takes its own instalment.
        await InsertMovementAsync(context, accountId, "ContractualFeeInstalment", 120_000m, "2026-08-01");
    }

    [SkippableFact]
    public async Task ASecondPeriodlessOpeningBalance_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        await InsertMovementAsync(context, accountId, "OpeningBalance", 0m, period: null);

        var refused = await RefusedAsync(() =>
            InsertMovementAsync(context, accountId, "OpeningBalance", 0m, period: null));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_account_movements_opening_zero", refused.ConstraintName);
    }

    /// <summary>
    /// Several payments for one period are not forbidden by any index: a voided payment is paid again,
    /// and the attempt rule that guards it arrives with the collection tables.
    /// </summary>
    [SkippableFact]
    public async Task SeveralPaymentsForOnePeriod_AreNotRefusedByTheLedger()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, accountId) = await SeedAccountAsync(context);

        await InsertMovementAsync(context, accountId, "Payment", -500_000m, "2026-07-01");
        await InsertMovementAsync(context, accountId, "Payment", -500_000m, "2026-07-01");
    }

    /// <summary>
    /// Spec test 61, database half: an amount without a count, or the reverse, a non-positive amount
    /// and a count outside 1 to 6 are each refused by their own check.
    /// </summary>
    [SkippableTheory]
    [InlineData("contractual_fee_amount = 100000, contractual_fee_instalments = NULL", "ck_contracts_contractual_fee_both_or_neither")]
    [InlineData("contractual_fee_amount = NULL, contractual_fee_instalments = 3", "ck_contracts_contractual_fee_both_or_neither")]
    [InlineData("contractual_fee_amount = 0, contractual_fee_instalments = 3", "ck_contracts_contractual_fee_amount_positive")]
    [InlineData("contractual_fee_amount = -1, contractual_fee_instalments = 3", "ck_contracts_contractual_fee_amount_positive")]
    [InlineData("contractual_fee_amount = 100000, contractual_fee_instalments = 0", "ck_contracts_contractual_fee_instalments_range")]
    [InlineData("contractual_fee_amount = 100000, contractual_fee_instalments = 7", "ck_contracts_contractual_fee_instalments_range")]
    public async Task AnInvalidFeePlan_IsRefusedByTheDatabase(string assignment, string expectedConstraint)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAccountAsync(context);

        var refused = await RefusedAsync(() =>
            ExecuteConstantSqlAsync(
                context, $"UPDATE contracts SET {assignment} WHERE id = '{contractId}'"));

        Assert.Equal(CheckViolation, refused.SqlState);
        Assert.Equal(expectedConstraint, refused.ConstraintName);
    }

    [SkippableFact]
    public async Task AValidFeePlan_IsAcceptedByTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAccountAsync(context);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
            WHERE id = {contractId}
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = NULL, contractual_fee_instalments = NULL
            WHERE id = {contractId}
            """);
    }

    /// <summary>Decision 32, database half: no plan change once the account holds an instalment.</summary>
    [SkippableFact]
    public async Task TheFeePlan_CannotBeChangedOnceAnInstalmentExists_EvenByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, accountId) = await SeedAccountAsync(context);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
            WHERE id = {contractId}
            """);
        await InsertMovementAsync(context, accountId, "ContractualFeeInstalment", 120_000m, "2026-07-01");

        var refused = await RefusedAsync(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE contracts SET contractual_fee_amount = 90000, contractual_fee_instalments = 3
                WHERE id = {contractId}
                """));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("contracts_fee_plan_locked", refused.ConstraintName);

        var plan = await context.Database
            .SqlQuery<decimal>($"SELECT contractual_fee_amount AS \"Value\" FROM contracts WHERE id = {contractId}")
            .ToListAsync();
        Assert.Equal(120_000m, Assert.Single(plan));
    }

    /// <summary>
    /// The trigger is about changing the plan, not about the statement naming the column: setting the
    /// same value, and updating an unrelated column, are both fine with an instalment on the account.
    /// </summary>
    [SkippableFact]
    public async Task TheFeePlanTrigger_LeavesANoOpAndAnUnrelatedUpdateAlone()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, accountId) = await SeedAccountAsync(context);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
            WHERE id = {contractId}
            """);
        await InsertMovementAsync(context, accountId, "ContractualFeeInstalment", 120_000m, "2026-07-01");

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
            WHERE id = {contractId}
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET monthly_rent = 777000 WHERE id = {contractId}
            """);
    }

    /// <summary>
    /// Review finding R3-fee-plan-after-rent-silently-dropped: the lock is on ANY movement, not only
    /// an instalment, so a plan cannot be entered after rent was written.
    /// </summary>
    [SkippableFact]
    public async Task TheFeePlan_CannotBeSet_OnceTheAccountHoldsOnlyRent_EvenByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, accountId) = await SeedAccountAsync(context);
        await InsertMovementAsync(context, accountId, "RentAccrual", 500_000m, "2026-07-01");

        var refused = await RefusedAsync(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
                WHERE id = {contractId}
                """));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("contracts_fee_plan_locked", refused.ConstraintName);
    }

    /// <summary>While the account holds nothing, the plan can be set and changed.</summary>
    [SkippableFact]
    public async Task TheFeePlan_CanBeChanged_WhileTheAccountIsEmpty()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAccountAsync(context);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 120000, contractual_fee_instalments = 6
            WHERE id = {contractId}
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contracts SET contractual_fee_amount = 90000, contractual_fee_instalments = 3
            WHERE id = {contractId}
            """);
    }

    /// <summary>Design D17, database half: the cutover date is one-way once the account holds any movement.</summary>
    [SkippableFact]
    public async Task TheCutoverDate_CannotBeChangedOnceTheAccountHoldsAMovement_EvenByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contractId, accountId) = await SeedAccountAsync(context);

        // Allowed while the account is empty, and movable until something is written.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contracts SET cutover_date = DATE '2026-06-30' WHERE id = {contractId}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contracts SET cutover_date = DATE '2026-05-31' WHERE id = {contractId}");

        await InsertMovementAsync(context, accountId, "OpeningBalance", 0m, period: null);

        var refused = await RefusedAsync(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contracts SET cutover_date = DATE '2026-06-30' WHERE id = {contractId}"));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("contracts_cutover_locked", refused.ConstraintName);

        // Naming the column without changing it is not a change.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contracts SET cutover_date = DATE '2026-05-31' WHERE id = {contractId}");
    }

    /// <summary>
    /// Spec test 64: contracts that existed before the migration carry no plan and no cutover date, and
    /// nothing else about them moves. Done for real: a second database is migrated only as far as the
    /// previous migration, a contract row is inserted, and the rest of the migrations are applied.
    /// </summary>
    [SkippableFact]
    public async Task ExistingContracts_CarryNoPlanAndNoCutover_AndNoOtherColumnMoves()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var admin = _fixture.CreateDbContext();
        var database = $"migration_a_{Guid.NewGuid():N}";
        await ExecuteConstantSqlAsync(admin, $"CREATE DATABASE {database}");

        var connection = new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()!)
        {
            Database = database,
            Pooling = false,
        };

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options;

        try
        {
            await using var context = new InmobiliariaDbContext(options);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync("20261010145210_AddContractAccounts");

            var contractId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO contracts (id, start_date, nominal_end_date, monthly_rent, honorarios_percentage, status, due_day)
                VALUES ({contractId}, DATE '2025-01-01', DATE '2026-12-31', 450000, 8, 'Active', 5)
                """);

            var before = await context.Database
                .SqlQuery<string>($"SELECT to_jsonb(c)::text AS \"Value\" FROM contracts c WHERE id = {contractId}")
                .SingleAsync();

            await migrator.MigrateAsync();

            var afterWithoutNewColumns = await context.Database
                .SqlQuery<string>($"""
                    SELECT (to_jsonb(c) - 'contractual_fee_amount' - 'contractual_fee_instalments' - 'cutover_date')::text AS "Value"
                    FROM contracts c WHERE id = {contractId}
                    """)
                .SingleAsync();
            var newColumnsAreNull = await context.Database
                .SqlQuery<bool>($"""
                    SELECT (c.contractual_fee_amount IS NULL
                        AND c.contractual_fee_instalments IS NULL
                        AND c.cutover_date IS NULL) AS "Value"
                    FROM contracts c WHERE id = {contractId}
                    """)
                .SingleAsync();

            Assert.True(newColumnsAreNull);
            Assert.Equal(
                System.Text.Json.JsonDocument.Parse(before).RootElement.GetRawText(),
                System.Text.Json.JsonDocument.Parse(afterWithoutNewColumns).RootElement.GetRawText());
        }
        finally
        {
            await ExecuteConstantSqlAsync(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    /// <summary>The three columns are nullable, with the types the design names.</summary>
    [SkippableFact]
    public async Task TheNewContractColumns_AreNullableWithTheDesignedTypes()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name || ':' || data_type || ':' || is_nullable || ':' ||
                       coalesce(numeric_precision::text, '') || ',' || coalesce(numeric_scale::text, '') AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'contracts'
                  AND column_name IN ('contractual_fee_amount', 'contractual_fee_instalments', 'cutover_date')
                ORDER BY column_name
                """)
            .ToListAsync();

        Assert.Equal(
            [
                "contractual_fee_amount:numeric:YES:14,2",
                "contractual_fee_instalments:smallint:YES:16,0",
                "cutover_date:date:YES:,",
            ],
            columns);
    }
}
