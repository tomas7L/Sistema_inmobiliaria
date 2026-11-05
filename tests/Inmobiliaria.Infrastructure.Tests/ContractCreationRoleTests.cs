using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>From the independent verification of slice S3: role games, a failing Up, and raw removal.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractCreationRoleTests
{
    private const string Columns = "id, start_date, nominal_end_date, monthly_rent, status, due_day, cutover_date";
    private static readonly FixedClock Clock = new(new DateOnly(2026, 11, 15));

    private readonly PostgresFixture _fixture;

    public ContractCreationRoleTests(PostgresFixture fixture) => _fixture = fixture;

    private static string Row(Guid id, string cutover) => $"('{id}', DATE '2025-01-01', DATE '2026-12-31', 1000, 'Active', 5, {cutover})";

    private static Task<int> InsertWithDateAsync(NpgsqlConnection connection) =>
        ReciboTestSupport.ExecAsync(connection, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}");

    /// <summary>The test is on the role in force: an Admin who sets their group role still passes; RESET returns to the login.</summary>
    [SkippableFact]
    public async Task AnAdminLogin_PassesAsLogin_AndAsItsGroupRole()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var (role, password) = await CreateTestLoginRoleAsync(database.Context, "inmobiliaria_admin");
        await using var admin = BuildConnectionAs(database.Context, role, password);
        await admin.OpenAsync();

        Assert.Equal(1, await InsertWithDateAsync(admin));
        await ReciboTestSupport.ExecAsync(admin, "SET ROLE inmobiliaria_admin");
        Assert.Equal(1, await InsertWithDateAsync(admin));
        await ReciboTestSupport.ExecAsync(admin, "RESET ROLE");
        Assert.Equal(1, await InsertWithDateAsync(admin));
    }

    /// <summary>An Admin login dropping to an Empleado-side role through a shared membership is judged by the role in force, not by the login.</summary>
    [SkippableFact]
    public async Task ALoginInBothRoles_SettingEmpleado_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var (role, password) = await CreateTestLoginRoleAsync(database.Context, "inmobiliaria_admin");
        string grant = $"GRANT inmobiliaria_empleado TO {role}"; await database.Context.Database.ExecuteSqlRawAsync(grant);
        await using var both = BuildConnectionAs(database.Context, role, password);
        await both.OpenAsync();

        Assert.Equal(1, await InsertWithDateAsync(both));
        await ReciboTestSupport.ExecAsync(both, "SET ROLE inmobiliaria_empleado");
        var refused = await ReciboTestSupport.RefusedAsync(() => InsertWithDateAsync(both));
        Assert.Equal("contracts_cutover_insert_admin_only", refused.ConstraintName);
    }

    /// <summary>A role with neither membership cannot write at all.</summary>
    [SkippableFact]
    public async Task ALoginWithNoGroup_IsDeniedByPrivilege()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var name = $"nogroup_{Guid.NewGuid():N}"[..30];
        string create = $"CREATE ROLE {name} LOGIN PASSWORD 'TestRolePassword123!'"; await database.Context.Database.ExecuteSqlRawAsync(create);
        await using var nobody = BuildConnectionAs(database.Context, name, "TestRolePassword123!");
        await nobody.OpenAsync();
        var refused = await ReciboTestSupport.RefusedAsync(() => InsertWithDateAsync(nobody));
        Assert.Equal("42501", refused.SqlState);
    }

    /// <summary>M1 on a database already holding two tenants fails (design §12), and leaves the previous schema whole.</summary>
    [SkippableFact]
    public async Task M1_OnTwoTenants_Fails_AndLeavesThePreviousSchema()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = database.Context.GetService<IMigrator>();
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);
        await migrator.MigrateAsync("20261025005832_FixClearMustChangePasswordIdentity");

        var contract = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await ReciboTestSupport.ExecAsync(connection, $"INSERT INTO contracts ({Columns}) VALUES {Row(contract, "NULL")}");
        foreach (var party in new[] { first, second })
        {
            await ReciboTestSupport.ExecAsync(connection, "INSERT INTO parties (id, first_name, last_name) VALUES (@id, 'A', 'B')", ("id", party));
            await ReciboTestSupport.ExecAsync(
                connection, "INSERT INTO contract_parties (contract_id, party_id, role) VALUES (@c, @p, 'Tenant')", ("c", contract), ("p", party));
        }

        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());

        Assert.Equal(0L, await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'contract_parties' AND column_name = 'removed_at'"));
        Assert.Equal(0L, await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM pg_proc WHERE proname = 'reject_cutover_insert_unless_admin'"));
        Assert.Equal(0L, await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%AddContractPartyRemovalAndGuards'"));

        await ReciboTestSupport.ExecAsync(connection, "DELETE FROM contract_parties WHERE party_id = @p", ("p", second));
        await migrator.MigrateAsync();
        Assert.Equal(1L, await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM pg_proc WHERE proname = 'reject_cutover_insert_unless_admin'"));
    }

    /// <summary>Requirement 8 through a real payment: paying writes only the period after the cutover, on the real clock.</summary>
    [SkippableFact]
    public async Task APayment_OnAPreExistingContract_WritesNothingOnOrBeforeTheCutover()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var today = DeskWorld.Today;
        var current = DeskWorld.FirstOfMonth(today);
        var tenant = new Party(Guid.NewGuid(), "Juan", "Pérez");
        var lessor = new Party(Guid.NewGuid(), "Ana", "Gómez");
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", "1", "Sunchales", "Santa Fe", "2322"));
        await using (var seed = world.NewContext())
        {
            seed.Parties.AddRange(tenant, lessor);
            seed.Units.Add(unit);
            await seed.SaveChangesAsync();
        }

        var created = await new ContractRegistry(world.NewContext(), new FixedClock(today)).CreateAsync(
            new ContractDraft
            {
                TenantId = tenant.Id,
                LessorIds = [lessor.Id],
                Shares = [new UnitShare(unit.Id, 100m)],
                StartDate = current.AddYears(-2),
                NominalEndDate = current.AddYears(1),
                MonthlyRent = 100_000m,
                DueDay = 1,
                HonorariosPercentage = 8m,
                CutoverDate = current.AddDays(-1),
                FeePlanAmount = 5_000m,
                FeePlanInstalments = 6,
            },
            false);
        Assert.True(created.Succeeded, $"{created.Refusal}");
        await using var reading = world.NewContext();
        var account = await reading.ContractAccounts.AsNoTracking().SingleAsync(a => a.ContractId == created.Contract!.Id);

        var paid = await DeskWorld.PayAsync(world.NewDesk(), account.Id);

        Assert.Equal(current, await PeriodOfOnlyAccrualAsync(world));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'ContractualFeeInstalment'"));
        Assert.NotNull(paid);
    }

    private static async Task<DateOnly> PeriodOfOnlyAccrualAsync(DeskWorld world)
    {
        await using var c = await ReciboTestSupport.OpenAsync(world.Database.Context);
        return await ReciboTestSupport.ScalarAsync<DateOnly>(c, "SELECT period FROM account_movements WHERE kind = 'RentAccrual'");
    }

    /// <summary>A clause on a discontinued index is written or refused, never thrown.</summary>
    [SkippableFact]
    public async Task AClauseOnADiscontinuedIndex_DoesNotThrow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var tenant = new Party(Guid.NewGuid(), "Juan", "Pérez");
        var lessor = new Party(Guid.NewGuid(), "Ana", "Gómez");
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", "1", "Sunchales", "Santa Fe", "2322"));
        var index = new EconomicIndex(Guid.NewGuid(), "OLD-" + Guid.NewGuid().ToString("N")[..6]);
        await using (var seed = world.NewContext())
        {
            seed.Parties.AddRange(tenant, lessor);
            seed.Units.Add(unit);
            seed.EconomicIndices.Add(index);
            await seed.SaveChangesAsync();
        }

        await using (var c = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(c, "UPDATE economic_indices SET discontinued_from = DATE '2020-01-01' WHERE id = @id", ("id", index.Id));
        }

        var result = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(
            new ContractDraft
            {
                TenantId = tenant.Id,
                LessorIds = [lessor.Id],
                Shares = [new UnitShare(unit.Id, 100m)],
                StartDate = new DateOnly(2026, 12, 1),
                NominalEndDate = new DateOnly(2028, 11, 30),
                MonthlyRent = 1000m,
                HonorariosPercentage = 8m,
                Clause = new ClauseDraft([index.Id], 6),
            },
            false);

        Assert.True(result.Succeeded || result.Refusal is not null);
    }
}
