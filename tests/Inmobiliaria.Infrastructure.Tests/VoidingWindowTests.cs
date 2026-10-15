using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec Test 72: the voiding window, enforced by the database. An Empleado may void a pair up to
/// <see cref="CollectionRules.VoidingFreeWindowDays"/> days after it was recorded, an Admin up to
/// <see cref="CollectionRules.VoidingAdminWindowDays"/>, and nobody after that; each refusal writes
/// nothing. The figures come from <see cref="CollectionRules"/>, so a constant changed without its
/// trigger line fails here (task 5.4, "constants agree").
///
/// The window is measured from <c>collections.recorded_at</c>, which the collection trigger stamps with
/// now(), so a test cannot simply insert a pair "recorded 8 days ago". The trigger is never loosened for
/// it and nothing in the schema accepts a test hook: the fixture inserts the pair as the container's
/// superuser inside a transaction that runs <c>SET LOCAL session_replication_role = replica</c>, which
/// skips ordinary triggers for that transaction only, and writes <c>recorded_at</c> explicitly.
///
/// The voiding under test runs on a separate connection logged in as a real login role that is a member
/// of the Empleado or Admin group role. Never as the superuser: a superuser passes
/// <c>pg_has_role</c> for every role, so a test run as one would prove nothing about the Empleado limit
/// (<see cref="AsTheSuperuser_TheEmpleadoDayEightCase_Succeeds_WhichIsWhyTheRolesAreRealLogins"/> shows it).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VoidingWindowTests
{
    private const string RaiseException = "P0001";
    private const string AdminOnly = "recibo_annulments_admin_only";
    private const string WindowClosed = "recibo_annulments_window_closed";

    private static readonly DateOnly July = new(2026, 7, 1);

    private readonly PostgresFixture _fixture;

    public VoidingWindowTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Role, days since the payment was recorded, and the refusal expected (null when allowed).</summary>
    public static TheoryData<UserRole, int, string?> Cases => new()
    {
        { UserRole.Empleado, 0, null },
        { UserRole.Empleado, CollectionRules.VoidingFreeWindowDays, null },
        { UserRole.Empleado, CollectionRules.VoidingFreeWindowDays + 1, AdminOnly },
        { UserRole.Empleado, CollectionRules.VoidingAdminWindowDays, AdminOnly },
        { UserRole.Empleado, CollectionRules.VoidingAdminWindowDays + 1, WindowClosed },
        { UserRole.Admin, CollectionRules.VoidingFreeWindowDays + 1, null },
        { UserRole.Admin, CollectionRules.VoidingAdminWindowDays, null },
        { UserRole.Admin, CollectionRules.VoidingAdminWindowDays + 1, WindowClosed },
    };

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task AVoiding_IsAllowedOrRefused_ByTheRoleAndTheAgeOfThePayment(UserRole role, int daysOld, string? refusal)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"v{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);
        var pair = await SeedPairRecordedDaysAgoAsync(database.Context, daysOld);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        if (refusal is null)
        {
            await VoidAsync(asRole, pair);

            Assert.Equal(2L, await CountAnnulmentsAsync(superuser, pair));
            Assert.Equal(2L, await CountCorrectionsAsync(superuser, pair));

            var byTheLogin = await ReciboTestSupport.ScalarAsync<long>(
                superuser,
                """
                SELECT count(*) FROM recibo_annulments a
                JOIN recibos r ON r.id = a.recibo_id
                WHERE r.collection_id = @collection AND a.annulled_by = @login
                """,
                ("collection", pair.CollectionId), ("login", $"{username}.{AccessTestSupport.TestProjectRef}"));
            Assert.Equal(2L, byTheLogin);
        }
        else
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => VoidAsync(asRole, pair));

            Assert.Equal(RaiseException, refused.SqlState);
            Assert.Equal(refusal, refused.ConstraintName);

            // The refusal wrote nothing: the corrections went in first inside the same transaction and
            // rolled back with it.
            Assert.Equal(0L, await CountAnnulmentsAsync(superuser, pair));
            Assert.Equal(0L, await CountCorrectionsAsync(superuser, pair));
        }
    }

    /// <summary>
    /// Guard proof of the fixture (design section 9): the Empleado day-8 case, run as the superuser,
    /// succeeds, because a superuser passes pg_has_role for the Admin group. It is why the cases above
    /// connect as real login roles.
    /// </summary>
    [SkippableFact]
    public async Task AsTheSuperuser_TheEmpleadoDayEightCase_Succeeds_WhichIsWhyTheRolesAreRealLogins()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var pair = await SeedPairRecordedDaysAgoAsync(database.Context, CollectionRules.VoidingFreeWindowDays + 1);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        await VoidAsync(superuser, pair);

        Assert.Equal(2L, await CountAnnulmentsAsync(superuser, pair));
    }

    /// <summary>
    /// The fixture itself: the collection really is the age asked for, its stamp was not overwritten, and
    /// the same insert made without the replica role is stamped with the present, which is the reason the
    /// replica transaction exists.
    /// </summary>
    [SkippableFact]
    public async Task TheFixture_WritesAnOldRecordedAt_ThatTheTriggerWouldHaveOverwritten()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var pair = await SeedPairRecordedDaysAgoAsync(database.Context, 10);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        var today = await ReciboTestSupport.LocalTodayAsync(superuser);

        var stored = await ReciboTestSupport.ScalarAsync<DateOnly>(
            superuser,
            "SELECT (recorded_at AT TIME ZONE 'America/Argentina/Buenos_Aires')::date FROM collections WHERE id = @id",
            ("id", pair.CollectionId));
        Assert.Equal(today.AddDays(-10), stored);

        var viaTheTrigger = await ReciboTestSupport.SeedCollectionOnlyAsync(
            database.Context, superuser, new DateOnly(2026, 8, 1));
        var stampedNow = await ReciboTestSupport.ScalarAsync<DateOnly>(
            superuser,
            "SELECT (recorded_at AT TIME ZONE 'America/Argentina/Buenos_Aires')::date FROM collections WHERE id = @id",
            ("id", viaTheTrigger.CollectionId));
        Assert.Equal(today, stampedNow);
    }

    private sealed record Pair(Guid AccountId, Guid CollectionId, Guid PaymentId, Guid RecargoId, Guid LocatarioId, Guid PropietarioId);

    /// <summary>
    /// Inserts a collection, its two movements and its recibo pair as the superuser, in a transaction that
    /// skips ordinary triggers, with <c>recorded_at</c> at midday of the agency-local day
    /// <paramref name="daysAgo"/> days back.
    /// </summary>
    private static async Task<Pair> SeedPairRecordedDaysAgoAsync(Inmobiliaria.Infrastructure.Persistence.InmobiliariaDbContext context, int daysAgo)
    {
        var accountId = await ReciboTestSupport.SeedAccountAsync(context);
        await using var connection = await ReciboTestSupport.OpenAsync(context);
        var today = await ReciboTestSupport.LocalTodayAsync(connection);
        var recordedOn = today.AddDays(-daysAgo);

        var pair = new Pair(accountId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var number = ReciboTestSupport.RandomNumber();

        await using var transaction = await connection.BeginTransactionAsync();
        await ReciboTestSupport.ExecAsync(connection, transaction, "SET LOCAL session_replication_role = replica");

        const string MovementSql =
            """
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES (@id, @account, @kind, @amount, @period, @period)
            """;
        await ReciboTestSupport.ExecAsync(
            connection, transaction, MovementSql,
            ("id", pair.RecargoId), ("account", accountId), ("kind", "RecargoCharged"), ("amount", 30_000m), ("period", July));
        await ReciboTestSupport.ExecAsync(
            connection, transaction, MovementSql,
            ("id", pair.PaymentId), ("account", accountId), ("kind", "Payment"), ("amount", -830_000m), ("period", July));

        await ReciboTestSupport.ExecAsync(
            connection,
            transaction,
            """
            INSERT INTO collections
                (id, account_id, period, attempt, received_on, recorded_at, recorded_by,
                 payment_movement_id, recargo_movement_id)
            VALUES (@id, @account, @period, 1, @received,
                    ((@received + TIME '12:00') AT TIME ZONE 'America/Argentina/Buenos_Aires'),
                    'fixture', @payment, @recargo)
            """,
            ("id", pair.CollectionId), ("account", accountId), ("period", July), ("received", recordedOn),
            ("payment", pair.PaymentId), ("recargo", pair.RecargoId));

        await ReciboTestSupport.InsertReciboAsync(connection, transaction, pair.LocatarioId, pair.CollectionId, number, "Locatario", July, recordedOn);
        await ReciboTestSupport.InsertReciboAsync(connection, transaction, pair.PropietarioId, pair.CollectionId, number + 1, "Propietario", July, recordedOn);

        await transaction.CommitAsync();

        return pair;
    }

    /// <summary>
    /// The voiding as raw SQL, in one transaction on <paramref name="connection"/>: the two corrections
    /// (the payment returned in full, the recargo charged returned in full) and the annulment of both
    /// recibos. The corrections go first, so a refusal at the annulment proves the rollback took them.
    /// </summary>
    private static async Task VoidAsync(NpgsqlConnection connection, Pair pair)
    {
        await using var transaction = await connection.BeginTransactionAsync();

        const string CorrectionSql =
            """
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period, corrects_movement_id)
            VALUES (@id, @account, 'Correction', @amount, DATE '2026-07-01', @period, @corrects)
            """;
        await ReciboTestSupport.ExecAsync(
            connection, transaction, CorrectionSql,
            ("id", Guid.NewGuid()), ("account", pair.AccountId), ("amount", 830_000m), ("period", July), ("corrects", pair.PaymentId));
        await ReciboTestSupport.ExecAsync(
            connection, transaction, CorrectionSql,
            ("id", Guid.NewGuid()), ("account", pair.AccountId), ("amount", -30_000m), ("period", July), ("corrects", pair.RecargoId));

        await ReciboTestSupport.InsertAnnulmentAsync(connection, transaction, pair.LocatarioId, "Cobrado por error");
        await ReciboTestSupport.InsertAnnulmentAsync(connection, transaction, pair.PropietarioId, "Cobrado por error");

        await transaction.CommitAsync();
    }

    private static Task<long> CountAnnulmentsAsync(NpgsqlConnection connection, Pair pair) =>
        ReciboTestSupport.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM recibo_annulments a JOIN recibos r ON r.id = a.recibo_id WHERE r.collection_id = @collection",
            ("collection", pair.CollectionId));

    private static Task<long> CountCorrectionsAsync(NpgsqlConnection connection, Pair pair) =>
        ReciboTestSupport.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM account_movements WHERE account_id = @account AND kind = 'Correction'",
            ("account", pair.AccountId));
}
