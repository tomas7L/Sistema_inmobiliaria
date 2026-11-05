using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
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
using NpgsqlTypes;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>From the independent verification of slice S3: every route to a stored cutover date, atomicity, odd amounts.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractCreationAdversarialTests
{
    private const string Admin = "inmobiliaria_admin";
    private const string Empleado = "inmobiliaria_empleado";
    private const string GuardConstraint = "contracts_cutover_insert_admin_only";
    private const string UpdateConstraint = "contracts_cutover_admin_only";
    private const string Columns = "id, start_date, nominal_end_date, monthly_rent, honorarios_percentage, status, due_day, cutover_date";

    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly FixedClock Clock = new(Today);

    private readonly PostgresFixture _fixture;

    public ContractCreationAdversarialTests(PostgresFixture fixture) => _fixture = fixture;

    private sealed record Cast(Guid Tenant, Guid Lessor, Guid Unit);

    private static async Task<Guid> PartyAsync(DeskWorld world, string first, string last)
    {
        await using var seed = world.NewContext();
        var party = new Party(Guid.NewGuid(), first, last);
        seed.Parties.Add(party);
        await seed.SaveChangesAsync();
        return party.Id;
    }

    private static async Task<Guid> UnitAsync(DeskWorld world, string number = "1258")
    {
        await using var seed = world.NewContext();
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", number, "Sunchales", "Santa Fe", "2322"));
        seed.Units.Add(unit);
        await seed.SaveChangesAsync();
        return unit.Id;
    }

    private static async Task<Cast> CastAsync(DeskWorld world) =>
        new(await PartyAsync(world, "Juan", "Pérez"), await PartyAsync(world, "Ana", "Gómez"), await UnitAsync(world));

    private static async Task<Guid> IndexAsync(DeskWorld world, string name = "IPC")
    {
        await using var seed = world.NewContext();
        var index = new EconomicIndex(Guid.NewGuid(), name + "-" + Guid.NewGuid().ToString("N")[..6]);
        seed.EconomicIndices.Add(index);
        await seed.SaveChangesAsync();
        return index.Id;
    }

    private static ContractDraft Draft(Cast cast) => new()
    {
        TenantId = cast.Tenant,
        LessorIds = [cast.Lessor],
        Shares = [new UnitShare(cast.Unit, 100m)],
        StartDate = new DateOnly(2026, 12, 1),
        NominalEndDate = new DateOnly(2028, 11, 30),
        MonthlyRent = 450_000m,
        HonorariosPercentage = 8m,
    };

    private static async Task<NpgsqlConnection> OpenAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, role, password);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<(ContractRegistry Registry, InmobiliariaDbContext Context)> RegistryAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!) { Username = role, Password = password };
        var context = new InmobiliariaDbContext(new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);
        return (new ContractRegistry(context, Clock), context);
    }

    private static string Row(Guid id, string cutover) =>
        $"('{id}', DATE '2025-01-01', DATE '2026-12-31', 450000, 8, 'Active', 5, {cutover})";

    private static async Task<string> HashAsync(DeskWorld world)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var parts = new List<string>();
        foreach (var table in new[] { "contracts", "contract_parties", "contract_units", "adjustment_clauses", "adjustment_clause_indices", "contract_accounts" })
        {
            parts.Add(table + "=" + await ReciboTestSupport.ScalarAsync<string>(
                connection,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(to_jsonb(t)::text, '|' ORDER BY to_jsonb(t)::text)), '') FROM {table} t"));
        }

        return string.Join(";", parts);
    }

    private static async Task<long> WithCutoverAsync(DeskWorld world) => await world.CountAsync("SELECT count(*) FROM contracts WHERE cutover_date IS NOT NULL");

    private static void IsGuarded(PostgresException refused, string constraint = GuardConstraint)
    {
        Assert.Equal("P0001", refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    // ---- Every route an Empleado could use to store a cutover date ----------------------------------------------------

    [SkippableTheory]
    [InlineData("single")]
    [InlineData("multi-null-first")]
    [InlineData("multi-date-first")]
    [InlineData("insert-select")]
    [InlineData("cte")]
    [InlineData("on-conflict-nothing")]
    [InlineData("merge")]
    [InlineData("do-block")]
    [InlineData("temp-view")]
    public async Task Empleado_RawInsertRoutes_AreRefused(string route)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var d = "DATE '2026-06-30'";
        var view = $"v_{Guid.NewGuid():N}";
        var sql = route switch
        {
            "single" => $"INSERT INTO contracts ({Columns}) VALUES {Row(a, d)}",
            "multi-null-first" => $"INSERT INTO contracts ({Columns}) VALUES {Row(a, "NULL")}, {Row(b, d)}",
            "multi-date-first" => $"INSERT INTO contracts ({Columns}) VALUES {Row(a, d)}, {Row(b, "NULL")}",
            "insert-select" => $"INSERT INTO contracts ({Columns}) SELECT '{a}'::uuid, DATE '2025-01-01', DATE '2026-12-31', 450000, 8, 'Active', 5, {d}",
            "cte" => $"WITH w AS (INSERT INTO contracts ({Columns}) VALUES {Row(a, d)} RETURNING id) SELECT * FROM w",
            "on-conflict-nothing" => $"INSERT INTO contracts ({Columns}) VALUES {Row(a, d)} ON CONFLICT DO NOTHING",
            "merge" => $"MERGE INTO contracts c USING (VALUES ('{a}'::uuid)) v(id) ON c.id = v.id WHEN NOT MATCHED THEN INSERT ({Columns}) VALUES ('{a}', DATE '2025-01-01', DATE '2026-12-31', 450000, 8, 'Active', 5, {d})",
            "do-block" => $"DO $$ BEGIN INSERT INTO contracts ({Columns}) VALUES {Row(a, d)}; END $$",
            "temp-view" => $"INSERT INTO {view} ({Columns}) VALUES {Row(a, d)}",
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        if (route == "temp-view")
        {
            await ReciboTestSupport.ExecAsync(empleado, $"CREATE TEMP VIEW {view} AS SELECT * FROM contracts");
        }

        var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(empleado, sql));

        IsGuarded(refused);
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empleado_Copy_IsRefused(bool binary)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);

        var refused = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (binary)
            {
                await using var writer = await empleado.BeginBinaryImportAsync($"COPY contracts ({Columns}) FROM STDIN (FORMAT BINARY)");
                await writer.StartRowAsync();
                await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await writer.WriteAsync(new DateOnly(2025, 1, 1), NpgsqlDbType.Date);
                await writer.WriteAsync(new DateOnly(2026, 12, 31), NpgsqlDbType.Date);
                await writer.WriteAsync(450000m, NpgsqlDbType.Numeric);
                await writer.WriteAsync(8m, NpgsqlDbType.Numeric);
                await writer.WriteAsync("Active", NpgsqlDbType.Varchar);
                await writer.WriteAsync((short)5, NpgsqlDbType.Smallint);
                await writer.WriteAsync(new DateOnly(2026, 6, 30), NpgsqlDbType.Date);
                await writer.CompleteAsync();
            }
            else
            {
                await using var writer = await empleado.BeginTextImportAsync($"COPY contracts ({Columns}) FROM STDIN");
                await writer.WriteAsync($"{Guid.NewGuid()}\t2025-01-01\t2026-12-31\t450000\t8\tActive\t5\t2026-06-30\n");
            }
        });

        IsGuarded(refused);
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    [SkippableTheory]
    [InlineData("update")]
    [InlineData("on-conflict-update")]
    [InlineData("merge-update")]
    public async Task Empleado_UpdateRoutes_AreRefused_OnAContractWithNoDate(string route)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);
        var id = Guid.NewGuid();
        await ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(id, "NULL")}");
        var sql = route switch
        {
            "update" => $"UPDATE contracts SET cutover_date = DATE '2026-06-30' WHERE id = '{id}'",
            "on-conflict-update" => $"INSERT INTO contracts ({Columns}) VALUES {Row(id, "NULL")} ON CONFLICT (id) DO UPDATE SET cutover_date = DATE '2026-06-30'",
            "merge-update" => $"MERGE INTO contracts c USING (VALUES ('{id}'::uuid)) v(id) ON c.id = v.id WHEN MATCHED THEN UPDATE SET cutover_date = DATE '2026-06-30'",
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        var refused = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(empleado, sql));

        IsGuarded(refused, UpdateConstraint);
        Assert.Equal(0L, await WithCutoverAsync(world));
    }

    [SkippableTheory]
    [InlineData("SET ROLE inmobiliaria_admin")]
    [InlineData("SET LOCAL ROLE inmobiliaria_admin")]
    [InlineData("SET SESSION AUTHORIZATION postgres")]
    [InlineData("SET session_replication_role = replica")]
    [InlineData("ALTER TABLE contracts DISABLE TRIGGER contracts_cutover_insert_guard")]
    [InlineData("DROP TRIGGER contracts_cutover_insert_guard ON contracts")]
    [InlineData("CREATE OR REPLACE FUNCTION reject_cutover_insert_unless_admin() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql")]
    [InlineData("GRANT inmobiliaria_admin TO CURRENT_USER")]
    [InlineData("CREATE SCHEMA evil")]
    [InlineData("CREATE FUNCTION public.pg_has_role(name, text, text) RETURNS boolean AS 'SELECT true' LANGUAGE sql")]
    public async Task Empleado_CannotEscapeTheGuard(string attempt)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);

        await using (var tx = await empleado.BeginTransactionAsync())
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReciboTestSupport.ExecAsync(empleado, tx, attempt));
            Assert.Contains(denied.SqlState, new[] { "42501", "22023", "0LP01" });
        }

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}"));
        IsGuarded(refused);
    }

    /// <summary>A temp function cannot shadow pg_has_role: PostgreSQL never resolves functions through pg_temp unqualified.</summary>
    [SkippableFact]
    public async Task Empleado_TempFunctionShadow_DoesNotPass()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);
        await ReciboTestSupport.ExecAsync(empleado, "CREATE FUNCTION pg_temp.pg_has_role(name, text, text) RETURNS boolean AS 'SELECT true' LANGUAGE sql");
        await ReciboTestSupport.ExecAsync(empleado, "SET search_path = pg_temp, public");

        var refused = await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}"));

        IsGuarded(refused);
    }

    /// <summary>
    /// The guard names pg_catalog.pg_has_role, so even a login that somehow held CREATE on a schema cannot shadow it with
    /// its own search_path. The other guards in the migration history do not qualify it: none of them is proven by this.
    /// </summary>
    [SkippableFact]
    public async Task AShadowedPgHasRole_DoesNotPassTheGuard()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var empleado = await OpenAsAsync(world, Empleado);
        Assert.False(await ReciboTestSupport.ScalarAsync<bool>(empleado, "SELECT has_schema_privilege(current_user, 'public', 'CREATE')"));
        Assert.False(await ReciboTestSupport.ScalarAsync<bool>(empleado, "SELECT has_database_privilege(current_user, current_database(), 'CREATE')"));
        Assert.False(await ReciboTestSupport.ScalarAsync<bool>(
            empleado, "SELECT prosecdef FROM pg_proc WHERE proname = 'reject_cutover_insert_unless_admin'"));
        Assert.Equal("O", await ReciboTestSupport.ScalarAsync<string>(
            empleado, "SELECT tgenabled::text FROM pg_trigger WHERE tgname = 'contracts_cutover_insert_guard'"));

        await using (var super = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(super, "GRANT CREATE ON SCHEMA public TO inmobiliaria_empleado");
        }

        try
        {
            await ReciboTestSupport.ExecAsync(empleado, "CREATE FUNCTION public.pg_has_role(name, text, text) RETURNS boolean AS 'SELECT true' LANGUAGE sql");
            await ReciboTestSupport.ExecAsync(empleado, "SET search_path = public, pg_catalog");

            var refused = await ReciboTestSupport.RefusedAsync(() =>
                ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}"));

            Assert.Equal("contracts_cutover_insert_admin_only", refused.ConstraintName);
        }
        finally
        {
            await using var super = await ReciboTestSupport.OpenAsync(world.Database.Context);
            await ReciboTestSupport.ExecAsync(super, "DROP FUNCTION IF EXISTS public.pg_has_role(name, text, text); REVOKE CREATE ON SCHEMA public FROM inmobiliaria_empleado");
        }
    }

    [SkippableFact]
    public async Task Admin_EveryRoute_Succeeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var admin = await OpenAsAsync(world, Admin);
        var d = "DATE '2026-06-30'";

        await ReciboTestSupport.ExecAsync(admin, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), d)}");
        await ReciboTestSupport.ExecAsync(admin, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), d)}, {Row(Guid.NewGuid(), "NULL")}");
        await using (var writer = await admin.BeginTextImportAsync($"COPY contracts ({Columns}) FROM STDIN"))
        {
            await writer.WriteAsync($"{Guid.NewGuid()}\t2025-01-01\t2026-12-31\t450000\t8\tActive\t5\t2026-06-30\n");
        }

        Assert.Equal(3L, await WithCutoverAsync(world));

        var cast = await CastAsync(world);
        var (registry, context) = await RegistryAsAsync(world, Admin);
        await using (context)
        {
            var result = await registry.CreateAsync(Draft(cast) with { StartDate = new DateOnly(2024, 11, 1), CutoverDate = new DateOnly(2024, 11, 1) }, false);
            Assert.True(result.Succeeded, $"{result.Refusal}");
        }

        Assert.Equal(4L, await WithCutoverAsync(world));
    }

    /// <summary>The Empleado's port with a date, current-month and old start, with a clause: refused, and nothing moved.</summary>
    [SkippableTheory]
    [InlineData(2026, 11, 1, 2026, 11, 3)]
    [InlineData(2024, 11, 1, 2026, 11, 15)]
    public async Task Empleado_ThePort_IsRefusedAndHashUnchanged(int sy, int sm, int sd, int cy, int cm, int cd)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var seed = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(
            Draft(cast with { Unit = await UnitAsync(world, "1") }) with { Clause = new ClauseDraft([await IndexAsync(world)], 6) }, false);
        Assert.True(seed.Succeeded);
        var before = await HashAsync(world);
        var (registry, context) = await RegistryAsAsync(world, Empleado);
        await using (context)
        {
            var result = await registry.CreateAsync(
                Draft(cast) with
                {
                    StartDate = new DateOnly(sy, sm, sd),
                    CutoverDate = new DateOnly(cy, cm, cd),
                    Clause = new ClauseDraft([await IndexAsync(world, "ICL")], 12),
                },
                true);
            Assert.Equal(ContractRefusal.CutoverAdminOnly, result.Refusal);
        }

        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Down then Up ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task TheMigration_DownThenUp_RemovesAndRestores_AndKeepsData()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = database.Context.GetService<IMigrator>();
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);
        const string Objects =
            """
            SELECT (SELECT count(*) FROM pg_proc WHERE proname = 'reject_cutover_insert_unless_admin')
                 + (SELECT count(*) FROM pg_trigger WHERE tgname = 'contracts_cutover_insert_guard')
                 + (SELECT count(*) FROM pg_indexes WHERE indexname = 'ux_contract_parties_one_tenant')
                 + (SELECT count(*) FROM information_schema.columns WHERE table_name = 'contract_parties' AND column_name = 'removed_at')
            """;
        Assert.Equal(4L, await ReciboTestSupport.ScalarAsync<long>(connection, Objects));

        var id = Guid.NewGuid();
        await ReciboTestSupport.ExecAsync(connection, $"INSERT INTO contracts ({Columns}) VALUES {Row(id, "DATE '2026-06-30'")}");
        const string RowSql = "SELECT to_jsonb(c)::text FROM contracts c WHERE id = @id";
        var row = await ReciboTestSupport.ScalarAsync<string>(connection, RowSql, ("id", id));

        await migrator.MigrateAsync("20261025005832_FixClearMustChangePasswordIdentity");
        Assert.Equal(0L, await ReciboTestSupport.ScalarAsync<long>(connection, Objects));
        Assert.Equal(row, await ReciboTestSupport.ScalarAsync<string>(connection, RowSql, ("id", id)));

        var (role, password) = await CreateTestLoginRoleAsync(database.Context, Empleado);
        await using var empleado = BuildConnectionAs(database.Context, role, password);
        await empleado.OpenAsync();
        Assert.Equal(1, await ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}"));
        await empleado.CloseAsync();

        await migrator.MigrateAsync();
        Assert.Equal(4L, await ReciboTestSupport.ScalarAsync<long>(connection, Objects));
        Assert.Equal(row, await ReciboTestSupport.ScalarAsync<string>(connection, RowSql, ("id", id)));

        await empleado.OpenAsync();
        IsGuarded(await ReciboTestSupport.RefusedAsync(() =>
            ReciboTestSupport.ExecAsync(empleado, $"INSERT INTO contracts ({Columns}) VALUES {Row(Guid.NewGuid(), "DATE '2026-06-30'")}")));
    }

    // ---- Atomicity ------------------------------------------------------------------------------------------------------

    /// <summary>Every refusal the port can give leaves the six tables byte for byte as they were, and releases the lock.</summary>
    [SkippableFact]
    public async Task EveryRefusal_LeavesTheDataHashUnchanged_AndReleasesTheUnitLock()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var seeded = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(
            Draft(cast) with { Clause = new ClauseDraft([await IndexAsync(world)], 6) }, false);
        Assert.True(seeded.Succeeded);
        var free = await UnitAsync(world, "2");
        var before = await HashAsync(world);
        var good = Draft(cast with { Unit = free });
        var ok = new ClauseDraft([await IndexAsync(world, "ICL")], 6);

        (ContractDraft Draft, ContractRefusal Expected)[] cases =
        [
            (good with { TenantId = null, Clause = ok }, ContractRefusal.TenantMissing),
            (good with { LessorIds = [], Clause = ok }, ContractRefusal.LessorMissing),
            (good with { Shares = [], Clause = ok }, ContractRefusal.UnitMissing),
            (good with { StartDate = null }, ContractRefusal.DatesMissing),
            (good with { StartDate = new DateOnly(2026, 12, 15) }, ContractRefusal.StartNotOnTheFirst),
            (good with { NominalEndDate = new DateOnly(2026, 11, 30) }, ContractRefusal.EndBeforeStart),
            (good with { StartDate = new DateOnly(2026, 10, 1) }, ContractRefusal.StartBeforeCurrentMonth),
            (good with { MonthlyRent = 100.5m }, ContractRefusal.RentNotWholePositive),
            (good with { Shares = [new UnitShare(free, 99m)] }, ContractRefusal.SharesNotHundred),
            (Draft(cast), ContractRefusal.UnitUnderActiveContract),
            (good with { TenantId = Guid.NewGuid() }, ContractRefusal.PartyNotFound),
            (good with { Shares = [new UnitShare(Guid.NewGuid(), 100m)] }, ContractRefusal.UnitNotFound),
            (good with { Clause = new ClauseDraft([Guid.NewGuid()], 6) }, ContractRefusal.IndexNotFound),
        ];

        foreach (var (draft, expected) in cases)
        {
            await using var context = world.NewContext();
            var result = await new ContractRegistry(context, Clock).CreateAsync(draft, true);
            Assert.Equal(expected, result.Refusal);
            Assert.Equal(before, await HashAsync(world));

            await using var other = await ReciboTestSupport.OpenAsync(world.Database.Context);
            await ReciboTestSupport.ExecAsync(other, "BEGIN");
            await ReciboTestSupport.ExecAsync(other, "SELECT 1 FROM units FOR UPDATE NOWAIT");
            await ReciboTestSupport.ExecAsync(other, "ROLLBACK");
        }

        // Warnings not yet accepted write nothing either.
        var warned = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(good with { HonorariosPercentage = null }, false);
        Assert.NotEmpty(warned.Warnings);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>A real database failure after an insert into each child table: nothing stays.</summary>
    [SkippableTheory]
    [InlineData("contract_accounts")]
    [InlineData("adjustment_clause_indices")]
    [InlineData("adjustment_clauses")]
    [InlineData("contract_units")]
    [InlineData("contract_parties")]
    public async Task AFailureAfterAnyInsert_RollsEverythingBack(string table)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var unit = await UnitAsync(world, "3");
        var index = await IndexAsync(world);
        var first = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast with { Unit = unit }) with { Clause = new ClauseDraft([index], 6) }, false);
        Assert.True(first.Succeeded);
        var before = await HashAsync(world);
        await using (var super = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(
                super,
                $"""
                CREATE FUNCTION probe_fail() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'probe failure'; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER probe_fail AFTER INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION probe_fail();
                """);
        }

        await using var context = world.NewContext();
        await Assert.ThrowsAnyAsync<Exception>(() => new ContractRegistry(context, Clock).CreateAsync(Draft(cast) with { Clause = new ClauseDraft([index], 6) }, false));

        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>The whole contract is written by one transaction, clause included: every row carries the same xmin.</summary>
    [SkippableFact]
    public async Task TheClause_IsWrittenInTheSameTransaction()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var one = await IndexAsync(world, "IPC");
        var two = await IndexAsync(world, "ICL");
        var (registry, context) = await RegistryAsAsync(world, Empleado);
        await using (context)
        {
            var result = await registry.CreateAsync(Draft(cast) with { Clause = new ClauseDraft([two, one], 6), CodebtorIds = [await PartyAsync(world, "Raúl", "Vera")] }, false);
            Assert.True(result.Succeeded, $"{result.Refusal}");
            var id = result.Contract!.Id;
            await using var c = await ReciboTestSupport.OpenAsync(world.Database.Context);
            var xmins = await ReciboTestSupport.ScalarAsync<long>(
                c,
                """
                SELECT count(DISTINCT x) FROM (
                  SELECT xmin::text x FROM contracts WHERE id = @id
                  UNION ALL SELECT xmin::text FROM contract_parties WHERE contract_id = @id
                  UNION ALL SELECT xmin::text FROM contract_units WHERE contract_id = @id
                  UNION ALL SELECT xmin::text FROM contract_accounts WHERE contract_id = @id
                  UNION ALL SELECT xmin::text FROM adjustment_clauses WHERE contract_id = @id
                  UNION ALL SELECT i.xmin::text FROM adjustment_clause_indices i JOIN adjustment_clauses a ON a.id = i.adjustment_clause_id WHERE a.contract_id = @id) s
                """,
                ("id", id));
            Assert.Equal(1L, xmins);

            // The order the operator chose is the order stored.
            var detail = await registry.DetailAsync(id);
            Assert.Equal(2, detail!.Clause!.IndexNames.Count);
            Assert.StartsWith("ICL", detail.Clause.IndexNames[0], StringComparison.Ordinal);
        }
    }

    // ---- Duplicates -----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ADuplicateLessor_IsOneRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var result = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast) with { LessorIds = [cast.Lessor, cast.Lessor] }, false);
        Assert.True(result.Succeeded, $"{result.Refusal}");
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_parties WHERE role = 'Lessor'"));
    }

    /// <summary>No rule forbids it; it must not reach a generic error, and readers cope.</summary>
    [SkippableFact]
    public async Task APersonBothTenantAndLessor_AndCodebtor_DoesNotThrow_AndReadersCope()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        await using var context = world.NewContext();
        var registry = new ContractRegistry(context, Clock);
        var result = await registry.CreateAsync(Draft(cast) with { LessorIds = [cast.Tenant], CodebtorIds = [cast.Tenant] }, false);
        Assert.True(result.Succeeded, $"{result.Refusal}");
        Assert.Equal(3, await world.CountAsync("SELECT count(*) FROM contract_parties"));
        Assert.Equal(3, (await registry.DetailAsync(result.Contract!.Id))!.Parties.Count);
        await using var directory = world.NewContext();
        Assert.Single(await new AccountDirectory(directory, Clock).SearchAsync("Pérez"));
    }

    [SkippableFact]
    public async Task AUnitRepeatedInShares_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var before = await HashAsync(world);
        var result = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(
            Draft(cast) with { Shares = [new UnitShare(cast.Unit, 50m), new UnitShare(cast.Unit, 50m)] }, false);
        Assert.Equal(ContractRefusal.SharesNotHundred, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Odd amounts: nothing an operator can type reaches an exception --------------------------------------------------

    public static TheoryData<string, decimal, decimal?, decimal?, int?, bool> Amounts => new()
    {
        { "max canon", 999_999_999_999m, 8m, null, null, true },
        { "canon with scale", 450_000.0000m, 8m, null, null, true },
        { "canon just over", 1_000_000_000_000m, 8m, null, null, false },
        { "canon at decimal max", decimal.MaxValue, 8m, null, null, false },
        { "canon at decimal min", decimal.MinValue, 8m, null, null, false },
        { "canon negative", -1m, 8m, null, null, false },
        { "canon zero", 0m, 8m, null, null, false },
        { "tiny canon", 0.0000000000000000000000000001m, 8m, null, null, false },
        { "rate 100", 1000m, 100m, null, null, true },
        { "rate 100 scaled", 1000m, 100.0000m, null, null, true },
        { "rate zero", 1000m, 0m, null, null, true },
        { "rate two places", 1000m, 33.33m, null, null, true },
        { "rate three places", 1000m, 33.333m, null, null, false },
        { "rate over", 1000m, 100.01m, null, null, false },
        { "rate negative", 1000m, -0.01m, null, null, false },
        { "rate huge", 1000m, decimal.MaxValue, null, null, false },
        { "rate tiny", 1000m, 0.0000000000000000000000000001m, null, null, false },
        { "fee max", 1000m, 8m, 999_999_999_999.99m, 6, true },
        { "fee cents", 1000m, 8m, 0.01m, 1, true },
        { "fee over", 1000m, 8m, 1_000_000_000_000m, 6, false },
        { "fee three places", 1000m, 8m, 1.005m, 1, false },
        { "fee scaled", 1000m, 8m, 1.5000m, 1, true },
        { "fee huge", 1000m, 8m, decimal.MaxValue, 6, false },
        { "fee zero", 1000m, 8m, 0m, 1, false },
        { "fee negative", 1000m, 8m, -5m, 1, false },
        { "fee 0 instalments", 1000m, 8m, 10m, 0, false },
        { "fee 7 instalments", 1000m, 8m, 10m, 7, false },
        { "fee no instalments", 1000m, 8m, 10m, null, false },
        { "instalments no fee", 1000m, 8m, null, 3, false },
    };

    [SkippableTheory]
    [MemberData(nameof(Amounts))]
    public async Task OddAmounts_AreWrittenOrRefused_NeverThrown(string label, decimal rent, decimal? rate, decimal? fee, int? instalments, bool writes)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var before = await HashAsync(world);
        await using var context = world.NewContext();

        var result = await new ContractRegistry(context, Clock).CreateAsync(
            Draft(cast) with { MonthlyRent = rent, HonorariosPercentage = rate, FeePlanAmount = fee, FeePlanInstalments = instalments }, true);

        Assert.True(writes == result.Succeeded, $"{label}: refusal {result.Refusal}");
        if (!writes)
        {
            Assert.NotNull(result.Refusal);
            Assert.NotEmpty(result.Refusal!.Value.OperatorText());
            Assert.Equal(before, await HashAsync(world));
        }
        else
        {
            await using var c = await ReciboTestSupport.OpenAsync(world.Database.Context);
            Assert.Equal(rent, await ReciboTestSupport.ScalarAsync<decimal>(c, "SELECT monthly_rent FROM contracts"));
        }
    }

    /// <summary>Typed percentages so large that summing them overflows decimal.</summary>
    [SkippableFact]
    public async Task HugeShares_AreRefused_NotThrown()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var other = await UnitAsync(world, "9");
        await using var context = world.NewContext();

        var result = await new ContractRegistry(context, Clock).CreateAsync(
            Draft(cast) with { Shares = [new UnitShare(cast.Unit, decimal.MaxValue), new UnitShare(other, decimal.MaxValue)] }, true);

        Assert.Equal(ContractRefusal.SharesNotHundred, result.Refusal);
    }

    [SkippableTheory]
    [InlineData(0, false)]
    [InlineData(32, false)]
    [InlineData(-1, false)]
    [InlineData(31, true)]
    [InlineData(1, true)]
    public async Task TheDueDay_IsWrittenOrRefused(int due, bool writes)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var result = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast) with { DueDay = due }, true);
        Assert.Equal(writes, result.Succeeded);
    }

    [SkippableTheory]
    [InlineData(0, false)]
    [InlineData(61, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    public async Task TheClauseInterval_IsWrittenOrRefused(int interval, bool writes)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var result = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(
            Draft(cast) with { Clause = new ClauseDraft([await IndexAsync(world)], interval) }, true);
        Assert.Equal(writes, result.Succeeded);
    }

    /// <summary>Extreme dates an operator can type, written or refused but never thrown, and the readers still read them.</summary>
    [SkippableTheory]
    [InlineData(2026, 12, 1, 9999, 12, 31)]
    [InlineData(9999, 12, 1, 9999, 12, 31)]
    public async Task ExtremeDates_DoNotThrow(int sy, int sm, int sd, int ey, int em, int ed)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        await using var context = world.NewContext();
        var registry = new ContractRegistry(context, Clock);
        var result = await registry.CreateAsync(Draft(cast) with { StartDate = new DateOnly(sy, sm, sd), NominalEndDate = new DateOnly(ey, em, ed) }, true);
        Assert.True(result.Succeeded, $"{result.Refusal}");
        Assert.NotNull(await registry.DetailAsync(result.Contract!.Id));
        Assert.Single(await registry.ListAsync(null, null));
    }

    [SkippableFact]
    public async Task ACutoverOnYearOne_AsAdmin_DoesNotThrow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        await using var context = world.NewContext();
        var result = await new ContractRegistry(context, Clock).CreateAsync(
            Draft(cast) with { StartDate = new DateOnly(1, 1, 1), NominalEndDate = new DateOnly(1, 1, 1), CutoverDate = new DateOnly(1, 1, 1) }, true);
        Assert.True(result.Succeeded, $"{result.Refusal}");
    }

    // ---- Requirement 8: no accrual on or before the cutover, however read --------------------------------------------------

    /// <summary>Materialiser, mora worklist and desk previews: none writes a period due on or before the cutover.</summary>
    [SkippableTheory]
    [InlineData(10, 2026, 11, 10)] // cutover ON a due date: excluded.
    [InlineData(10, 2026, 11, 9)]  // the day before a due date: that period accrues.
    [InlineData(31, 2026, 9, 30)]  // clamped due date in a short month, on the cutover.
    public async Task NoAccrual_OnOrBeforeTheCutover_HoweverRead(int dueDay, int cy, int cm, int cd)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var cutover = new DateOnly(cy, cm, cd);
        var draft = Draft(cast) with
        {
            StartDate = new DateOnly(2024, 11, 1),
            NominalEndDate = new DateOnly(2028, 10, 31),
            CutoverDate = cutover,
            DueDay = dueDay,
            FeePlanAmount = 50_000m,
            FeePlanInstalments = 6,
        };
        var (registry, context) = await RegistryAsAsync(world, Admin);
        await using (context)
        {
            var created = await registry.CreateAsync(draft, false);
            Assert.True(created.Succeeded, $"{created.Refusal}");
            await using var reading = world.NewContext();
            var account = await reading.ContractAccounts.AsNoTracking().SingleAsync(a => a.ContractId == created.Contract!.Id);

            await world.MaterialiseAsync(account.Id, Today);
            await using (var mora = world.NewContext())
            {
                await new MoraWorklistQuery(mora, new AccountMaterialiser(mora)).GetAccountsInMoraAsync(Today, DeskWorld.Terms);
            }

            await using (var deskContext = world.NewContext())
            {
                await world.NewDesk(Clock, deskContext).PreviewAsync(new CollectionRequest(account.Id, null, null, 0m));
            }

            await using (var deskContext = world.NewContext())
            {
                await world.NewDesk(Clock, deskContext).PreviewAsync(new CollectionRequest(account.Id, new DateOnly(2025, 3, 1), null, 0m));
            }

            await using var c = await ReciboTestSupport.OpenAsync(world.Database.Context);
            var offenders = await ReciboTestSupport.ScalarAsync<long>(
                c,
                """
                SELECT count(*) FROM account_movements m
                WHERE m.account_id = @a AND m.kind IN ('RentAccrual', 'ContractualFeeInstalment')
                  AND make_date(extract(year FROM m.period)::int, extract(month FROM m.period)::int,
                        LEAST(@due, extract(day FROM (date_trunc('month', m.period) + interval '1 month - 1 day'))::int)) <= @cutover
                """,
                ("a", account.Id), ("due", dueDay), ("cutover", cutover));
            Assert.Equal(0L, offenders);

            var nov = await ReciboTestSupport.ScalarAsync<long>(c, "SELECT count(*) FROM account_movements WHERE account_id = @a AND kind = 'RentAccrual' AND period = DATE '2026-11-01'", ("a", account.Id));
            var dueNov = new DateOnly(2026, 11, Math.Min(dueDay, 30));
            Assert.Equal(dueNov > cutover && dueNov <= Today ? 1L : 0L, nov);
        }
    }

    // ---- Readers with no removed rows, and a contract with no parties (F6 raw route) --------------------------------------

    [SkippableFact]
    public async Task ReadersCope_WithAContractOnlyRawSqlCouldLeave_WithNoParties()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await using var c = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var id = Guid.NewGuid();
        await ReciboTestSupport.ExecAsync(c, $"INSERT INTO contracts ({Columns}) VALUES {Row(id, "NULL")}");
        await using var context = world.NewContext();
        var registry = new ContractRegistry(context, Clock);
        Assert.Single(await registry.ListAsync(null, null));
        Assert.Empty(await registry.ListAsync("Pérez", null));
        Assert.NotNull(await registry.DetailAsync(id));
    }

    /// <summary>A removed row is hidden from the reader; a raw UPDATE of removed_at by an Empleado is possible (advisory for S4).</summary>
    [SkippableFact]
    public async Task ARemovedRow_IsHiddenFromReaders()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var created = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast) with { CodebtorIds = [await PartyAsync(world, "Raúl", "Vera")] }, false);
        await using var empleado = await OpenAsAsync(world, Empleado);
        var written = await ReciboTestSupport.ExecAsync(
            empleado, "UPDATE contract_parties SET removed_at = now() WHERE role = 'Codebtor' AND contract_id = @id", ("id", created.Contract!.Id));
        Assert.Equal(1, written);
        await using var context = world.NewContext();
        Assert.Equal(2, (await new ContractRegistry(context, Clock).DetailAsync(created.Contract.Id))!.Parties.Count);
    }

    // ---- Concurrency ----------------------------------------------------------------------------------------------------

    /// <summary>Two real creations racing for overlapping units in opposite order: one wins, one is refused, no deadlock.</summary>
    [SkippableFact]
    public async Task OverlappingUnits_InOppositeOrder_NeverDeadlock_NorDoubleLet()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var tenant = await PartyAsync(world, "Juan", "Pérez");
        var lessor = await PartyAsync(world, "Ana", "Gómez");
        for (var round = 0; round < 10; round++)
        {
            var a = await UnitAsync(world, $"{round}a");
            var b = await UnitAsync(world, $"{round}b");
            var cast = new Cast(tenant, lessor, a);
            var first = Draft(cast) with { Shares = [new UnitShare(a, 50m), new UnitShare(b, 50m)] };
            var second = Draft(cast) with { Shares = [new UnitShare(b, 50m), new UnitShare(a, 50m)] };
            await using var c1 = world.NewContext();
            await using var c2 = world.NewContext();
            var results = await Task.WhenAll(
                Task.Run(() => new ContractRegistry(c1, Clock).CreateAsync(first, true)),
                Task.Run(() => new ContractRegistry(c2, Clock).CreateAsync(second, true)));
            Assert.Equal(1, results.Count(r => r.Succeeded));
            Assert.Equal(ContractRefusal.UnitUnderActiveContract, results.Single(r => !r.Succeeded).Refusal);
        }
    }

    /// <summary>A unit under a PendingTermination contract AND its Active successor: the Active one decides (Decision 30).</summary>
    [SkippableFact]
    public async Task AUnitWithAVacatingAndASuccessorContract_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var cast = await CastAsync(world);
        var first = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast), false);
        await using (var c = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(
                c, "UPDATE contracts SET status = 'PendingTermination', notice_given_date = DATE '2026-11-01', planned_move_out_date = DATE '2027-01-31' WHERE id = @id", ("id", first.Contract!.Id));
        }

        var successor = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast) with { TenantId = await PartyAsync(world, "Lía", "Sosa") }, true);
        Assert.True(successor.Succeeded, $"{successor.Refusal}");

        var third = await new ContractRegistry(world.NewContext(), Clock).CreateAsync(Draft(cast) with { TenantId = await PartyAsync(world, "Otro", "Más") }, true);
        Assert.Equal(ContractRefusal.UnitUnderActiveContract, third.Refusal);
        Assert.Contains("Lía", third.CurrentTenant!, StringComparison.Ordinal);
    }
}
