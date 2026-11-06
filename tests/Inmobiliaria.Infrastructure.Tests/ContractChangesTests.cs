using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 23 to 29 and 80 (Requirements 9 and 10) against real Postgres, as real Admin and Empleado
/// logins, with effects read back by raw SQL. All people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractChangesTests
{
    private const string Admin = "inmobiliaria_admin";
    private const string Empleado = "inmobiliaria_empleado";

    private static readonly DateOnly Today = DeskWorld.Today;
    private static readonly FixedClock Clock = new(Today);
    private static readonly DateOnly NextMonth = DeskWorld.FirstOfMonth(Today).AddMonths(1);

    private readonly PostgresFixture _fixture;

    public ContractChangesTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string, string> RolesAndFields()
    {
        var data = new TheoryData<string, string>();
        foreach (var role in new[] { Admin, Empleado })
        {
            foreach (var field in Fields)
            {
                data.Add(role, field);
            }
        }

        return data;
    }

    public static TheoryData<string> LockedFields()
    {
        var data = new TheoryData<string>();
        foreach (var field in Fields)
        {
            data.Add(field);
        }

        return data;
    }

    private static readonly string[] Fields = ["start", "end", "rent", "dueDay", "feePlan", "tenant", "lessors", "unit"];

    private sealed record Seeded(Guid Contract, Guid Account, Guid Tenant, Guid Lessor, Guid Unit);

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static ContractRegistry Registry(DeskWorld world, IAgencyClock? clock = null, RecargoTerms? terms = null) =>
        new(world.NewContext(), clock ?? Clock, terms ?? DeskWorld.Terms);

    /// <summary>A registry on a context logged in as a real member of <paramref name="groupRole"/>, with the login's name.</summary>
    private static async Task<(ContractRegistry Registry, InmobiliariaDbContext Context, string Login)> AsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!) { Username = role, Password = password };
        var context = new InmobiliariaDbContext(new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);

        return (new ContractRegistry(context, Clock, DeskWorld.Terms), context, role);
    }

    private static async Task<NpgsqlConnection> OpenAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, role, password);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Seeded> SeedAsync(
        DeskWorld world, DateOnly? start = null, decimal? rate = 8m, int dueDay = Contract.DefaultDueDay, ContractualFeePlan? plan = null)
    {
        var accountId = await world.SeedAccountAsync(start ?? NextMonth, dueDay, plan, rate);
        await using var read = world.NewContext();
        var account = await read.ContractAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        var contract = await read.Contracts.AsNoTracking().Include(c => c.Parties).Include(c => c.Units).SingleAsync(c => c.Id == account.ContractId);

        return new Seeded(
            contract.Id,
            accountId,
            contract.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId,
            contract.Parties.Single(p => p.Role == PartyRole.Lessor).PartyId,
            contract.Units.Single().UnitId);
    }

    /// <summary>Gives the account a movement: the first period of a contract starting next month, through its due date.</summary>
    private static Task WriteFirstAccrualAsync(DeskWorld world, Seeded seeded) => world.MaterialiseAsync(seeded.Account, NextMonth.AddDays(14));

    private static async Task<Guid> PersonAsync(DeskWorld world, string first = "Raúl", string last = "Vera", Guid? id = null)
    {
        await using var seed = world.NewContext();
        var party = new Party(id ?? Guid.NewGuid(), first, last);
        seed.Parties.Add(party);
        await seed.SaveChangesAsync();
        return party.Id;
    }

    private static async Task<Guid> UnitAsync(DeskWorld world, string number = "40")
    {
        await using var seed = world.NewContext();
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", number, "Sunchales", "Santa Fe", "2322"));
        seed.Units.Add(unit);
        await seed.SaveChangesAsync();
        return unit.Id;
    }

    private static ContractCorrection Standing(Seeded seeded, DateOnly? start = null)
    {
        var from = start ?? NextMonth;
        return new ContractCorrection
        {
            TenantId = seeded.Tenant,
            LessorIds = [seeded.Lessor],
            Shares = [new UnitShare(seeded.Unit, 100m)],
            StartDate = from,
            NominalEndDate = from.AddYears(2).AddDays(-1),
            MonthlyRent = 450_000m,
        };
    }

    private static async Task<ContractCorrection> EditedAsync(DeskWorld world, Seeded seeded, string field)
    {
        var standing = Standing(seeded);
        return field switch
        {
            "start" => standing with { StartDate = NextMonth.AddMonths(1) },
            "end" => standing with { NominalEndDate = NextMonth.AddYears(3) },
            "rent" => standing with { MonthlyRent = 500_000m },
            "dueDay" => standing with { DueDay = 5 },
            "feePlan" => standing with { FeePlanAmount = 5_000m, FeePlanInstalments = 3 },
            "tenant" => standing with { TenantId = await PersonAsync(world) },
            "lessors" => standing with { LessorIds = [await PersonAsync(world)] },
            "unit" => standing with { Shares = [new UnitShare(await UnitAsync(world), 100m)] },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No such field."),
        };
    }

    private static void AssertHolds(ContractDetail detail, ContractCorrection correction, string field)
    {
        switch (field)
        {
            case "start": Assert.Equal(correction.StartDate, detail.StartDate); break;
            case "end": Assert.Equal(correction.NominalEndDate, detail.NominalEndDate); break;
            case "rent": Assert.Equal(correction.MonthlyRent, detail.MonthlyRent); break;
            case "dueDay": Assert.Equal(correction.DueDay, detail.DueDay); break;
            case "feePlan": Assert.Equal(new ContractualFeePlan(5_000m, 3), detail.FeePlan); break;
            case "tenant": Assert.Equal(correction.TenantId, detail.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId); break;
            case "lessors": Assert.Equal(correction.LessorIds, detail.Parties.Where(p => p.Role == PartyRole.Lessor).Select(p => p.PartyId)); break;
            default: Assert.Equal(correction.Shares[0].UnitId, Assert.Single(detail.Units).UnitId); break;
        }
    }

    /// <summary>Every row a change could touch, so a refusal can be shown to have written nothing.</summary>
    private static async Task<string> HashAsync(DeskWorld world)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var parts = new List<string>();
        foreach (var table in new[] { "contracts", "contract_parties", "contract_units", "contract_events", "account_movements" })
        {
            parts.Add(table + "=" + await ReciboTestSupport.ScalarAsync<string>(
                connection, $"SELECT coalesce(string_agg(t::text, '|' ORDER BY t::text), '') FROM {table} t"));
        }

        return string.Join(";", parts);
    }

    private static async Task<T> ContractColumnAsync<T>(DeskWorld world, Guid contract, string column)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        return await ReciboTestSupport.ScalarAsync<T>(connection, $"SELECT {column} FROM contracts WHERE id = @id", ("id", contract));
    }

    private static void AssertDone(ContractResult result) =>
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}; warnings: {string.Join(", ", result.Warnings.Select(w => w.Kind))}.");

    // ---- Test 23: each locked field, before and after the first movement ----------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(RolesAndFields))]
    public async Task EachLockedField_IsCorrectableBeforeTheFirstMovement(string role, string field)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var correction = await EditedAsync(world, seeded, field);
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            AssertDone(await registry.CorrectAsync(seeded.Contract, correction, false));
        }

        AssertHolds((await Registry(world).DetailAsync(seeded.Contract))!, correction, field);
    }

    [SkippableTheory]
    [MemberData(nameof(LockedFields))]
    public async Task EachLockedField_IsRefusedAfterAMovement_NamingWhy_AndWritesNothing(string field)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await WriteFirstAccrualAsync(world, seeded);
        var correction = await EditedAsync(world, seeded, field);
        var before = await HashAsync(world);
        var (registry, context, _) = await AsAsync(world, Empleado);

        await using (context)
        {
            var result = await registry.CorrectAsync(seeded.Contract, correction, true);

            Assert.Equal(ContractRefusal.AccountHasMovements, result.Refusal);
            Assert.Contains("cancelar el contrato", result.Refusal!.Value.OperatorText(), StringComparison.Ordinal);
        }

        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>A correction's checks are creation's: nothing the form would refuse gets through, and nothing is written.</summary>
    [SkippableFact]
    public async Task ACorrection_TakesTheChecksOfCreation()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var standing = Standing(seeded);
        var before = await HashAsync(world);

        (ContractCorrection Correction, ContractRefusal Refusal)[] cases =
        [
            (standing with { TenantId = null }, ContractRefusal.TenantMissing),
            (standing with { LessorIds = [] }, ContractRefusal.LessorMissing),
            (standing with { Shares = [] }, ContractRefusal.UnitMissing),
            (standing with { StartDate = null }, ContractRefusal.DatesMissing),
            (standing with { StartDate = NextMonth.AddDays(1) }, ContractRefusal.StartNotOnTheFirst),
            (standing with { NominalEndDate = NextMonth.AddDays(-1) }, ContractRefusal.EndBeforeStart),
            (standing with { StartDate = DeskWorld.FirstOfMonth(Today).AddMonths(-1) }, ContractRefusal.StartBeforeCurrentMonth),
            (standing with { MonthlyRent = 0m }, ContractRefusal.RentNotWholePositive),
            (standing with { MonthlyRent = 1.5m }, ContractRefusal.RentNotWholePositive),
            (standing with { DueDay = 32 }, ContractRefusal.DueDayOutOfRange),
            (standing with { FeePlanAmount = 5_000m }, ContractRefusal.FeePlanInvalid),
            (standing with { Shares = [new UnitShare(seeded.Unit, 90m)] }, ContractRefusal.SharesNotHundred),
            (standing with { TenantId = Guid.NewGuid() }, ContractRefusal.PartyNotFound),
            (standing with { LessorIds = [Guid.NewGuid()] }, ContractRefusal.PartyNotFound),
            (standing with { Shares = [new UnitShare(Guid.NewGuid(), 100m)] }, ContractRefusal.UnitNotFound),
        ];

        foreach (var (correction, refusal) in cases)
        {
            Assert.Equal(refusal, (await Registry(world).CorrectAsync(seeded.Contract, correction, true)).Refusal);
        }

        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableFact]
    public async Task AContractThatIsNotThere_IsRefusedByEveryChange()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var missing = Guid.NewGuid();

        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).CorrectAsync(missing, Standing(seeded), true)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SetRateAsync(missing, 8m)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SetCodebtorsAsync(missing, [])).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).GiveNoticeAsync(missing, Today, Today)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SummarizeEndAsync(missing, EndReason.Expiry, Today)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).EndAsync(missing, EndReason.Expiry, Today)).Refusal);
    }

    /// <summary>A contract that waited a month with no movement must not be stuck by a start date nobody changed.</summary>
    [SkippableFact]
    public async Task AStartAlreadyInThePast_DoesNotStopACorrection_UnlessItMoves()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var start = DeskWorld.OldestPeriod;
        var seeded = await SeedAsync(world, start);
        var standing = Standing(seeded, start);

        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, standing with { MonthlyRent = 500_000m }, false));

        Assert.Equal(500_000m, await ContractColumnAsync<decimal>(world, seeded.Contract, "monthly_rent"));
        Assert.Equal(
            ContractRefusal.StartBeforeCurrentMonth,
            (await Registry(world).CorrectAsync(seeded.Contract, standing with { StartDate = start.AddMonths(-1) }, true)).Refusal);
    }

    /// <summary>The cutover guard fires on INSERT and on changing the date. Correcting anything else stays open to an Empleado.</summary>
    [SkippableFact]
    public async Task AnEmpleado_CanCorrectAContractWithACutover_WithoutTouchingTheDate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var start = DeskWorld.OldestPeriod;
        var seeded = await SeedAsync(world, start);
        var cutover = start.AddDays(20);
        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(connection, "UPDATE contracts SET cutover_date = @c WHERE id = @id", ("c", cutover), ("id", seeded.Contract));
        }

        var standing = Standing(seeded, start);
        var (registry, context, _) = await AsAsync(world, Empleado);
        await using (context)
        {
            AssertDone(await registry.CorrectAsync(seeded.Contract, standing with { MonthlyRent = 500_000m }, false));
            Assert.Equal(
                ContractRefusal.CutoverBeforeStart,
                (await registry.CorrectAsync(seeded.Contract, standing with { StartDate = start.AddMonths(1) }, true)).Refusal);
        }

        Assert.Equal(cutover, await ContractColumnAsync<DateOnly>(world, seeded.Contract, "cutover_date"));
    }

    [SkippableFact]
    public async Task AnEndedContract_IsNotCorrected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var before = await HashAsync(world);

        var result = await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded, DeskWorld.OldestPeriod) with { MonthlyRent = 500_000m }, true);

        Assert.Equal(ContractRefusal.ContractEnded, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>An adjustment prices from the canon, so its history would stop adding up even with an empty ledger.</summary>
    [SkippableFact]
    public async Task AConfirmedAdjustment_StopsACorrection_EvenWithNoMovement()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var seed = world.NewContext();
        var userId = await AccessTestSupport.ProvisionUserAsync(seed, "ajuste-correccion", UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(seed);
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 8_000m, new IndexPeriod(2026, 7), 9_200m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, userId);
        seed.ContractAccounts.Add(new ContractAccount(Guid.NewGuid(), contract.Id));
        await seed.SaveChangesAsync();
        var seeded = new Seeded(contract.Id, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), contract.Units.First().UnitId);
        var before = await HashAsync(world);

        var result = await Registry(world).CorrectAsync(
            contract.Id,
            Standing(seeded, contract.StartDate) with { NominalEndDate = contract.NominalEndDate, MonthlyRent = 1_000m, Shares = contract.Units.Select(u => new UnitShare(u.UnitId, u.SharePercentage)).ToList() },
            true);

        Assert.Equal(ContractRefusal.AdjustmentConfirmed, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Units (Finding F1, design D14) -------------------------------------------------------------------------------

    /// <summary>Neither role can DELETE a share, which is why replacing one is a raw UPDATE. It must run as a real role, and leave one row.</summary>
    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task OneOfTwoUnits_IsReplaced_AndTheSharesCorrected_AsARealRole(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var second = await UnitAsync(world, "41");
        var third = await UnitAsync(world, "42");
        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded), false));
        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(
                connection,
                """
                BEGIN;
                UPDATE contract_units SET share_percentage = 60 WHERE contract_id = @c;
                INSERT INTO contract_units (contract_id, unit_id, share_percentage) VALUES (@c, @u, 40);
                COMMIT;
                """,
                ("c", seeded.Contract),
                ("u", second));
        }

        var (registry, context, _) = await AsAsync(world, role);
        await using (context)
        {
            AssertDone(await registry.CorrectAsync(
                seeded.Contract,
                Standing(seeded) with { Shares = [new UnitShare(seeded.Unit, 75m), new UnitShare(third, 25m)] },
                false));
        }

        Assert.Equal(2, await world.CountAsync("SELECT count(*) FROM contract_units WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_units WHERE contract_id = @c AND unit_id = @u AND share_percentage = 75", ("c", seeded.Contract), ("u", seeded.Unit)));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_units WHERE contract_id = @c AND unit_id = @u AND share_percentage = 25", ("c", seeded.Contract), ("u", third)));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contract_units WHERE contract_id = @c AND unit_id = @u", ("c", seeded.Contract), ("u", second)));
    }

    [SkippableFact]
    public async Task AddingOrDroppingAUnit_IsNotACorrection()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var second = await UnitAsync(world, "41");
        var before = await HashAsync(world);

        var result = await Registry(world).CorrectAsync(
            seeded.Contract,
            Standing(seeded) with { Shares = [new UnitShare(seeded.Unit, 50m), new UnitShare(second, 50m)] },
            true);

        Assert.Equal(ContractRefusal.UnitCountChanged, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableFact]
    public async Task ReplacingWithAUnitAlreadyLet_NamesTheTenant_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var other = await SeedAsync(world);
        var before = await HashAsync(world);

        var result = await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { Shares = [new UnitShare(other.Unit, 100m)] }, true);

        Assert.Equal(ContractRefusal.UnitUnderActiveContract, result.Refusal);
        Assert.Contains("Perez", result.CurrentTenant, StringComparison.Ordinal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>The contract's own unit is under its own contract, which must not refuse a correction that keeps it.</summary>
    [SkippableFact]
    public async Task ACorrection_KeepingItsOwnUnit_IsNotRefusedForIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { MonthlyRent = 500_000m }, false));
    }

    [SkippableFact]
    public async Task ReplacingWithAUnitBeingVacated_Warns_ThenProceeds()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var other = await SeedAsync(world, DeskWorld.OldestPeriod);
        var moveOut = Today.AddDays(30);
        AssertDone(await Registry(world).GiveNoticeAsync(other.Contract, Today, moveOut));
        var correction = Standing(seeded) with { Shares = [new UnitShare(other.Unit, 100m)] };
        var before = await HashAsync(world);

        var warned = await Registry(world).CorrectAsync(seeded.Contract, correction, false);

        Assert.False(warned.Succeeded);
        var warning = Assert.Single(warned.Warnings);
        Assert.Equal(ContractWarningKind.UnitBeingVacated, warning.Kind);
        Assert.Equal(moveOut, warning.Date);
        Assert.Equal(before, await HashAsync(world));

        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, correction, true));
        Assert.Equal(other.Unit, Assert.Single((await Registry(world).DetailAsync(seeded.Contract))!.Units).UnitId);
    }

    /// <summary>A correction is about the terms: a contract with no rate is not warned about it again, since the rate has its own operation.</summary>
    [SkippableFact]
    public async Task ACorrection_DoesNotWarnAboutTheRate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, rate: null);

        var result = await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { MonthlyRent = 500_000m }, false);

        AssertDone(result);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts WHERE honorarios_percentage IS NULL AND monthly_rent = 500000"));
    }

    /// <summary>Moving the start into this month past its due date warns, as creating it there does.</summary>
    [SkippableFact]
    public async Task MovingTheStartPastItsFirstDueDate_Warns()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var month = DeskWorld.FirstOfMonth(Today);
        var clock = new FixedClock(month.AddDays(14));

        var result = await Registry(world, clock).CorrectAsync(seeded.Contract, Standing(seeded, month) with { DueDay = 1 }, false);

        Assert.Equal(ContractWarningKind.FirstDueDatePassed, Assert.Single(result.Warnings).Kind);
    }

    /// <summary>A first due date already passed was accepted when the contract was created, so correcting something else does not raise it again.</summary>
    [SkippableFact]
    public async Task AStartThatDidNotMove_IsNotWarnedAboutAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var month = DeskWorld.FirstOfMonth(Today);
        var seeded = await SeedAsync(world, month, dueDay: 1);
        var clock = new FixedClock(month.AddDays(14));

        var result = await Registry(world, clock).CorrectAsync(seeded.Contract, Standing(seeded, month) with { DueDay = 1, MonthlyRent = 500_000m }, false);

        AssertDone(result);
    }

    // ---- Test 24 ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ACorrectedCanon_IsWhatAccrues()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { MonthlyRent = 500_000m, DueDay = 5 }, false));
        await WriteFirstAccrualAsync(world, seeded);

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(500_000m, await ReciboTestSupport.ScalarAsync<decimal>(connection, "SELECT amount FROM account_movements WHERE kind = 'RentAccrual'"));
        Assert.Equal(NextMonth.AddDays(4), await ReciboTestSupport.ScalarAsync<DateOnly>(connection, "SELECT occurred_on FROM account_movements WHERE kind = 'RentAccrual'"));
    }

    // ---- Two operators ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The canon is guarded by the port alone (design D16), so a correction has to wait for a payment or an accrual already running
    /// on the account, and then see what it wrote. Nothing else stops a correction overwriting the canon of a period just written.
    /// </summary>
    [SkippableFact]
    public async Task ACorrection_WaitsForAWriterOnTheAccount_ThenSeesItsMovement()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using var writer = world.NewContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await new AccountMaterialiser(writer).MaterialiseDueAccrualsThroughAsync(seeded.Account, NextMonth.AddDays(14));

        var correcting = Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { MonthlyRent = 500_000m }, false);
        var finishedFirst = await Task.WhenAny(correcting, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(correcting, finishedFirst);

        await transaction.CommitAsync();
        var result = await correcting;

        Assert.Equal(ContractRefusal.AccountHasMovements, result.Refusal);
        Assert.Equal(450_000m, await ContractColumnAsync<decimal>(world, seeded.Contract, "monthly_rent"));
    }

    /// <summary>
    /// A unit is locked before its availability is read, or two operators both find it free. The foreign key would make this
    /// correction wait anyway, so what proves the lock is that it then sees the other contract and refuses. Found by the independent verifier of S4.
    /// </summary>
    [SkippableFact]
    public async Task ReplacingWithAUnitBeingLetRightNow_WaitsAndThenRefuses()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var other = await SeedAsync(world);
        var unit = await UnitAsync(world);
        await using var letting = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(
            letting,
            "BEGIN; SELECT 1 FROM units WHERE id = @u FOR UPDATE; UPDATE contract_units SET unit_id = @u WHERE contract_id = @c;",
            ("u", unit),
            ("c", other.Contract));

        var correcting = Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { Shares = [new UnitShare(unit, 100m)] }, true);
        var finishedFirst = await Task.WhenAny(correcting, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(correcting, finishedFirst);

        await ReciboTestSupport.ExecAsync(letting, "COMMIT");
        var result = await correcting;

        Assert.Equal(ContractRefusal.UnitUnderActiveContract, result.Refusal);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_units WHERE unit_id = @u", ("u", unit)));
    }

    // ---- Test 25 ------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task AMissingRate_IsEnteredAfterMovements_AndTheDeskThenCollects(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod, rate: null);
        await world.MaterialiseAsync(seeded.Account, Today);
        var refused = await world.NewDesk().PreviewAsync(new CollectionRequest(seeded.Account, null, null, 0m));
        Assert.False(refused.CanCommit);
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            AssertDone(await registry.SetRateAsync(seeded.Contract, 8m));
        }

        Assert.Equal(8m, await ContractColumnAsync<decimal>(world, seeded.Contract, "honorarios_percentage"));
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);
    }

    [SkippableFact]
    public async Task ChangingTheRate_LeavesAnIssuedReciboAsItWas()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);

        async Task<string> RecibosAsync()
        {
            await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
            return await ReciboTestSupport.ScalarAsync<string>(
                connection,
                "SELECT (SELECT string_agg(r::text, '|' ORDER BY r::text) FROM recibos r) || (SELECT string_agg(l::text, '|' ORDER BY l::text) FROM recibo_lines l)");
        }

        var before = await RecibosAsync();

        AssertDone(await Registry(world).SetRateAsync(seeded.Contract, 12.5m));

        Assert.Equal(12.5m, await ContractColumnAsync<decimal>(world, seeded.Contract, "honorarios_percentage"));
        Assert.Equal(before, await RecibosAsync());
    }

    [SkippableTheory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(8.123)]
    public async Task ARateOutOfRange_IsRefused_AndKeepsTheOldOne(double rate)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        var result = await Registry(world).SetRateAsync(seeded.Contract, (decimal)rate);

        Assert.Equal(ContractRefusal.HonorariosOutOfRange, result.Refusal);
        Assert.Equal(8m, await ContractColumnAsync<decimal>(world, seeded.Contract, "honorarios_percentage"));
    }

    [SkippableFact]
    public async Task TheRate_CanBeZero_AndChangedOnAnEndedContract()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));

        AssertDone(await Registry(world).SetRateAsync(seeded.Contract, 0m));

        Assert.Equal(0m, await ContractColumnAsync<decimal>(world, seeded.Contract, "honorarios_percentage"));
    }

    // ---- Test 26 and Test 80: who is on the contract ---------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task ACodeudor_IsReplacedAfterMovements_AndTheTenantIsNot(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await WriteFirstAccrualAsync(world, seeded);
        var first = await PersonAsync(world, "Raúl", "Vera");
        var second = await PersonAsync(world, "Lía", "Sosa");
        var replacement = await PersonAsync(world, "Ana", "Ruiz");
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            AssertDone(await registry.SetCodebtorsAsync(seeded.Contract, [first]));
            AssertDone(await registry.SetCodebtorsAsync(seeded.Contract, [second]));

            var refused = await registry.CorrectAsync(seeded.Contract, Standing(seeded) with { TenantId = replacement }, true);
            Assert.Equal(ContractRefusal.AccountHasMovements, refused.Refusal);
        }

        var detail = (await Registry(world).DetailAsync(seeded.Contract))!;
        Assert.Equal(second, detail.Parties.Single(p => p.Role == PartyRole.Codebtor).PartyId);
        Assert.Equal(seeded.Tenant, detail.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId);
    }

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task ACodeudorRemoved_LeavesATrace_AndNoRoleHoldsDelete(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await WriteFirstAccrualAsync(world, seeded);
        var codeudor = await PersonAsync(world);
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        var (registry, context, login) = await AsAsync(world, role);

        await using (context)
        {
            AssertDone(await registry.SetCodebtorsAsync(seeded.Contract, []));
        }

        Assert.DoesNotContain((await Registry(world).DetailAsync(seeded.Contract))!.Parties, p => p.Role == PartyRole.Codebtor);
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(1L, await ReciboTestSupport.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM contract_parties WHERE contract_id = @c AND party_id = @p AND role = 'Codebtor' AND removed_at IS NOT NULL",
            ("c", seeded.Contract),
            ("p", codeudor)));
        Assert.Equal("PartyRemoved", await ReciboTestSupport.ScalarAsync<string>(connection, "SELECT kind FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.Equal(login, await ReciboTestSupport.ScalarAsync<string>(connection, "SELECT recorded_by::text FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.Equal($"Codebtor {codeudor}", await ReciboTestSupport.ScalarAsync<string>(connection, "SELECT detail FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.True(await ReciboTestSupport.ScalarAsync<bool>(connection, "SELECT recorded_at > now() - interval '1 minute' FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));

        foreach (var table in new[] { "contract_parties", "contract_units", "contract_events" })
        {
            foreach (var groupRole in new[] { Admin, Empleado })
            {
                Assert.False(
                    await ReciboTestSupport.ScalarAsync<bool>(connection, $"SELECT has_table_privilege('{groupRole}', '{table}', 'DELETE')"),
                    $"{groupRole} holds DELETE on {table}.");
            }
        }
    }

    /// <summary>The key is (contract, party, role), so bringing someone back must reuse their row, and a later removal leaves a second trace.</summary>
    [SkippableFact]
    public async Task ACodeudorBroughtBack_ReusesTheRow_AndEachRemovalLeavesATrace()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var codeudor = await PersonAsync(world);

        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, []));
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));

        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_parties WHERE party_id = @p", ("p", codeudor)));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_parties WHERE party_id = @p AND removed_at IS NULL", ("p", codeudor)));
        Assert.Equal(codeudor, (await Registry(world).DetailAsync(seeded.Contract))!.Parties.Single(p => p.Role == PartyRole.Codebtor).PartyId);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_events"));

        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, []));

        Assert.Equal(2, await world.CountAsync("SELECT count(*) FROM contract_events"));
    }

    /// <summary>Saying who is already there changes nothing and leaves no trace.</summary>
    [SkippableFact]
    public async Task SettingTheSameCodeudores_WritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var codeudor = await PersonAsync(world);
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        var before = await HashAsync(world);

        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor, codeudor]));

        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableFact]
    public async Task ACodeudorWhoIsNotThere_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var codeudor = await PersonAsync(world);
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        var before = await HashAsync(world);

        var result = await Registry(world).SetCodebtorsAsync(seeded.Contract, [Guid.NewGuid()]);

        Assert.Equal(ContractRefusal.PartyNotFound, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>Design D13: the filter is what keeps a removed person off every existing read, so it is pinned here.</summary>
    [SkippableFact]
    public async Task TheQueryFilter_HidesARemovedRow_FromEveryOrdinaryRead()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var codeudor = await PersonAsync(world);
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, []));

        await using var read = world.NewContext();
        Assert.Empty(await read.ContractParties.Where(p => p.PartyId == codeudor).ToListAsync());
        Assert.Single(await read.ContractParties.IgnoreQueryFilters().Where(p => p.PartyId == codeudor).ToListAsync());
        var contract = await read.Contracts.AsNoTracking().Include(c => c.Parties).SingleAsync(c => c.Id == seeded.Contract);
        Assert.DoesNotContain(contract.Parties, p => p.PartyId == codeudor);
    }

    /// <summary>
    /// The tenant index is not deferrable, so the old row has to be gone before the new one arrives. Bringing someone back is two
    /// updates on one table, which EF orders by key, so the replacement's id is picked to sort on each side of the tenant's: left to
    /// chance, a missing save would be caught only some of the time.
    /// </summary>
    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task ATenant_IsReplaced_AndCanBeBroughtBack_LeavingTraces(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var sortsBefore = await PersonAsync(world, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var sortsAfter = await PersonAsync(world, id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            foreach (var replacement in new[] { sortsBefore, sortsAfter })
            {
                AssertDone(await registry.CorrectAsync(seeded.Contract, Standing(seeded) with { TenantId = replacement }, false));
                AssertDone(await registry.CorrectAsync(seeded.Contract, Standing(seeded), false));
            }
        }

        Assert.Equal(seeded.Tenant, (await Registry(world).DetailAsync(seeded.Contract))!.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId);
        Assert.Equal(3, await world.CountAsync("SELECT count(*) FROM contract_parties WHERE contract_id = @c AND role = 'Tenant'", ("c", seeded.Contract)));
        Assert.Equal(4, await world.CountAsync("SELECT count(*) FROM contract_events WHERE kind = 'PartyRemoved' AND contract_id = @c", ("c", seeded.Contract)));
    }

    [SkippableFact]
    public async Task Lessors_AreReplacedAndAdded_LeavingATraceForWhoLeft()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var first = await PersonAsync(world);
        var second = await PersonAsync(world, "Lía", "Sosa");

        AssertDone(await Registry(world).CorrectAsync(seeded.Contract, Standing(seeded) with { LessorIds = [first, second, first] }, false));

        var lessors = (await Registry(world).DetailAsync(seeded.Contract))!.Parties.Where(p => p.Role == PartyRole.Lessor).Select(p => p.PartyId).ToHashSet();
        Assert.Equal(new HashSet<Guid> { first, second }, lessors);
        Assert.Equal($"Lessor {seeded.Lessor}", await ScalarEventDetailAsync(world));
    }

    private static async Task<string> ScalarEventDetailAsync(DeskWorld world)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        return await ReciboTestSupport.ScalarAsync<string>(connection, "SELECT detail FROM contract_events");
    }

    /// <summary>A failure on the way to committing leaves the correction, its party change and its unit change all unwritten.</summary>
    [SkippableFact]
    public async Task AFailureAtTheLastStep_RollsTheWholeCorrectionBack()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var correction = Standing(seeded) with
        {
            MonthlyRent = 500_000m,
            TenantId = await PersonAsync(world),
            Shares = [new UnitShare(await UnitAsync(world), 100m)],
        };
        await using (var super = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(
                super,
                """
                CREATE FUNCTION probe_fail() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'probe failure'; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER probe_fail AFTER INSERT ON contract_events FOR EACH ROW EXECUTE FUNCTION probe_fail();
                """);
        }

        var before = await HashAsync(world);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Registry(world).CorrectAsync(seeded.Contract, correction, false));

        Assert.Equal("probe failure", Assert.IsType<PostgresException>(failure.GetBaseException()).MessageText);
        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Test 27: notice ----------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task Notice_MovesToPendingTermination_WithItsDates(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        var moveOut = Today.AddDays(30);
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            var result = await registry.GiveNoticeAsync(seeded.Contract, Today, moveOut);

            AssertDone(result);
            Assert.Equal(ContractStatus.PendingTermination, result.Contract!.Status);
        }

        Assert.Equal("PendingTermination", await ContractColumnAsync<string>(world, seeded.Contract, "status"));
        Assert.Equal(Today, await ContractColumnAsync<DateOnly>(world, seeded.Contract, "notice_given_date"));
        Assert.Equal(moveOut, await ContractColumnAsync<DateOnly>(world, seeded.Contract, "planned_move_out_date"));
    }

    [SkippableFact]
    public async Task Notice_InTheFuture_OrWithAMoveOutBeforeIt_OrGivenTwice_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.NoticeAfterToday, (await Registry(world).GiveNoticeAsync(seeded.Contract, Today.AddDays(1), Today.AddDays(2))).Refusal);
        Assert.Equal(ContractRefusal.MoveOutBeforeNotice, (await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today.AddDays(-1))).Refusal);
        Assert.Equal(before, await HashAsync(world));

        AssertDone(await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today));
        var afterNotice = await HashAsync(world);

        Assert.Equal(ContractRefusal.NoticeNeedsActive, (await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today)).Refusal);
        Assert.Equal(afterNotice, await HashAsync(world));
    }

    // ---- Test 28: ending ----------------------------------------------------------------------------------------------

    /// <summary>The summary counts what is due but unwritten, as the desk would, and writes nothing.</summary>
    [SkippableFact]
    public async Task TheEndSummary_ShowsWhatIsOwed_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        var before = await HashAsync(world);

        var summary = await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, Today);

        var due = AccrualSchedule.PeriodsDue(await ContractAsync(world, seeded.Contract), Today).Count;
        Assert.Null(summary.Refusal);
        Assert.True(due >= 3, $"{due} periods due.");
        Assert.Equal(due * 450_000m, summary.LedgerBalance);
        Assert.True(summary.AmountOwed > summary.LedgerBalance, $"{summary.AmountOwed} owed on a ledger of {summary.LedgerBalance}.");
        Assert.Empty(summary.PeriodsAlreadyWritten);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>What the summary promises is what the desk then asks for, and what paying everything adds up to, fee plan included.</summary>
    [SkippableFact]
    public async Task TheEndSummary_IsWhatTheDeskAsksFor_AndWhatPayingItAllComesTo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod, plan: new ContractualFeePlan(5_000m, 2));

        var summary = await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, Today);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));

        var desk = world.NewDesk();
        var first = await desk.PreviewAsync(new CollectionRequest(seeded.Account, null, null, 0m));
        Assert.Equal(summary.AmountOwed, first.PreviousAmountOwed);

        var paid = 0m;
        for (var payment = 0; payment < 8; payment++)
        {
            var next = await desk.PreviewAsync(new CollectionRequest(seeded.Account, null, null, 0m));
            if (next.Period is null || next.Refusal == CollectionRefusal.NothingPayable)
            {
                break;
            }

            paid += next.LedgerFigure;
            var result = await DeskWorld.PayAsync(desk, seeded.Account);
            Assert.True(result.Succeeded, $"{result.Refusal}");
        }

        Assert.Equal(summary.AmountOwed, paid);
    }

    /// <summary>
    /// Voiding an early payment leaves the rent dated on its due date, still ahead. A figure that counts only what is dated up to today
    /// says nothing is owed, and then the desk asks for the rent. Found by the independent verifier of S4.
    /// </summary>
    [SkippableFact]
    public async Task TheEndSummary_CountsARentLeftOwedByAVoidedEarlyPayment()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.FirstOfMonth(Today), dueDay: 31);
        var desk = world.NewDesk(Clock);
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account)).Succeeded);
        var voiding = await desk.PreviewVoidAsync(await world.LatestReciboIdAsync(seeded.Account), "Error de carga", UserRole.Admin);
        Assert.True((await desk.VoidAsync(voiding)).Succeeded);

        var summary = await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, Today);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var asked = await world.NewDesk(Clock).PreviewAsync(new CollectionRequest(seeded.Account, null, null, 0m));

        Assert.Equal(450_000m, summary.LedgerBalance);
        Assert.Equal(asked.PreviousAmountOwed, summary.AmountOwed);
    }

    private static async Task<Contract> ContractAsync(DeskWorld world, Guid id)
    {
        await using var read = world.NewContext();
        return await read.Contracts.AsNoTracking().Include(c => c.Adjustments).SingleAsync(c => c.Id == id);
    }

    /// <summary>Invalid recargo settings close the desk. They must not stop a contract being ended, and no recargo is guessed.</summary>
    [SkippableFact]
    public async Task TheEndSummary_WithoutRecargoTerms_ShowsTheLedgerOnly()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);

        var summary = await new ContractRegistry(world.NewContext(), Clock).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, Today);

        Assert.Null(summary.Refusal);
        Assert.True(summary.LedgerBalance > 0m);
        Assert.Null(summary.AmountOwed);
    }

    [SkippableFact]
    public async Task TheEndSummary_CarriesTheSameRefusalsAsEnding()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);

        (EndReason? Reason, DateOnly Date, ContractRefusal Refusal)[] cases =
        [
            (null, Today, ContractRefusal.EndReasonMissing),
            (EndReason.Expiry, Today.AddDays(1), ContractRefusal.EndAfterToday),
            (EndReason.Expiry, DeskWorld.OldestPeriod.AddDays(-1), ContractRefusal.EndBeforeContractStart),
        ];
        var before = await HashAsync(world);

        foreach (var (reason, date, refusal) in cases)
        {
            Assert.Equal(refusal, (await Registry(world).SummarizeEndAsync(seeded.Contract, reason, date)).Refusal);
            Assert.Equal(refusal, (await Registry(world).EndAsync(seeded.Contract, reason, date)).Refusal);
        }

        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task AContractWithDebt_Ends_AndTheDebtStaysCollectable(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        await world.MaterialiseAsync(seeded.Account, Today);
        var (registry, context, _) = await AsAsync(world, role);

        await using (context)
        {
            var result = await registry.EndAsync(seeded.Contract, EndReason.TerminationForCause, Today);

            AssertDone(result);
            Assert.Equal(ContractStatus.Ended, result.Contract!.Status);
        }

        Assert.Equal("Ended", await ContractColumnAsync<string>(world, seeded.Contract, "status"));
        Assert.Equal(Today, await ContractColumnAsync<DateOnly>(world, seeded.Contract, "actual_end_date"));
        Assert.Equal("TerminationForCause", await ContractColumnAsync<string>(world, seeded.Contract, "end_reason"));
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);
    }

    [SkippableFact]
    public async Task EndingFromPendingTermination_IsAllowed_AndEndingTwiceIsNot()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.OldestPeriod);
        AssertDone(await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today));

        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.ContractEnded, (await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today)).Refusal);
        Assert.Equal(ContractRefusal.ContractEnded, (await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, Today)).Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Test 29 ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task NothingAccrues_AfterAnEndDate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var start = DeskWorld.OldestPeriod;
        var seeded = await SeedAsync(world, start);
        var endDate = start.AddMonths(1).AddDays(14);

        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, endDate));
        await world.MaterialiseAsync(seeded.Account, Today);

        Assert.Equal(2, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual'"));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual' AND occurred_on > @d", ("d", endDate)));
    }

    /// <summary>Spec Requirement 10: a period already written stays on the ledger, and the summary says so.</summary>
    [SkippableFact]
    public async Task APeriodWrittenBeyondTheEndDate_StaysOnTheLedger_AndTheSummaryNamesIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var start = DeskWorld.OldestPeriod;
        var seeded = await SeedAsync(world, start);
        await world.MaterialiseAsync(seeded.Account, start.AddDays(14));
        var endDate = start.AddDays(4);

        var summary = await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.Expiry, endDate);

        // The two later periods are long due today, and ending stops them: the figure counts the written one and no others.
        Assert.Equal([start], summary.PeriodsAlreadyWritten);
        Assert.Equal(450_000m, summary.LedgerBalance);

        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, endDate));
        await world.MaterialiseAsync(seeded.Account, Today);
        Assert.Equal(start, await SingleAccrualPeriodAsync(world));
    }

    private static async Task<DateOnly> SingleAccrualPeriodAsync(DeskWorld world)
    {
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual'"));
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        return await ReciboTestSupport.ScalarAsync<DateOnly>(connection, "SELECT period FROM account_movements WHERE kind = 'RentAccrual'");
    }

    // ---- The guards M2 adds -------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("start_date = start_date + 1")]
    [InlineData("nominal_end_date = nominal_end_date + 1")]
    [InlineData("due_day = 5")]
    public async Task TheTermsLock_RefusesRawSql_OnceTheAccountHoldsAMovement(string set)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using var empleado = await OpenAsAsync(world, Empleado);

        await ReciboTestSupport.ExecAsync(empleado, $"UPDATE contracts SET {set} WHERE id = @id", ("id", seeded.Contract));
        await WriteFirstAccrualAsync(world, seeded);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ReciboTestSupport.ExecAsync(empleado, $"UPDATE contracts SET {set.Replace("= 5", "= 6", StringComparison.Ordinal)} WHERE id = @id", ("id", seeded.Contract)));

        Assert.Equal("P0001", refusal.SqlState);
        Assert.Equal("contracts_terms_locked", refusal.ConstraintName);
    }

    /// <summary>Design D16: a confirmed adjustment legitimately changes the canon, so the trigger leaves it out, and rewriting the same terms passes.</summary>
    [SkippableFact]
    public async Task TheTermsLock_LeavesTheCanonOut_AndIgnoresAnUnchangedValue()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await WriteFirstAccrualAsync(world, seeded);
        await using var empleado = await OpenAsAsync(world, Empleado);

        await ReciboTestSupport.ExecAsync(empleado, "UPDATE contracts SET monthly_rent = 500000 WHERE id = @id", ("id", seeded.Contract));
        await ReciboTestSupport.ExecAsync(empleado, "UPDATE contracts SET start_date = start_date, due_day = due_day WHERE id = @id", ("id", seeded.Contract));

        Assert.Equal(500_000m, await ContractColumnAsync<decimal>(world, seeded.Contract, "monthly_rent"));
    }

    [Fact]
    public void TheDatabasesLocks_ReadAsTheRefusalAnOperatorGets()
    {
        Assert.Equal(ContractRefusal.AccountHasMovements, ContractRegistryErrorMap.For("P0001", "contracts_terms_locked"));
        Assert.Equal(ContractRefusal.AccountHasMovements, ContractRegistryErrorMap.For("P0001", "contracts_fee_plan_locked"));
        Assert.Null(ContractRegistryErrorMap.For("P0001", "something_else"));
    }

    [SkippableTheory]
    [InlineData("UPDATE contract_events SET detail = 'x'")]
    [InlineData("DELETE FROM contract_events")]
    public async Task ContractEvents_AreAppendOnly_EvenForTheOwner(string sql)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var codeudor = await PersonAsync(world);
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, [codeudor]));
        AssertDone(await Registry(world).SetCodebtorsAsync(seeded.Contract, []));
        await using var super = await ReciboTestSupport.OpenAsync(world.Database.Context);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ReciboTestSupport.ExecAsync(super, sql));

        Assert.Equal("P0001", refusal.SqlState);
        Assert.Equal("contract_events_append_only", refusal.ConstraintName);
    }

    [SkippableTheory]
    [InlineData(Admin, "UPDATE contract_events SET detail = 'x'")]
    [InlineData(Admin, "DELETE FROM contract_events")]
    [InlineData(Empleado, "UPDATE contract_events SET detail = 'x'")]
    [InlineData(Empleado, "DELETE FROM contract_events")]
    public async Task NoRole_MayUpdateOrDeleteAContractEvent(string role, string sql)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var connection = await OpenAsAsync(world, role);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ReciboTestSupport.ExecAsync(connection, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);
    }

    /// <summary>Who and when belong to the database: what an INSERT supplies is overwritten, whoever sends it.</summary>
    [SkippableFact]
    public async Task TheStamp_OverwritesWhatAnInsertSupplies()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using var empleado = await OpenAsAsync(world, Empleado);
        var login = await ReciboTestSupport.ScalarAsync<string>(empleado, "SELECT current_user::text");

        await ReciboTestSupport.ExecAsync(
            empleado,
            "INSERT INTO contract_events (id, contract_id, kind, recorded_by, recorded_at) VALUES (@id, @c, 'PartyRemoved', 'somebody_else', '2001-01-01')",
            ("id", Guid.NewGuid()),
            ("c", seeded.Contract));

        await using var super = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(login, await ReciboTestSupport.ScalarAsync<string>(super, "SELECT recorded_by::text FROM contract_events"));
        Assert.True(await ReciboTestSupport.ScalarAsync<bool>(super, "SELECT recorded_at > now() - interval '1 minute' FROM contract_events"));
    }

    /// <summary>A CHECK passes on NULL, so a missing reason has to be refused explicitly, not only a blank one.</summary>
    [SkippableTheory]
    [InlineData("Cancelled", null)]
    [InlineData("NoticeUndone", "   ")]
    [InlineData("EndUndone", "\t\n")]
    public async Task AnEventThatNeedsAReason_IsRefusedWithoutOne(string kind, string? reason)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using var super = await ReciboTestSupport.OpenAsync(world.Database.Context);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ReciboTestSupport.ExecAsync(
            super,
            "INSERT INTO contract_events (id, contract_id, kind, reason, recorded_by, recorded_at) VALUES (@id, @c, @k, @r, 'x', now())",
            ("id", Guid.NewGuid()),
            ("c", seeded.Contract),
            ("k", kind),
            ("r", reason)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_contract_events_reason_not_blank", refusal.ConstraintName);
    }
}
