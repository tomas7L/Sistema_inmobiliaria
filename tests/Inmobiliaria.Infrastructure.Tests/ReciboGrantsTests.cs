using Inmobiliaria.Domain.Access;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec Test 65: what each application role may do with the collection and recibo tables, proven by
/// connecting AS that role and doing it. Both roles read and insert; neither may update or delete the
/// append-only tables; both advance the recibo counter; only Admin may set the cutover number.
///
/// A real login role, not the container's superuser: a superuser passes every privilege check, so a
/// test run as one proves nothing about a grant. Each case runs on its own freshly migrated database
/// because the cutover number can be set only while no recibo exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReciboGrantsTests
{
    private const string InsufficientPrivilege = "42501";

    private static readonly DateOnly July = new(2026, 7, 1);

    private static readonly string[] AppendOnlyTables =
        ["collections", "recibos", "recibo_lines", "recibo_annulments"];

    private readonly PostgresFixture _fixture;

    public ReciboGrantsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<UserRole> Roles => new() { UserRole.Admin, UserRole.Empleado };

    [SkippableTheory]
    [MemberData(nameof(Roles))]
    public async Task BothRoles_ReadAndInsert_TheCollectionAndReciboTables(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"g{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        var today = await ReciboTestSupport.LocalTodayAsync(superuser);
        var accountId = await ReciboTestSupport.SeedAccountAsync(database.Context);
        var (paymentId, recargoId) = await ReciboTestSupport.InsertSettlementMovementsAsync(superuser, accountId, July);

        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        var collectionId = Guid.NewGuid();
        var locatarioId = Guid.NewGuid();
        var propietarioId = Guid.NewGuid();

        await ReciboTestSupport.InsertCollectionAsync(asRole, null, collectionId, accountId, July, 1, today, paymentId, recargoId);
        await ReciboTestSupport.InsertReciboAsync(asRole, null, locatarioId, collectionId, 70001, "Locatario", July, today);
        await ReciboTestSupport.InsertReciboAsync(asRole, null, propietarioId, collectionId, 70002, "Propietario", July, today);
        await ReciboTestSupport.InsertLineAsync(asRole, null, locatarioId, 1, "Rent", 500_000m);
        await ReciboTestSupport.InsertAnnulmentAsync(asRole, null, locatarioId, "Cobrado por error");

        foreach (var table in AppendOnlyTables)
        {
            var rows = await ReciboTestSupport.ScalarAsync<long>(asRole, $"SELECT count(*) FROM {table}");
            Assert.True(rows > 0, $"{table} should be readable and hold the row just inserted.");
        }

        // The stamps are the role's, not the table owner's: no trigger function runs as its owner.
        var stampedByTheRole = await ReciboTestSupport.ScalarAsync<bool>(
            asRole,
            """
            SELECT c.recorded_by = current_user AND a.annulled_by = current_user
            FROM collections c, recibo_annulments a
            WHERE c.id = @collection
            """,
            ("collection", collectionId));
        Assert.True(stampedByTheRole);
    }

    [SkippableTheory]
    [MemberData(nameof(Roles))]
    public async Task NeitherRole_CanUpdateOrDelete_TheAppendOnlyTables(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"g{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        // WHERE false: the privilege is checked before any row is looked at, so no seeded data is
        // needed to prove the role was never granted the statement.
        foreach (var table in AppendOnlyTables)
        {
            foreach (var statement in new[] { $"UPDATE {table} SET id = id WHERE false", $"DELETE FROM {table} WHERE false" })
            {
                var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(asRole, statement));
                Assert.Equal(InsufficientPrivilege, refused.SqlState);
            }
        }

        // The counter row can be neither deleted nor given a new row, nor changed beyond the two columns.
        foreach (var statement in new[]
                 {
                     "DELETE FROM recibo_numbering WHERE false",
                     "INSERT INTO recibo_numbering (id) VALUES (2)",
                     "UPDATE recibo_numbering SET id = id WHERE false",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(asRole, statement));
            Assert.Equal(InsufficientPrivilege, refused.SqlState);
        }
    }

    /// <summary>
    /// Both roles read the counter and advance <c>next_number</c>; the cutover number is Admin's alone,
    /// by a column-level grant, and the Empleado role is refused.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Roles))]
    public async Task OnlyAdmin_SetsTheCutoverNumber_ButBothRolesAdvanceTheCounter(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"g{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(asRole));

        const string SetStarting = "UPDATE recibo_numbering SET starting_number = 31001 WHERE id = 1";

        if (role == UserRole.Admin)
        {
            await ReciboTestSupport.ExecAsync(asRole, SetStarting);
            Assert.Equal((31001, 31001), await ReciboTestSupport.ReadCounterAsync(asRole));
        }
        else
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(asRole, SetStarting));
            Assert.Equal(InsufficientPrivilege, refused.SqlState);
            Assert.Equal((null, null), await ReciboTestSupport.ReadCounterAsync(asRole));

            // An Admin (here the superuser, standing in for Volkode's go-live act) sets it.
            await ReciboTestSupport.ExecAsync(superuser, SetStarting);
        }

        // Issuance is the same statement for both roles.
        Assert.Equal(31001, await ReciboTestSupport.IssueAsync(asRole, null));
        Assert.Equal(31003, await ReciboTestSupport.IssueAsync(asRole, null));
    }

    /// <summary>
    /// The privileges the roles must NOT hold, read from the catalog: nothing beyond SELECT and INSERT
    /// on the append-only tables, nothing but SELECT on the counter table at table level.
    /// </summary>
    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task TheGroupRoles_HoldExactlyTheDesignedTablePrivileges(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);

        foreach (var table in AppendOnlyTables)
        {
            var held = await ReciboTestSupport.ScalarAsync<string>(
                connection,
                """
                SELECT string_agg(p.privilege, ',' ORDER BY p.privilege)
                FROM unnest(ARRAY['SELECT','INSERT','UPDATE','DELETE','TRUNCATE','REFERENCES','TRIGGER']) AS p(privilege)
                WHERE has_table_privilege(@role, @table, p.privilege)
                """,
                ("role", groupRole), ("table", table));

            Assert.Equal("INSERT,SELECT", held);
        }

        var counterHeld = await ReciboTestSupport.ScalarAsync<string>(
            connection,
            """
            SELECT string_agg(p.privilege, ',' ORDER BY p.privilege)
            FROM unnest(ARRAY['SELECT','INSERT','UPDATE','DELETE','TRUNCATE','REFERENCES','TRIGGER']) AS p(privilege)
            WHERE has_table_privilege(@role, 'recibo_numbering', p.privilege)
            """,
            ("role", groupRole));

        Assert.Equal("SELECT", counterHeld);
    }
}
