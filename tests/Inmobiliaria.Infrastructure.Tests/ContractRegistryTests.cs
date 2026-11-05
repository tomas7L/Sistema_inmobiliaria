using System.Data.Common;
using System.Text.RegularExpressions;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 10 to 22 (Requirements 6 to 8) against real Postgres, as real Admin and Empleado logins,
/// with effects read back by raw SQL. Each test has its own database, because counts and lists answer over every row. All
/// people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractRegistryTests
{
    private const string Admin = "inmobiliaria_admin";
    private const string Empleado = "inmobiliaria_empleado";

    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly FixedClock Clock = new(Today);
    private static readonly Regex InsertAccounts = new(@"INSERT INTO ""?contract_accounts""?\s", RegexOptions.Compiled);

    private readonly PostgresFixture _fixture;

    public ContractRegistryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static ContractRegistry Registry(DeskWorld world, IAgencyClock? clock = null) => new(world.NewContext(), clock ?? Clock);

    /// <summary>A registry on a context logged in as a real member of <paramref name="groupRole"/>.</summary>
    private static async Task<(ContractRegistry Registry, InmobiliariaDbContext Context)> RegistryAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!)
        {
            Username = role,
            Password = password,
        };
        var context = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);

        return (new ContractRegistry(context, Clock), context);
    }

    private sealed record Cast(Guid Tenant, Guid Lessor, Guid Unit);

    private static async Task<Guid> PartyAsync(DeskWorld world, string first, string last)
    {
        await using var seed = world.NewContext();
        var party = new Party(Guid.NewGuid(), first, last);
        seed.Parties.Add(party);
        await seed.SaveChangesAsync();
        return party.Id;
    }

    private static async Task<Guid> UnitAsync(DeskWorld world, string street = "Alem", string number = "1258")
    {
        await using var seed = world.NewContext();
        var unit = new PropertyUnit(Guid.NewGuid(), new Address(street, number, "Sunchales", "Santa Fe", "2322"));
        seed.Units.Add(unit);
        await seed.SaveChangesAsync();
        return unit.Id;
    }

    private static async Task<Cast> CastAsync(DeskWorld world) =>
        new(await PartyAsync(world, "Juan", "Pérez"), await PartyAsync(world, "Ana", "Gómez"), await UnitAsync(world));

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

    private static async Task<ContractListing> SavedAsync(Task<ContractResult> call)
    {
        var result = await call;
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}; warnings: {string.Join(", ", result.Warnings.Select(w => w.Kind))}.");
        return result.Contract!;
    }

    private static async Task AssertNothingWrittenAsync(DeskWorld world)
    {
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contracts"));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contract_parties"));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contract_units"));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM contract_accounts"));
    }

    private static async Task<object?> ColumnAsync(DeskWorld world, Guid contractId, string column)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM contracts WHERE id = @id";
        command.Parameters.AddWithValue("id", contractId);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>Spec Test 11: a complete creation, as an Empleado, writes everything and reaches the desk.</summary>
    [SkippableFact]
    public async Task ACompleteCreation_WritesEverything_AndReachesTheDesk()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var (registry, context) = await RegistryAsAsync(world, Empleado);
        await using (context)
        {
            var created = await SavedAsync(registry.CreateAsync(Draft(cast), false));

            Assert.Equal(ContractStatus.Active, created.Status);
            Assert.Equal("Active", await ColumnAsync(world, created.Id, "status"));
            Assert.Equal(10, Convert.ToInt32(await ColumnAsync(world, created.Id, "due_day")));
            Assert.Equal(8m, await ColumnAsync(world, created.Id, "honorarios_percentage"));
            Assert.Null(await ColumnAsync(world, created.Id, "cutover_date"));
            Assert.Equal(2, await world.CountAsync("SELECT count(*) FROM contract_parties"));
            Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_units WHERE share_percentage = 100"));
            Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_accounts"));
            Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements"));

            await using var deskContext = world.NewContext();
            var found = Assert.Single(await new AccountDirectory(deskContext, Clock).SearchAsync("Pérez"));
            Assert.Equal(created.Id, found.ContractId);
            Assert.Equal("Gómez, Ana", found.Lessors);
        }
    }

    /// <summary>Spec Test 12: no tenant, no lessor or no unit: each refused by its name, with no row left.</summary>
    [SkippableTheory]
    [InlineData("tenant", ContractRefusal.TenantMissing)]
    [InlineData("lessor", ContractRefusal.LessorMissing)]
    [InlineData("unit", ContractRefusal.UnitMissing)]
    public async Task AMissingPart_IsRefused_AndWritesNothing(string missing, ContractRefusal expected)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world));
        draft = missing switch
        {
            "tenant" => draft with { TenantId = null },
            "lessor" => draft with { LessorIds = [] },
            _ => draft with { Shares = [] },
        };

        var result = await Registry(world).CreateAsync(draft, true);

        Assert.Equal(expected, result.Refusal);
        await AssertNothingWrittenAsync(world);
    }

    /// <summary>Spec Test 13: a forced failure in account creation leaves no contract row.</summary>
    [SkippableFact]
    public async Task AFailureCreatingTheAccount_LeavesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var probe = new FailureProbe();
        await using var context = world.NewContext(CommandHook.FailOnNth(InsertAccounts, 1, probe));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => new ContractRegistry(context, Clock).CreateAsync(Draft(cast), false));

        Assert.True(probe.Fired);
        Assert.True(InjectedFailureException.IsIn(failure));
        await AssertNothingWrittenAsync(world);
    }

    /// <summary>The result is read before committing: if it cannot be read, nothing was written, so a failure never hides a saved contract.</summary>
    [SkippableFact]
    public async Task AFailureReadingTheResult_LeavesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        await using var context = world.NewContext(new FailFirstReadAfterSaving());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new ContractRegistry(context, Clock).CreateAsync(Draft(cast), false));

        await AssertNothingWrittenAsync(world);
    }

    /// <summary>Spec Test 15: a start not on the 1st is refused by the port, saying a contract takes effect on the 1st.</summary>
    [SkippableFact]
    public async Task AStartOnThe15th_IsRefusedByThePort()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2026, 12, 15) };

        var result = await Registry(world).CreateAsync(draft, true);

        Assert.Equal(ContractRefusal.StartNotOnTheFirst, result.Refusal);
        await AssertNothingWrittenAsync(world);
    }

    /// <summary>Spec Test 16: a contract signed a few days into its month is accepted with no warning.</summary>
    [SkippableFact]
    public async Task AStartInTheCurrentMonth_BeforeItsDueDate_HasNoWarning()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2026, 11, 1) };

        await SavedAsync(Registry(world, new FixedClock(new DateOnly(2026, 11, 5))).CreateAsync(draft, false));

        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    /// <summary>Spec Test 16: past its first due date it warns, writes nothing until accepted, and then proceeds.</summary>
    [SkippableFact]
    public async Task AFirstDueDateAlreadyPassed_WarnsAndCanProceed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2026, 11, 1) };

        var warned = await Registry(world).CreateAsync(draft, false);

        Assert.False(warned.Succeeded);
        Assert.Null(warned.Refusal);
        Assert.Contains("noviembre", Assert.Single(warned.Warnings).OperatorText());
        await AssertNothingWrittenAsync(world);

        await SavedAsync(Registry(world).CreateAsync(draft, true));

        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    /// <summary>Spec Test 17 at the port: a canon with cents and shares off 100% write nothing; a split stores as typed.</summary>
    [SkippableFact]
    public async Task TheCanonAndTheShares_AreCheckedBeforeWriting()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var second = await UnitAsync(world, "Roca", "40");

        Assert.Equal(ContractRefusal.RentNotWholePositive, (await Registry(world).CreateAsync(Draft(cast) with { MonthlyRent = 100.5m }, true)).Refusal);
        Assert.Equal(
            ContractRefusal.SharesNotHundred,
            (await Registry(world).CreateAsync(Draft(cast) with { Shares = [new UnitShare(cast.Unit, 60m), new UnitShare(second, 30m)] }, true)).Refusal);
        await AssertNothingWrittenAsync(world);

        var shares = RentSplit.FromAmounts(300_000m, [(cast.Unit, 100_000m), (second, 200_000m)])!;
        await SavedAsync(Registry(world).CreateAsync(Draft(cast) with { MonthlyRent = 300_000m, Shares = shares }, false));

        Assert.Equal(100, await world.CountAsync("SELECT sum(share_percentage)::bigint FROM contract_units"));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_units WHERE share_percentage = 66.666667"));
    }

    /// <summary>Spec Test 18: a unit under an Active contract is refused naming the tenant, and accepting warnings does not get past it.</summary>
    [SkippableFact]
    public async Task AUnitUnderAnActiveContract_IsRefusedNamingTheTenant()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        await SavedAsync(Registry(world).CreateAsync(Draft(cast), false));
        var successor = await PartyAsync(world, "Lía", "Sosa");

        var result = await Registry(world).CreateAsync(Draft(cast) with { TenantId = successor }, true);

        Assert.Equal(ContractRefusal.UnitUnderActiveContract, result.Refusal);
        Assert.Contains("Juan Pérez", result.Refusal!.Value.OperatorText(result.CurrentTenant));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    /// <summary>Spec Test 18: under PendingTermination it warns with the date, and the operator can proceed.</summary>
    [SkippableFact]
    public async Task AUnitBeingVacated_WarnsWithTheDate_AndCanProceed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var first = await SavedAsync(Registry(world).CreateAsync(Draft(cast), false));
        await using (var change = world.NewContext())
        {
            (await change.Contracts.SingleAsync(c => c.Id == first.Id)).GiveNotice(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31));
            await change.SaveChangesAsync();
        }

        var draft = Draft(cast) with { TenantId = await PartyAsync(world, "Lía", "Sosa"), StartDate = new DateOnly(2027, 1, 1) };

        var warned = await Registry(world).CreateAsync(draft, false);

        var warning = Assert.Single(warned.Warnings);
        Assert.Equal(ContractWarningKind.UnitBeingVacated, warning.Kind);
        Assert.Contains("31/12/2026", warning.OperatorText());
        Assert.Contains("Alem 1258", warning.OperatorText());
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts"));

        await SavedAsync(Registry(world).CreateAsync(draft, true));

        Assert.Equal(2, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    /// <summary>
    /// Availability is checked inside the transaction with the unit locked (design §4): a contract committed while this
    /// creation waits for the lock is seen, so two operators cannot both let the same unit (spec Decision 30).
    /// </summary>
    [SkippableFact]
    public async Task TwoOperatorsOnOneUnit_TheSecondIsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var contractId = Guid.NewGuid();
        await using var first = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(first, "BEGIN");
        await ReciboTestSupport.ExecAsync(first, "SELECT 1 FROM units WHERE id = @id FOR UPDATE", ("id", cast.Unit));
        await ReciboTestSupport.ExecAsync(
            first,
            "INSERT INTO contracts (id, start_date, nominal_end_date, monthly_rent, status, due_day) VALUES (@id, DATE '2026-12-01', DATE '2028-11-30', 100000, 'Active', 10)",
            ("id", contractId));
        await ReciboTestSupport.ExecAsync(
            first,
            "INSERT INTO contract_units (contract_id, unit_id, share_percentage) VALUES (@contract, @unit, 100)",
            ("contract", contractId), ("unit", cast.Unit));

        var second = Registry(world).CreateAsync(Draft(cast) with { TenantId = await PartyAsync(world, "Lía", "Sosa") }, true);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(second.IsCompleted);
        await ReciboTestSupport.ExecAsync(first, "COMMIT");

        Assert.Equal(ContractRefusal.UnitUnderActiveContract, (await second).Refusal);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts"));
    }

    /// <summary>Spec Test 19: the rate of the lessor's contract with the latest start that carries one.</summary>
    [SkippableFact]
    public async Task TheRateSuggestion_ComesFromTheLessorsMostRecentContractCarryingOne()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var lessor = await PartyAsync(world, "Ana", "Gómez");
        var other = await PartyAsync(world, "Otro", "Dueño");

        async Task SeedAsync(Guid owner, DateOnly start, decimal? rate)
        {
            await using var seed = world.NewContext();
            var ownerParty = await seed.Parties.SingleAsync(p => p.Id == owner);
            var unit = new PropertyUnit(Guid.NewGuid(), new Address("Calle", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
            var contract = new Contract(Guid.NewGuid(), start, start.AddYears(2), 100_000m, [new UnitShare(unit.Id, 100m)], rate);
            contract.AssignParty(ownerParty, PartyRole.Lessor);
            seed.Units.Add(unit);
            seed.Contracts.Add(contract);
            await seed.SaveChangesAsync();
        }

        Assert.Null(await Registry(world).SuggestRateAsync(lessor));

        await SeedAsync(lessor, new DateOnly(2023, 1, 1), 9m);
        await SeedAsync(lessor, new DateOnly(2025, 1, 1), 6m);
        await SeedAsync(lessor, new DateOnly(2026, 1, 1), null);
        await SeedAsync(other, new DateOnly(2026, 6, 1), 3m);

        Assert.Equal(6m, await Registry(world).SuggestRateAsync(lessor));
        Assert.Equal(3m, await Registry(world).SuggestRateAsync(other));
    }

    /// <summary>Spec Test 19: an empty rate warns and proceeds, stored as null; 0% has no warning and is stored as 0.</summary>
    [SkippableFact]
    public async Task EmptyAndZeroRates_StayDifferent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var empty = Draft(cast) with { HonorariosPercentage = null };

        var warned = await Registry(world).CreateAsync(empty, false);

        Assert.Equal(ContractWarningKind.NoHonorarios, Assert.Single(warned.Warnings).Kind);
        var withoutRate = await SavedAsync(Registry(world).CreateAsync(empty, true));
        Assert.Null(await ColumnAsync(world, withoutRate.Id, "honorarios_percentage"));

        var zero = Draft(new Cast(cast.Tenant, cast.Lessor, await UnitAsync(world, "Roca", "40"))) with { HonorariosPercentage = 0m };
        var withZero = await SavedAsync(Registry(world).CreateAsync(zero, false));
        Assert.Equal(0m, await ColumnAsync(world, withZero.Id, "honorarios_percentage"));
    }

    /// <summary>Spec Test 20: an Admin loads a contract signed two years ago, and reading its account writes nothing up to the cutover.</summary>
    [SkippableFact]
    public async Task AnAdminLoadsAPreExistingContract_AndItsHistoryNeverAccrues()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var draft = Draft(cast) with
        {
            StartDate = new DateOnly(2024, 11, 1),
            NominalEndDate = new DateOnly(2027, 10, 31),
            CutoverDate = Today,
        };
        var (registry, context) = await RegistryAsAsync(world, Admin);
        await using (context)
        {
            var created = await SavedAsync(registry.CreateAsync(draft, false));
            Assert.Equal(Today, await ColumnAsync(world, created.Id, "cutover_date") is DateOnly stored ? stored : default);

            await using var reading = world.NewContext();
            var account = await reading.ContractAccounts.AsNoTracking().SingleAsync(a => a.ContractId == created.Id);

            Assert.Equal(0, await new AccountMaterialiser(reading).MaterialiseDueAccrualsThroughAsync(account.Id, Today));
            Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements"));

            Assert.Equal(1, await new AccountMaterialiser(reading).MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 12, 15)));
            Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM account_movements WHERE period = DATE '2026-12-01'"));
        }
    }

    /// <summary>Spec Test 21: the cutover's bounds are refused at the port and write nothing.</summary>
    [SkippableTheory]
    [InlineData(2026, 11, 16, ContractRefusal.CutoverAfterToday)]
    [InlineData(2024, 10, 31, ContractRefusal.CutoverBeforeStart)]
    public async Task TheCutoversBounds_AreRefused(int year, int month, int day, ContractRefusal expected)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2024, 11, 1), CutoverDate = new DateOnly(year, month, day) };

        var result = await Registry(world).CreateAsync(draft, true);

        Assert.Equal(expected, result.Refusal);
        await AssertNothingWrittenAsync(world);
    }

    /// <summary>Spec Test 22: an Empleado with a start before the current month is stopped, saying an Admin has to load it.</summary>
    [SkippableFact]
    public async Task AnEmpleado_WithAnOldStart_IsStopped()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2026, 9, 1) };
        var (registry, context) = await RegistryAsAsync(world, Empleado);
        await using (context)
        {
            var result = await registry.CreateAsync(draft, true);

            Assert.Equal(ContractRefusal.StartBeforeCurrentMonth, result.Refusal);
            Assert.Contains("Admin", result.Refusal!.Value.OperatorText());
            await AssertNothingWrittenAsync(world);
        }
    }

    /// <summary>Spec R21: the form is not what decides. An Empleado who reaches the port with a date is refused by the database, as a value.</summary>
    [SkippableFact]
    public async Task AnEmpleado_WithACutoverDate_IsRefusedByTheDatabase_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var draft = Draft(await CastAsync(world)) with { StartDate = new DateOnly(2024, 11, 1), CutoverDate = Today };
        var (registry, context) = await RegistryAsAsync(world, Empleado);
        await using (context)
        {
            var result = await registry.CreateAsync(draft, true);

            Assert.Equal(ContractRefusal.CutoverAdminOnly, result.Refusal);
            await AssertNothingWrittenAsync(world);
        }
    }

    /// <summary>A person, a unit or an index that is gone is refused by name instead of failing at a foreign key.</summary>
    [SkippableFact]
    public async Task SomethingThatIsGone_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);

        Assert.Equal(ContractRefusal.PartyNotFound, (await Registry(world).CreateAsync(Draft(cast) with { TenantId = Guid.NewGuid() }, true)).Refusal);
        Assert.Equal(ContractRefusal.PartyNotFound, (await Registry(world).CreateAsync(Draft(cast) with { CodebtorIds = [Guid.NewGuid()] }, true)).Refusal);
        Assert.Equal(ContractRefusal.UnitNotFound, (await Registry(world).CreateAsync(Draft(cast) with { Shares = [new UnitShare(Guid.NewGuid(), 100m)] }, true)).Refusal);
        Assert.Equal(ContractRefusal.IndexNotFound, (await Registry(world).CreateAsync(Draft(cast) with { Clause = new ClauseDraft([Guid.NewGuid()], 6) }, true)).Refusal);
        await AssertNothingWrittenAsync(world);
    }

    /// <summary>Spec Test 10: past its end a contract reads Active and is flagged; before it, and once ended, it is not flagged.</summary>
    [SkippableFact]
    public async Task AContractPastItsEnd_ReadsActive_AndIsFlagged()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var created = await SavedAsync(Registry(world).CreateAsync(Draft(await CastAsync(world)), false));
        var afterEnd = new FixedClock(new DateOnly(2028, 12, 1));

        var onItsLastDay = Assert.Single(await Registry(world, new FixedClock(new DateOnly(2028, 11, 30))).ListAsync(null, null));
        var past = Assert.Single(await Registry(world, afterEnd).ListAsync(null, null));

        Assert.False(onItsLastDay.IsPastEnd);
        Assert.True(past.IsPastEnd);
        Assert.Equal(ContractStatus.Active, past.Status);
        Assert.True((await Registry(world, afterEnd).DetailAsync(created.Id))!.IsPastEnd);

        await using (var change = world.NewContext())
        {
            (await change.Contracts.SingleAsync(c => c.Id == created.Id)).End(EndReason.Expiry, new DateOnly(2028, 11, 30));
            await change.SaveChangesAsync();
        }

        Assert.False(Assert.Single(await Registry(world, afterEnd).ListAsync(null, null)).IsPastEnd);
    }

    /// <summary>Spec Requirement 6: found by tenant, lessor, unit and state, as both roles.</summary>
    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task TheList_FiltersByTenantLessorUnitAndState(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var one = await SavedAsync(Registry(world).CreateAsync(Draft(await CastAsync(world)), false));
        var cast = new Cast(await PartyAsync(world, "Lía", "Sosa"), await PartyAsync(world, "Raúl", "Vera"), await UnitAsync(world, "Roca", "40"));
        var two = await SavedAsync(Registry(world).CreateAsync(Draft(cast), false));
        await using (var change = world.NewContext())
        {
            (await change.Contracts.SingleAsync(c => c.Id == two.Id)).End(EndReason.Expiry, Today);
            await change.SaveChangesAsync();
        }

        var (registry, context) = await RegistryAsAsync(world, groupRole);
        await using (context)
        {
            Assert.Equal(2, (await registry.ListAsync("  ", null)).Count);
            Assert.Equal(one.Id, Assert.Single(await registry.ListAsync("perez", null)).Id);
            Assert.Equal(two.Id, Assert.Single(await registry.ListAsync("vera", null)).Id);
            Assert.Equal(two.Id, Assert.Single(await registry.ListAsync("roca", null)).Id);
            Assert.Equal(one.Id, Assert.Single(await registry.ListAsync(null, ContractStatus.Active)).Id);
            Assert.Equal(two.Id, Assert.Single(await registry.ListAsync(null, ContractStatus.Ended)).Id);
            Assert.Empty(await registry.ListAsync("perez", ContractStatus.Ended));
        }
    }

    /// <summary>Spec Requirement 6: the detail shows everything, written in one operation with its clause.</summary>
    [SkippableFact]
    public async Task TheDetail_ShowsEverythingCreated()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cast = await CastAsync(world);
        var codebtor = await PartyAsync(world, "Raúl", "Vera");
        var ipc = new EconomicIndex(Guid.NewGuid(), "IPC-" + Guid.NewGuid().ToString("N")[..6]);
        var icl = new EconomicIndex(Guid.NewGuid(), "ICL-" + Guid.NewGuid().ToString("N")[..6]);
        await using (var seed = world.NewContext())
        {
            seed.EconomicIndices.AddRange(ipc, icl);
            await seed.SaveChangesAsync();
        }

        var draft = Draft(cast) with
        {
            CodebtorIds = [codebtor],
            DueDay = 5,
            FeePlanAmount = 120_000m,
            FeePlanInstalments = 3,
            Clause = new ClauseDraft([icl.Id, ipc.Id], 6),
        };

        var created = await SavedAsync(Registry(world).CreateAsync(draft, false));
        var detail = (await Registry(world).DetailAsync(created.Id))!;

        Assert.Equal(new DateOnly(2026, 12, 1), detail.StartDate);
        Assert.Equal(new DateOnly(2028, 11, 30), detail.NominalEndDate);
        Assert.Equal(ContractStatus.Active, detail.Status);
        Assert.Equal(450_000m, detail.MonthlyRent);
        Assert.Equal(5, detail.DueDay);
        Assert.Equal(8m, detail.HonorariosPercentage);
        Assert.Equal(new ContractualFeePlan(120_000m, 3), detail.FeePlan);
        Assert.Equal(PartyRole.Tenant, detail.Parties.Single(p => p.PartyId == cast.Tenant).Role);
        Assert.Equal("Gómez, Ana", detail.Parties.Single(p => p.Role == PartyRole.Lessor).Name);
        Assert.Equal(codebtor, detail.Parties.Single(p => p.Role == PartyRole.Codebtor).PartyId);
        var unit = Assert.Single(detail.Units);
        Assert.Equal(100m, unit.Percentage);
        Assert.Contains("Alem 1258", unit.Address);
        Assert.Equal([icl.Name, ipc.Name], detail.Clause!.IndexNames);
        Assert.Equal(6, detail.Clause.IntervalMonths);
        Assert.Empty(detail.Adjustments);
    }

    private sealed class FailFirstReadAfterSaving : DbCommandInterceptor
    {
        private bool _saved;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_saved && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The result could not be read.");
            }

            _saved |= InsertAccounts.IsMatch(command.CommandText);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Spec Requirement 6: the adjustment history, in order, and null for a contract that is not there.</summary>
    [SkippableFact]
    public async Task TheDetail_ShowsTheAdjustmentHistory()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var seed = world.NewContext();
        var userId = await AccessTestSupport.ProvisionUserAsync(seed, "detalle-ajuste", UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(seed);
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 8_000m, new IndexPeriod(2026, 7), 9_200m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, userId);
        await seed.SaveChangesAsync();

        var detail = (await Registry(world).DetailAsync(contract.Id))!;

        var line = Assert.Single(detail.Adjustments);
        Assert.Equal(new DateOnly(2026, 7, 1), line.EffectiveDate);
        Assert.Equal(450_000m, line.PreviousCanon);
        Assert.Equal(detail.MonthlyRent, line.NewCanon);
        Assert.Equal(AdjustmentKind.Regular, line.Kind);
        Assert.Null(await Registry(world).DetailAsync(Guid.NewGuid()));
    }
}
