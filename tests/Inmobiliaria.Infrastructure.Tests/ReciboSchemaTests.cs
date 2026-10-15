using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Migration B of cobranza-y-recibos against a real engine: the collection and recibo tables, the
/// stamps the database writes itself, the append-only rule, the recibo counter and the cutover
/// number. (Spec Tests 14, 32, 33, 34, 35, 48, 51 and 66, plus the unnumbered trigger tests of
/// task 5.4.)
///
/// Every refusal here is a raw INSERT or UPDATE, because the point is the database refusing what the
/// domain would already have refused: raw SQL is the path that bypasses the domain.
///
/// Anything that depends on the state of the single counter row, or on there being no recibo yet,
/// runs on its own freshly migrated database (<see cref="IsolatedDatabase"/>): the shared fixture
/// database accumulates recibos from other tests, and the counter is one row for the whole system.
///
/// Every fact skips (not fails) when Docker is unreachable locally, and the ubuntu-latest CI job
/// always runs them. A skipped test here is an unrun test, never a passing one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReciboSchemaTests
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string RaiseException = "P0001";

    private static readonly DateOnly July = new(2026, 7, 1);

    private readonly PostgresFixture _fixture;

    public ReciboSchemaTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Down leaves nothing behind: the five tables and every trigger function are gone, the earlier
    /// schema is untouched, and the migration applies again afterwards.
    /// </summary>
    [SkippableFact]
    public async Task TheMigration_RollsBackCleanly_AndAppliesAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = database.Context.GetService<IMigrator>();
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        const string ObjectsSql =
            """
            SELECT (SELECT count(*) FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN ('collections', 'recibos', 'recibo_lines', 'recibo_annulments', 'recibo_numbering'))
                 + (SELECT count(*) FROM pg_proc
                    WHERE proname IN ('reject_recibo_table_mutation', 'stamp_collection',
                                      'stamp_recibo_annulment', 'guard_recibo_numbering'))
            """;

        Assert.Equal(9L, await ReciboTestSupport.ScalarAsync<long>(connection, ObjectsSql));

        await migrator.MigrateAsync("20261014204601_AddFeePlanCutoverAndLedgerConstraints");
        Assert.Equal(0L, await ReciboTestSupport.ScalarAsync<long>(connection, ObjectsSql));
        Assert.Equal(1L, await ReciboTestSupport.ScalarAsync<long>(connection, "SELECT count(*) FROM pg_tables WHERE tablename = 'account_movements'"));

        await migrator.MigrateAsync();
        Assert.Equal(9L, await ReciboTestSupport.ScalarAsync<long>(connection, ObjectsSql));
    }

    // ---- Test 14: the collection is stamped by the database ---------------------------------

    /// <summary>
    /// Spec test 14, first half: a recorded-at and a recorded-by supplied on INSERT are ignored; the
    /// database writes its own instant and the connected user.
    /// </summary>
    [SkippableFact]
    public async Task ACollection_IgnoresASuppliedRecordedAtAndRecordedBy()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var accountId = await ReciboTestSupport.SeedAccountAsync(context);
        var (paymentId, recargoId) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, accountId, July);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);

        await ReciboTestSupport.ExecAsync(
            connection,
            """
            INSERT INTO collections
                (id, account_id, period, attempt, received_on, recorded_at, recorded_by,
                 payment_movement_id, recargo_movement_id)
            VALUES (@id, @account, @period, 1, @received, TIMESTAMPTZ '2000-01-01 00:00:00+00', 'somebody_else',
                    @payment, @recargo)
            """,
            ("id", Guid.NewGuid()), ("account", accountId), ("period", July), ("received", today),
            ("payment", paymentId), ("recargo", recargoId));

        var stamped = await ReciboTestSupport.ScalarAsync<bool>(
            connection,
            """
            SELECT recorded_at > now() - interval '1 minute' AND recorded_at <= now() + interval '1 minute'
                   AND recorded_by = current_user
            FROM collections WHERE account_id = @account
            """,
            ("account", accountId));

        Assert.True(stamped);
    }

    /// <summary>
    /// Spec test 14, second half: neither stamped column can be changed afterwards, by raw SQL either,
    /// and the row cannot be deleted.
    /// </summary>
    [SkippableFact]
    public async Task ACollection_CannotBeUpdatedOrDeleted_ByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July);

        foreach (var statement in new[]
                 {
                     "UPDATE collections SET recorded_at = now() WHERE id = @id",
                     "UPDATE collections SET recorded_by = 'somebody_else' WHERE id = @id",
                     "UPDATE collections SET received_on = received_on WHERE id = @id",
                     "DELETE FROM collections WHERE id = @id",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.ExecAsync(connection, statement, ("id", paid.CollectionId)));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("collections_append_only", refused.ConstraintName);
        }
    }

    /// <summary>
    /// Spec test 14, third part, and the first of the "constants agree" checks (task 5.4): the back-date
    /// bound in the trigger is the figure <see cref="CollectionRules.BackDateBoundDays"/> holds. Exactly
    /// the bound is accepted; one more day is refused; a future date is refused.
    /// </summary>
    [SkippableFact]
    public async Task TheReceivedDateBound_IsTheOneTheDomainConstantSays()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);

        // Each attempt gets its own account: a refusal must be about the date and nothing else.
        async Task<PostgresException?> TryAsync(DateOnly receivedOn)
        {
            var accountId = await ReciboTestSupport.SeedAccountAsync(context);
            var (paymentId, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, accountId, July);

            try
            {
                await ReciboTestSupport.InsertCollectionAsync(
                    connection, null, Guid.NewGuid(), accountId, July, 1, receivedOn, paymentId, null);
                return null;
            }
            catch (PostgresException refused)
            {
                return refused;
            }
        }

        Assert.Null(await TryAsync(today));
        Assert.Null(await TryAsync(today.AddDays(-CollectionRules.BackDateBoundDays)));

        var tooOld = await TryAsync(today.AddDays(-(CollectionRules.BackDateBoundDays + 1)));
        Assert.NotNull(tooOld);
        Assert.Equal(RaiseException, tooOld.SqlState);
        Assert.Equal("collections_received_on_bound", tooOld.ConstraintName);

        var future = await TryAsync(today.AddDays(1));
        Assert.NotNull(future);
        Assert.Equal(RaiseException, future.SqlState);
        Assert.Equal("collections_received_on_future", future.ConstraintName);
    }

    [SkippableFact]
    public async Task ACollection_RefusesAPeriodThatIsNotTheFirstOfAMonth()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var accountId = await ReciboTestSupport.SeedAccountAsync(context);
        var (paymentId, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, accountId, July);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), accountId, new DateOnly(2026, 7, 15), 1, today, paymentId, null));

        Assert.Equal(CheckViolation, refused.SqlState);
        Assert.Equal("ck_collections_period_first_of_month", refused.ConstraintName);
    }

    // ---- Unnumbered: the attempt rule and the live-attempt index -----------------------------

    [SkippableFact]
    public async Task AnAttemptThatIsNotThePreviousPlusOne_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var accountId = await ReciboTestSupport.SeedAccountAsync(context);

        // A raw INSERT claiming attempt 5 for a period with no collection, and attempt 0.
        foreach (var attempt in new[] { 5, 0 })
        {
            var (paymentId, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, accountId, July);

            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.InsertCollectionAsync(
                    connection, null, Guid.NewGuid(), accountId, July, attempt, today, paymentId, null));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("collections_attempt_rule", refused.ConstraintName);
        }
    }

    [SkippableFact]
    public async Task ASecondCollection_WhileTheFirstIsLive_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July);
        var (secondPayment, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, paid.AccountId, July);

        // Attempt 2 is the next number, but attempt 1 has no annulment: still live.
        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), paid.AccountId, July, 2, today, secondPayment, null));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("collections_attempt_rule", refused.ConstraintName);

        // The same attempt again is refused by the rule too: it is not previous + 1.
        var repeated = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), paid.AccountId, July, 1, today, secondPayment, null));

        Assert.Equal("collections_attempt_rule", repeated.ConstraintName);
    }

    [SkippableFact]
    public async Task ACollection_AfterTheEarlierOneIsAnnulled_IsAcceptedAsTheNextAttempt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July);

        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, "Cobrado por error");
        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.PropietarioId, "Cobrado por error");
        var (secondPayment, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, paid.AccountId, July);

        await ReciboTestSupport.InsertCollectionAsync(
            connection, null, Guid.NewGuid(), paid.AccountId, July, 2, today, secondPayment, null);

        var attempts = await ReciboTestSupport.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM collections WHERE account_id = @account AND period = @period",
            ("account", paid.AccountId), ("period", July));
        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// Design D15: voiding always annuls the pair, and a collection is live while ANY of its recibos
    /// has no annulment. With only the Locatario copy annulled a new attempt is refused; once the
    /// Propietario copy is annulled too it is accepted.
    /// </summary>
    [SkippableFact]
    public async Task ACollection_StaysLive_WhileOnlyOneOfItsRecibosIsAnnulled()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July);
        var (secondPayment, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, paid.AccountId, July);

        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, "Cobrado por error");

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), paid.AccountId, July, 2, today, secondPayment, null));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("collections_attempt_rule", refused.ConstraintName);

        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.PropietarioId, "Cobrado por error");

        await ReciboTestSupport.InsertCollectionAsync(
            connection, null, Guid.NewGuid(), paid.AccountId, July, 2, today, secondPayment, null);

        Assert.Equal(
            2L,
            await ReciboTestSupport.ScalarAsync<long>(
                connection,
                "SELECT count(*) FROM collections WHERE account_id = @account AND period = @period",
                ("account", paid.AccountId), ("period", July)));
    }

    /// <summary>
    /// A collection with no recibo at all is still live (nothing annuls it), so it keeps blocking a
    /// new attempt, as it did before.
    /// </summary>
    [SkippableFact]
    public async Task ACollectionWithNoRecibos_StaysLive()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var seed = await ReciboTestSupport.SeedCollectionOnlyAsync(context, connection, July);
        var (secondPayment, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(connection, seed.AccountId, July);

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertCollectionAsync(
                connection, null, Guid.NewGuid(), seed.AccountId, July, 2, today, secondPayment, null));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("collections_attempt_rule", refused.ConstraintName);
    }

    /// <summary>
    /// The unique index on (account, period, attempt) is what stops two operators who both computed
    /// attempt 1 at once: the trigger sees neither row, the second INSERT waits on the first and fails
    /// when it commits.
    /// </summary>
    [SkippableFact]
    public async Task TwoSessionsClaimingTheSameAttempt_CollideOnTheLiveAttemptIndex()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var first = await ReciboTestSupport.OpenAsync(context);
        await using var second = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(first);
        var accountId = await ReciboTestSupport.SeedAccountAsync(context);
        var (paymentOne, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(first, accountId, July);
        var (paymentTwo, _) = await ReciboTestSupport.InsertSettlementMovementsAsync(first, accountId, July);

        await using var firstTransaction = await first.BeginTransactionAsync();
        await using var secondTransaction = await second.BeginTransactionAsync();

        await ReciboTestSupport.InsertCollectionAsync(
            first, firstTransaction, Guid.NewGuid(), accountId, July, 1, today, paymentOne, null);

        var losing = ReciboTestSupport.InsertCollectionAsync(
            second, secondTransaction, Guid.NewGuid(), accountId, July, 1, today, paymentTwo, null);

        await Task.Delay(500);
        Assert.False(losing.IsCompleted, "The second INSERT should be waiting on the first one's index entry.");

        await firstTransaction.CommitAsync();

        var refused = await Assert.ThrowsAsync<PostgresException>(() => losing);
        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_collections_live_attempt", refused.ConstraintName);
    }

    // ---- Test 32 and the recibo shape -------------------------------------------------------

    /// <summary>Spec test 32: the database refuses a duplicate recibo number.</summary>
    [SkippableFact]
    public async Task ADuplicateReciboNumber_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var number = ReciboTestSupport.RandomNumber();
        var first = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, number);
        var other = await ReciboTestSupport.SeedCollectionOnlyAsync(context, connection, July);

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertReciboAsync(
                connection, null, Guid.NewGuid(), other.CollectionId, number, "Locatario", July, other.ReceivedOn));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_recibos_number", refused.ConstraintName);
        Assert.NotEqual(first.CollectionId, other.CollectionId);
    }

    [SkippableFact]
    public async Task ACollection_HasOneReciboPerCopy_AndOnlyTheTwoCopies()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());

        var twice = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertReciboAsync(
                connection, null, Guid.NewGuid(), paid.CollectionId, ReciboTestSupport.RandomNumber(), "Locatario",
                July, paid.ReceivedOn));
        Assert.Equal(UniqueViolation, twice.SqlState);
        Assert.Equal("ux_recibos_collection_copy", twice.ConstraintName);

        var unknownCopy = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertReciboAsync(
                connection, null, Guid.NewGuid(), paid.CollectionId, ReciboTestSupport.RandomNumber(), "Inquilino",
                July, paid.ReceivedOn));
        Assert.Equal(CheckViolation, unknownCopy.SqlState);
        Assert.Equal("ck_recibos_copy", unknownCopy.ConstraintName);
    }

    [SkippableFact]
    public async Task AReciboNumber_BelowOne_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July);

        foreach (var number in new[] { 0, -5 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.InsertReciboAsync(
                    connection, null, Guid.NewGuid(), paid.CollectionId, number, "Locatario", July, paid.ReceivedOn));

            Assert.Equal(CheckViolation, refused.SqlState);
            Assert.Equal("ck_recibos_number_positive", refused.ConstraintName);
        }
    }

    // ---- Unnumbered: the lines ---------------------------------------------------------------

    [SkippableFact]
    public async Task AReciboLine_TakesOnePositionAndOneKind_OnlyOnce()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());

        await ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "Rent", 500_000m);

        var samePosition = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "Recargo", 30_000m));
        Assert.Equal(UniqueViolation, samePosition.SqlState);
        Assert.Equal("ux_recibo_lines_position", samePosition.ConstraintName);

        var sameKind = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 2, "Rent", 1m));
        Assert.Equal(UniqueViolation, sameKind.SqlState);
        Assert.Equal("ux_recibo_lines_kind", sameKind.ConstraintName);
    }

    [SkippableFact]
    public async Task AReciboLine_ObeysTheSignRulesOfItsKind()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());

        var negativeTasa = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "TasaMunicipal", -1m));
        Assert.Equal(CheckViolation, negativeTasa.SqlState);
        Assert.Equal("ck_recibo_lines_tasa_not_negative", negativeTasa.ConstraintName);

        var positiveHonorarios = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertLineAsync(connection, null, paid.PropietarioId, 1, "HonorariosAdministracion", 1m));
        Assert.Equal(CheckViolation, positiveHonorarios.SqlState);
        Assert.Equal("ck_recibo_lines_honorarios_not_positive", positiveHonorarios.ConstraintName);

        var unknownKind = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "Propina", 10m));
        Assert.Equal(CheckViolation, unknownKind.SqlState);
        Assert.Equal("ck_recibo_lines_kind", unknownKind.ConstraintName);

        // What the rules allow stays allowed: a negative seguro and a zero or negative honorarios.
        await ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "Seguro", -5_000m);
        await ReciboTestSupport.InsertLineAsync(connection, null, paid.PropietarioId, 1, "HonorariosAdministracion", -47_860m);
    }

    /// <summary>
    /// The kind list in the CHECK is built from <see cref="ReciboLineKind"/>, so every member the
    /// domain can print is storable.
    /// </summary>
    [SkippableFact]
    public async Task EveryReciboLineKind_TheDomainKnows_IsStorable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());

        var position = 1;
        foreach (var kind in Enum.GetNames<ReciboLineKind>())
        {
            var amount = kind == nameof(ReciboLineKind.HonorariosAdministracion) ? -1m : 1m;
            await ReciboTestSupport.InsertLineAsync(connection, null, paid.PropietarioId, position++, kind, amount);
        }

        var stored = await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM recibo_lines WHERE recibo_id = @id", ("id", paid.PropietarioId));
        Assert.Equal(Enum.GetNames<ReciboLineKind>().Length, stored);
    }

    // ---- Test 48: recibos and lines are append-only -----------------------------------------

    /// <summary>Spec test 48: a recibo and its lines cannot be updated or deleted, by raw SQL either.</summary>
    [SkippableFact]
    public async Task AReciboAndItsLines_CannotBeUpdatedOrDeleted_ByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());
        var lineId = Guid.NewGuid();
        await ReciboTestSupport.InsertLineAsync(connection, null, paid.LocatarioId, 1, "Rent", 500_000m, lineId);

        var attempts = new (string Sql, Guid Id, string Constraint)[]
        {
            ("UPDATE recibos SET total = 1 WHERE id = @id", paid.LocatarioId, "recibos_append_only"),
            ("UPDATE recibos SET number = number + 1000 WHERE id = @id", paid.LocatarioId, "recibos_append_only"),
            ("UPDATE recibos SET legend = 'otra cosa' WHERE id = @id", paid.LocatarioId, "recibos_append_only"),
            ("DELETE FROM recibos WHERE id = @id", paid.LocatarioId, "recibos_append_only"),
            ("UPDATE recibo_lines SET amount = 1 WHERE id = @id", lineId, "recibo_lines_append_only"),
            ("DELETE FROM recibo_lines WHERE id = @id", lineId, "recibo_lines_append_only"),
        };

        foreach (var (sql, id, constraint) in attempts)
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.ExecAsync(connection, sql, ("id", id)));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal(constraint, refused.ConstraintName);
        }

        var total = await ReciboTestSupport.ScalarAsync<decimal>(
            connection, "SELECT total FROM recibos WHERE id = @id", ("id", paid.LocatarioId));
        Assert.Equal(830_000m, total);
    }

    // ---- Test 51: annulments ----------------------------------------------------------------

    /// <summary>
    /// Spec test 51: an annulled recibo is read as annulled with its number, the reason, the user and
    /// the time, the last two stamped by the database whatever the INSERT supplied.
    /// </summary>
    [SkippableFact]
    public async Task AnAnnulledRecibo_IsReadAsAnnulled_WithWhoAndWhenStampedByTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var number = ReciboTestSupport.RandomNumber();
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, number);

        await ReciboTestSupport.ExecAsync(
            connection,
            """
            INSERT INTO recibo_annulments (id, recibo_id, reason, annulled_by, annulled_at)
            VALUES (@id, @recibo, 'Importe mal cargado', 'somebody_else', TIMESTAMPTZ '2000-01-01 00:00:00+00')
            """,
            ("id", Guid.NewGuid()), ("recibo", paid.LocatarioId));

        await using var command = new NpgsqlCommand(
            """
            SELECT r.number, a.reason, a.annulled_by = current_user,
                   a.annulled_at > now() - interval '1 minute' AND a.annulled_at <= now() + interval '1 minute'
            FROM recibos r JOIN recibo_annulments a ON a.recibo_id = r.id
            WHERE r.id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", paid.LocatarioId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(number, reader.GetInt32(0));
        Assert.Equal("Importe mal cargado", reader.GetString(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
    }

    [SkippableFact]
    public async Task AnAnnulment_NeedsAReason_AndHappensOnlyOncePerRecibo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());

        foreach (var blank in new[] { "", "   ", "\t\n" })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, blank));

            Assert.Equal(CheckViolation, refused.SqlState);
            Assert.Equal("ck_recibo_annulments_reason_not_blank", refused.ConstraintName);
        }

        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, "Cobrado por error");

        var twice = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, "Otra vez"));

        Assert.Equal(UniqueViolation, twice.SqlState);
        Assert.Equal("ux_recibo_annulments_recibo", twice.ConstraintName);

        // Its sibling copy is a different recibo and may be annulled, which is how a pair goes together.
        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.PropietarioId, "Cobrado por error");
    }

    [SkippableFact]
    public async Task AnAnnulment_CannotBeUpdatedOrDeleted_ByRawSql()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(context, connection, July, ReciboTestSupport.RandomNumber());
        var annulmentId = Guid.NewGuid();
        await ReciboTestSupport.InsertAnnulmentAsync(connection, null, paid.LocatarioId, "Cobrado por error", annulmentId);

        foreach (var statement in new[]
                 {
                     "UPDATE recibo_annulments SET reason = 'otra' WHERE id = @id",
                     "UPDATE recibo_annulments SET annulled_by = 'somebody_else' WHERE id = @id",
                     "DELETE FROM recibo_annulments WHERE id = @id",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.ExecAsync(connection, statement, ("id", annulmentId)));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("recibo_annulments_append_only", refused.ConstraintName);
        }
    }

    // ---- Tests 33, 34, 35, 66: the counter and the cutover number ---------------------------

    [SkippableFact]
    public async Task TheCounterRow_ExistsFromTheMigration_WithNothingSetAndNoSecondRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        Assert.Equal(1L, await ReciboTestSupport.ScalarAsync<long>(connection, "SELECT count(*) FROM recibo_numbering"));
        Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(connection));

        var secondRow = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(connection, "INSERT INTO recibo_numbering (id) VALUES (2)"));
        Assert.Equal(CheckViolation, secondRow.SqlState);
    }

    /// <summary>
    /// Spec test 33, first half: with starting number 31001 and no recibo, the raw issuance returns
    /// 31001 for the locatario and the pair's next number, 31002, for the propietario.
    /// </summary>
    [SkippableFact]
    public async Task WithAStartingNumberAndNoRecibo_TheRawIssuanceReturnsTheStartingNumber()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        Assert.Equal((31001, 31001), await ReciboTestSupport.ReadCounterAsync(connection));

        var first = await ReciboTestSupport.IssueAsync(connection, null);
        Assert.Equal(31001, first);

        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(database.Context, connection, July, first!.Value);
        Assert.Equal(31001, paid.LocatarioNumber);
        Assert.Equal(31002, paid.LocatarioNumber + 1);

        // The next payment starts after the pair.
        Assert.Equal(31003, await ReciboTestSupport.IssueAsync(connection, null));
    }

    /// <summary>
    /// Spec test 33, second half: a starting number corrected to 31100 before the first recibo is the one
    /// the issuance returns.
    /// </summary>
    [SkippableFact]
    public async Task AStartingNumberCorrectedBeforeUse_IsTheOneReturned()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31100 WHERE id = 1");

        Assert.Equal((31100, 31100), await ReciboTestSupport.ReadCounterAsync(connection));
        Assert.Equal(31100, await ReciboTestSupport.IssueAsync(connection, null));
    }

    /// <summary>Spec test 34: once a recibo exists, the cutover number is refused.</summary>
    [SkippableFact]
    public async Task TheCutoverNumber_IsRefused_AfterTheFirstRecibo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        var first = await ReciboTestSupport.IssueAsync(connection, null);
        await ReciboTestSupport.SeedPaidPeriodAsync(database.Context, connection, July, first!.Value);
        var counterBefore = await ReciboTestSupport.ReadCounterAsync(connection);

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 40000 WHERE id = 1"));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("recibo_numbering_cutover_locked", refused.ConstraintName);
        Assert.Equal(counterBefore, await ReciboTestSupport.ReadCounterAsync(connection));

        // Naming the column without changing it is not a change.
        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        Assert.Equal(counterBefore, await ReciboTestSupport.ReadCounterAsync(connection));
    }

    /// <summary>
    /// Spec test 35: with no starting number the raw issuance returns no row and leaves the counter
    /// unchanged; a zero or negative starting number is refused.
    /// </summary>
    [SkippableFact]
    public async Task WithNoStartingNumber_NothingIsIssued_AndANonPositiveOneIsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        Assert.Null(await ReciboTestSupport.IssueAsync(connection, null));
        Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(connection));

        foreach (var starting in new[] { 0, -1 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.ExecAsync(
                    connection, "UPDATE recibo_numbering SET starting_number = @n WHERE id = 1", ("n", starting)));

            Assert.Equal(CheckViolation, refused.SqlState);
            Assert.Equal("ck_recibo_numbering_starting_positive", refused.ConstraintName);
        }

        Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    /// <summary>No issuance before the cutover: the counter cannot be moved while there is no starting number.</summary>
    [SkippableFact]
    public async Task TheCounter_CannotBeMoved_BeforeTheCutoverNumberIsSet()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET next_number = 5 WHERE id = 1"));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Equal("recibo_numbering_not_started", refused.ConstraintName);
        Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    /// <summary>
    /// The counter only moves forward: once started, an UPDATE that sets <c>next_number</c> to NULL or
    /// to a value below the current one is refused, and nothing moves. Advancing still works. (Setting
    /// the value it already holds changes nothing and is not an UPDATE the trigger can tell apart from
    /// one that leaves the column alone, so it is let through; it cannot produce a duplicate.)
    /// </summary>
    [SkippableFact]
    public async Task TheCounter_OnlyMovesForward()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        Assert.Equal(31001, await ReciboTestSupport.IssueAsync(connection, null));
        Assert.Equal((31001, 31003), await ReciboTestSupport.ReadCounterAsync(connection));

        foreach (var statement in new[]
                 {
                     "UPDATE recibo_numbering SET next_number = 31001 WHERE id = 1",
                     "UPDATE recibo_numbering SET next_number = 31002 WHERE id = 1",
                     "UPDATE recibo_numbering SET next_number = NULL WHERE id = 1",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(connection, statement));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("recibo_numbering_next_number_forward", refused.ConstraintName);
            Assert.Equal((31001, 31003), await ReciboTestSupport.ReadCounterAsync(connection));
        }

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET next_number = 31004 WHERE id = 1");
        Assert.Equal((31001, 31004), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    /// <summary>The forward-only rule binds the application roles too, and advancing still works for both.</summary>
    [SkippableTheory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public async Task TheCounter_OnlyMovesForward_ForBothRoles(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"f{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        await ReciboTestSupport.ExecAsync(superuser, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        Assert.Equal(31001, await ReciboTestSupport.IssueAsync(asRole, null));

        foreach (var statement in new[]
                 {
                     "UPDATE recibo_numbering SET next_number = 31001 WHERE id = 1",
                     "UPDATE recibo_numbering SET next_number = NULL WHERE id = 1",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(asRole, statement));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal("recibo_numbering_next_number_forward", refused.ConstraintName);
        }

        Assert.Equal(31003, await ReciboTestSupport.IssueAsync(asRole, null));
        Assert.Equal((31001, 31005), await ReciboTestSupport.ReadCounterAsync(asRole));
    }

    /// <summary>
    /// Spec test 66: a counter set back by a direct UPDATE cannot produce a duplicate. The next raw
    /// issuance plus its recibo INSERT is refused by the unique constraint, loudly, and rolls back with
    /// the whole payment.
    /// </summary>
    [SkippableFact]
    public async Task ACounterSetBack_CannotProduceADuplicateNumber()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1");
        var first = await ReciboTestSupport.IssueAsync(connection, null);
        var paid = await ReciboTestSupport.SeedPaidPeriodAsync(database.Context, connection, July, first!.Value);

        // The tampering: back to the number already used. The forward-only trigger now refuses this from
        // the application roles (see TheCounter_OnlyMovesForward), so the set-back is made as the
        // superuser with triggers off (session_replication_role = replica), the way a restore or a
        // DBA's mistake would bypass them. The unique index is the last line, and what is proved here.
        await using (var tamper = await connection.BeginTransactionAsync())
        {
            await ReciboTestSupport.ExecAsync(connection, tamper, "SET LOCAL session_replication_role = replica");
            await ReciboTestSupport.ExecAsync(connection, tamper, "UPDATE recibo_numbering SET next_number = 31001 WHERE id = 1");
            await tamper.CommitAsync();
        }

        var other = await ReciboTestSupport.SeedCollectionOnlyAsync(database.Context, connection, new DateOnly(2026, 8, 1));

        await using var transaction = await connection.BeginTransactionAsync();
        var issued = await ReciboTestSupport.IssueAsync(connection, transaction);
        Assert.Equal(31001, issued);

        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            ReciboTestSupport.InsertReciboAsync(
                connection, transaction, Guid.NewGuid(), other.CollectionId, issued!.Value, "Locatario",
                new DateOnly(2026, 8, 1), other.ReceivedOn));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_recibos_number", refused.ConstraintName);

        await transaction.RollbackAsync();

        // Rolled back with it: the counter is where the tampering left it, and the original recibo stands.
        Assert.Equal((31001, 31001), await ReciboTestSupport.ReadCounterAsync(connection));
        Assert.Equal(
            1L,
            await ReciboTestSupport.ScalarAsync<long>(
                connection, "SELECT count(*) FROM recibos WHERE number = 31001"));
        Assert.Equal(31001, paid.LocatarioNumber);
    }
}

/// <summary>
/// A freshly migrated database of its own, dropped on dispose. The counter and "no recibo yet" are
/// properties of the whole database, so a test about them cannot share the fixture's.
/// </summary>
internal sealed class IsolatedDatabase : IAsyncDisposable
{
    private readonly InmobiliariaDbContext _admin;
    private readonly string _name;

    private IsolatedDatabase(InmobiliariaDbContext admin, string name, InmobiliariaDbContext context)
    {
        _admin = admin;
        _name = name;
        Context = context;
    }

    /// <summary>A superuser context on the new database.</summary>
    public InmobiliariaDbContext Context { get; }

    public static async Task<IsolatedDatabase> CreateAsync(PostgresFixture fixture)
    {
        var admin = fixture.CreateDbContext();
        var name = $"recibos_{Guid.NewGuid():N}";
        await ReciboTestSupport.ExecuteConstantSqlAsync(admin, $"CREATE DATABASE {name}");

        var connection = new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()!)
        {
            Database = name,
            Pooling = false,
        };

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options;

        var context = new InmobiliariaDbContext(options);
        await context.Database.MigrateAsync();

        return new IsolatedDatabase(admin, name, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await ReciboTestSupport.ExecuteConstantSqlAsync(_admin, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
        await _admin.DisposeAsync();
    }
}

/// <summary>What <see cref="ReciboTestSupport.SeedCollectionOnlyAsync"/> wrote: a payment with no recibo yet.</summary>
internal sealed record CollectionSeed(Guid AccountId, Guid CollectionId, DateOnly ReceivedOn);

/// <summary>What <see cref="ReciboTestSupport.SeedPaidPeriodAsync"/> wrote: a payment and its two recibos.</summary>
internal sealed record PaidPeriod(
    Guid AccountId,
    Guid CollectionId,
    Guid LocatarioId,
    Guid PropietarioId,
    int LocatarioNumber,
    DateOnly ReceivedOn);

/// <summary>
/// Raw-SQL seeding and reading for the migration B tests, shared by the three Recibo test classes.
/// Everything here goes through the real triggers: a seeded collection is stamped like a real one.
/// </summary>
internal static class ReciboTestSupport
{
    public static Task<int> ExecuteConstantSqlAsync(InmobiliariaDbContext context, string sql)
    {
        // Statements assembled from test-controlled constants (a generated database name), never user
        // input, so the injection warning EF raises for string-built SQL does not apply.
#pragma warning disable EF1002
        return context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    public static async Task<NpgsqlConnection> OpenAsync(InmobiliariaDbContext context)
    {
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public static int RandomNumber() => Random.Shared.Next(1_000_000, int.MaxValue - 1_000);

    public static Task<int> ExecAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        ExecAsync(connection, null, sql, parameters);

    public static async Task<int> ExecAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<PostgresException> RefusedAsync(Func<Task> statement) =>
        await Assert.ThrowsAsync<PostgresException>(statement);

    /// <summary>The agency-local date, asked of the database that will apply the trigger's own clock.</summary>
    public static async Task<DateOnly> LocalTodayAsync(NpgsqlConnection connection) =>
        await ScalarAsync<DateOnly>(connection, "SELECT (now() AT TIME ZONE 'America/Argentina/Buenos_Aires')::date");

    public static async Task<(int? Starting, int? Next)> ReadCounterAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT starting_number, next_number FROM recibo_numbering WHERE id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    /// <summary>
    /// The issuance statement of design section 3, verbatim: one UPDATE ... RETURNING, no row when the
    /// cutover number was never set.
    /// </summary>
    public static async Task<int?> IssueAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE recibo_numbering
               SET next_number = next_number + 2
             WHERE id = 1 AND next_number IS NOT NULL
            RETURNING next_number - 2 AS first_number
            """,
            connection,
            transaction);

        return await command.ExecuteScalarAsync() is int first ? first : null;
    }

    public static async Task<Guid> SeedAccountAsync(InmobiliariaDbContext context)
    {
        var (contract, _) = SchemaConstraintTests.SeedContractWithClause(context);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return account.Id;
    }

    /// <summary>The two ledger rows one payment writes: the payment (a credit) and its charged recargo.</summary>
    public static async Task<(Guid Payment, Guid Recargo)> InsertSettlementMovementsAsync(
        NpgsqlConnection connection, Guid accountId, DateOnly period)
    {
        var payment = Guid.NewGuid();
        var recargo = Guid.NewGuid();

        const string Sql =
            """
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES (@id, @account, @kind, @amount, @period, @period)
            """;

        await ExecAsync(connection, Sql, ("id", recargo), ("account", accountId), ("kind", "RecargoCharged"), ("amount", 30_000m), ("period", period));
        await ExecAsync(connection, Sql, ("id", payment), ("account", accountId), ("kind", "Payment"), ("amount", -830_000m), ("period", period));

        return (payment, recargo);
    }

    public static Task<int> InsertCollectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        Guid accountId,
        DateOnly period,
        int attempt,
        DateOnly receivedOn,
        Guid paymentId,
        Guid? recargoId) =>
        ExecAsync(
            connection,
            transaction,
            """
            INSERT INTO collections (id, account_id, period, attempt, received_on, payment_movement_id, recargo_movement_id)
            VALUES (@id, @account, @period, @attempt, @received, @payment, @recargo)
            """,
            ("id", id), ("account", accountId), ("period", period), ("attempt", (short)attempt),
            ("received", receivedOn), ("payment", paymentId), ("recargo", recargoId));

    public static Task<int> InsertReciboAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        Guid collectionId,
        int number,
        string copy,
        DateOnly period,
        DateOnly receivedOn) =>
        ExecAsync(
            connection,
            transaction,
            """
            INSERT INTO recibos
                (id, collection_id, number, copy, contract_id, period, received_on,
                 tenant_name, lessor_names, units_description, legend, total)
            VALUES (@id, @collection, @number, @copy, @contract, @period, @received,
                    'Maria Perez', 'Juan Gomez', 'Local 1', 'Documento no valido como factura', 830000)
            """,
            ("id", id), ("collection", collectionId), ("number", number), ("copy", copy),
            ("contract", Guid.NewGuid()), ("period", period), ("received", receivedOn));

    public static Task<int> InsertLineAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid reciboId, int position, string kind, decimal amount, Guid? id = null) =>
        ExecAsync(
            connection,
            transaction,
            "INSERT INTO recibo_lines (id, recibo_id, position, kind, amount) VALUES (@id, @recibo, @position, @kind, @amount)",
            ("id", id ?? Guid.NewGuid()), ("recibo", reciboId), ("position", (short)position), ("kind", kind), ("amount", amount));

    public static Task<int> InsertAnnulmentAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid reciboId, string reason, Guid? id = null) =>
        ExecAsync(
            connection,
            transaction,
            "INSERT INTO recibo_annulments (id, recibo_id, reason) VALUES (@id, @recibo, @reason)",
            ("id", id ?? Guid.NewGuid()), ("recibo", reciboId), ("reason", reason));

    /// <summary>A new account with one collection (attempt 1, received today) and no recibo.</summary>
    public static async Task<CollectionSeed> SeedCollectionOnlyAsync(
        InmobiliariaDbContext context, NpgsqlConnection connection, DateOnly period)
    {
        var today = await LocalTodayAsync(connection);
        var accountId = await SeedAccountAsync(context);
        var (paymentId, recargoId) = await InsertSettlementMovementsAsync(connection, accountId, period);
        var collectionId = Guid.NewGuid();

        await InsertCollectionAsync(connection, null, collectionId, accountId, period, 1, today, paymentId, recargoId);

        return new CollectionSeed(accountId, collectionId, today);
    }

    /// <summary>
    /// A new account with a settled period: the collection and both recibos, the locatario taking
    /// <paramref name="firstNumber"/> and the propietario the next one. A random pair when none is given.
    /// </summary>
    public static async Task<PaidPeriod> SeedPaidPeriodAsync(
        InmobiliariaDbContext context, NpgsqlConnection connection, DateOnly period, int? firstNumber = null)
    {
        var number = firstNumber ?? RandomNumber();
        var seed = await SeedCollectionOnlyAsync(context, connection, period);
        var locatario = Guid.NewGuid();
        var propietario = Guid.NewGuid();

        await InsertReciboAsync(connection, null, locatario, seed.CollectionId, number, "Locatario", period, seed.ReceivedOn);
        await InsertReciboAsync(connection, null, propietario, seed.CollectionId, number + 1, "Propietario", period, seed.ReceivedOn);

        return new PaidPeriod(seed.AccountId, seed.CollectionId, locatario, propietario, number, seed.ReceivedOn);
    }
}
