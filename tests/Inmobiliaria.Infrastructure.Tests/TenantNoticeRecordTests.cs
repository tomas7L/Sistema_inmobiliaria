using Inmobiliaria.Domain.Access;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// notificaciones spec Test 14 and Requirement 7, beside <see cref="ReciboGrantsTests"/>: the sent record <c>tenant_notices</c>
/// proven by connecting AS each role. Both roles read and insert, neither updates or deletes, the trigger refuses even a
/// superuser, the sender is the inserting <c>current_user</c> whatever the insert says, a second row for the same contract, kind
/// and month is refused, and Supabase's API roles hold nothing on it (design D4).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantNoticeRecordTests
{
    private const string InsufficientPrivilege = "42501";
    private const string UniqueViolation = "23505";

    private static readonly string[] EveryTablePrivilege =
        ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER"];

    private readonly PostgresFixture _fixture;

    public TenantNoticeRecordTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<UserRole> Roles => new() { UserRole.Admin, UserRole.Empleado };

    /// <summary>A contract, read back from the account <see cref="ReciboTestSupport.SeedAccountAsync"/> writes.</summary>
    private static async Task<Guid> SeedContractAsync(IsolatedDatabase database, NpgsqlConnection superuser)
    {
        var accountId = await ReciboTestSupport.SeedAccountAsync(database.Context);
        return await ReciboTestSupport.ScalarAsync<Guid>(
            superuser, "SELECT contract_id FROM contract_accounts WHERE id = @a", ("a", accountId));
    }

    /// <summary>A sent row, supplying a forged sender and date the stamp trigger must overwrite.</summary>
    private static Task<int> InsertAsync(NpgsqlConnection connection, Guid id, Guid contractId, DateOnly month) =>
        ReciboTestSupport.ExecAsync(
            connection,
            """
            INSERT INTO tenant_notices
                (id, contract_id, kind, notice_month, recipient, figures_on, total, recargo, recorded_at, recorded_by)
            VALUES
                (@id, @contract, 'amount_owed', @month, 'ana@example.com', @month, 520000, 20000, '2000-01-01', 'somebody_else')
            """,
            ("id", id), ("contract", contractId), ("month", month));

    private static readonly DateOnly December = new(2026, 12, 1);

    [SkippableTheory]
    [MemberData(nameof(Roles))]
    public async Task BothRoles_ReadAndInsert_AndTheSenderIsTheirCurrentUser_WhateverTheInsertSays(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"n{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        var contractId = await SeedContractAsync(database, superuser);

        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        var id = Guid.NewGuid();
        await InsertAsync(asRole, id, contractId, December);

        var stamped = await ReciboTestSupport.ScalarAsync<bool>(
            asRole,
            """
            SELECT recorded_by = current_user AND recorded_at > now() - interval '1 hour'
            FROM tenant_notices WHERE id = @id
            """,
            ("id", id));
        Assert.True(stamped);
    }

    [SkippableTheory]
    [MemberData(nameof(Roles))]
    public async Task NeitherRole_CanUpdateOrDelete_ASentNotice(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var username = $"n{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(database.Context, username, role);

        await using var asRole = AccessTestSupport.BuildRawConnectionAs(database.Context, username);
        await asRole.OpenAsync();

        foreach (var statement in new[] { "UPDATE tenant_notices SET id = id WHERE false", "DELETE FROM tenant_notices WHERE false" })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(asRole, statement));
            Assert.Equal(InsufficientPrivilege, refused.SqlState);
        }
    }

    /// <summary>The trigger holds where a grant cannot: the table's owner, here the superuser, is refused too.</summary>
    [SkippableFact]
    public async Task TheAppendOnlyTrigger_RefusesAnUpdateOrDelete_EvenFromTheOwner()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        var contractId = await SeedContractAsync(database, superuser);
        var id = Guid.NewGuid();
        await InsertAsync(superuser, id, contractId, December);

        foreach (var statement in new[]
                 {
                     "UPDATE tenant_notices SET recipient = 'other@example.com' WHERE id = @id",
                     "DELETE FROM tenant_notices WHERE id = @id",
                 })
        {
            var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(superuser, statement, ("id", id)));
            Assert.Equal("P0001", refused.SqlState);
            Assert.Equal("tenant_notices_append_only", refused.ConstraintName);
        }

        Assert.Equal(1L, await ReciboTestSupport.ScalarAsync<long>(superuser, "SELECT count(*) FROM tenant_notices"));
    }

    /// <summary>spec Requirement 4's database half: one row per contract, kind and month; the next month is a new notice.</summary>
    [SkippableFact]
    public async Task ASecondNoticeForTheSameContractKindAndMonth_IsRefused_ButTheNextMonthIsNot()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        await using var superuser = await ReciboTestSupport.OpenAsync(database.Context);
        var contractId = await SeedContractAsync(database, superuser);
        await InsertAsync(superuser, Guid.NewGuid(), contractId, December);

        var refused = await ReciboTestSupport.RefusedAsync(() => InsertAsync(superuser, Guid.NewGuid(), contractId, December));
        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_tenant_notices_contract_kind_month", refused.ConstraintName);

        await InsertAsync(superuser, Guid.NewGuid(), contractId, December.AddMonths(1));
        Assert.Equal(2L, await ReciboTestSupport.ScalarAsync<long>(superuser, "SELECT count(*) FROM tenant_notices"));
    }

    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task TheGroupRoles_HoldExactlyInsertAndSelect(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        Assert.Equal("INSERT,SELECT", await HeldAsync(groupRole));
    }

    /// <summary>
    /// design D4, Finding F2: Supabase grants every new table in public to its API roles, and this table is revoked from them.
    /// Proven only because <see cref="PostgresFixture"/> creates those roles and grants them tables as Supabase does; without
    /// that, the migration's guarded revoke would be skipped and this would pass for nothing. That the fixture really
    /// grants is checked before any revocation, by <see cref="SupabaseApiRoleRevocationTests"/> Test 1 (revocar-permisos-api).
    /// </summary>
    [SkippableFact]
    public async Task SupabasesApiRoles_HoldNothing_OnTheSentRecord()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        foreach (var role in PostgresFixture.SupabaseApiRoles.Append("public"))
        {
            Assert.Null(await HeldAsync(role));
        }
    }

    /// <summary>The privileges <paramref name="role"/> holds on <paramref name="table"/>, comma-separated in order, or null for none.</summary>
    private async Task<string?> HeldAsync(string role, string table = "tenant_notices")
    {
        await using var context = _fixture.CreateDbContext();
        await using var connection = await ReciboTestSupport.OpenAsync(context);

        await using var command = new NpgsqlCommand(
            """
            SELECT string_agg(p.privilege, ',' ORDER BY p.privilege)
            FROM unnest(@privileges) AS p(privilege)
            WHERE has_table_privilege(@role, @table, p.privilege)
            """,
            connection);
        command.Parameters.AddWithValue("privileges", EveryTablePrivilege);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("table", table);

        return await command.ExecuteScalarAsync() as string;
    }
}
