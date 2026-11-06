using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
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

/// <summary>
/// gestion-de-alquileres spec Tests 72 to 79, 89 and 90 (Requirements 10, 23, 24) against real Postgres, as real Admin and Empleado
/// logins, with effects read back by raw SQL. All people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractCancellationRegistryTests
{
    private const string Admin = "inmobiliaria_admin";
    private const string Empleado = "inmobiliaria_empleado";
    private const string Reason = "Inquilino equivocado";

    private static readonly DateOnly Today = DeskWorld.Today;
    private static readonly FixedClock Clock = new(Today);
    private static readonly DateOnly Oldest = DeskWorld.OldestPeriod;

    private readonly PostgresFixture _fixture;

    public ContractCancellationRegistryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seeded(Guid Contract, Guid Account, Guid Tenant, Guid Unit);

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static ContractRegistry Registry(DeskWorld world, IAgencyClock? clock = null) => new(world.NewContext(), clock ?? Clock, DeskWorld.Terms);

    private static async Task<(ContractRegistry Registry, InmobiliariaDbContext Context, string Login)> AsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!) { Username = role, Password = password };
        var context = new InmobiliariaDbContext(new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);

        return (new ContractRegistry(context, Clock, DeskWorld.Terms), context, role);
    }

    private static async Task<Seeded> SeedAsync(DeskWorld world, DateOnly? start = null)
    {
        var accountId = await world.SeedAccountAsync(start ?? Oldest);
        await using var read = world.NewContext();
        var account = await read.ContractAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        var contract = await read.Contracts.AsNoTracking().Include(c => c.Parties).Include(c => c.Units).SingleAsync(c => c.Id == account.ContractId);

        return new Seeded(contract.Id, accountId, contract.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId, contract.Units.Single().UnitId);
    }

    private static async Task<ContractResult> CancelAsync(DeskWorld world, Guid contract, string? reason = Reason, IAgencyClock? clock = null)
    {
        var summary = await Registry(world, clock).SummarizeCancelAsync(contract, reason);
        return await Registry(world, clock).CancelAsync(contract, reason, summary);
    }

    private static void AssertDone(ContractResult result) =>
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}; warnings: {string.Join(", ", result.Warnings.Select(w => w.Kind))}.");

    private static async Task<string> HashAsync(DeskWorld world, string[]? tables = null)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var parts = new List<string>();
        foreach (var table in tables ?? ["contracts", "contract_parties", "contract_units", "contract_events", "account_movements", "collections", "recibos", "recibo_lines", "recibo_annulments"])
        {
            parts.Add(table + "=" + await ReciboTestSupport.ScalarAsync<string>(
                connection, $"SELECT coalesce(string_agg(t::text, '|' ORDER BY t::text), '') FROM {table} t"));
        }

        return string.Join(";", parts);
    }

    private static async Task<T> ScalarAsync<T>(DeskWorld world, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        return await ReciboTestSupport.ScalarAsync<T>(connection, sql, parameters);
    }

    private static Task<decimal> BalanceAsync(DeskWorld world, Guid account) =>
        ScalarAsync<decimal>(world, "SELECT coalesce(sum(amount), 0) FROM account_movements WHERE account_id = @a", ("a", account));

    private static Task<CollectionPreview> AskDeskAsync(DeskWorld world, Guid account) =>
        world.NewDesk().PreviewAsync(new CollectionRequest(account, null, null, 0m));

    // ---- Test 72 ------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task Cancelling_LeavesBalanceZero_EveryMovementWithItsCorrection_AndWhoAndWhy(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var before = await HashAsync(world, ["account_movements"]);
        var accruals = await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual'");
        Assert.True(accruals >= 3, $"{accruals} accruals.");
        var (registry, context, login) = await AsAsync(world, role);

        await using (context)
        {
            var summary = await registry.SummarizeCancelAsync(seeded.Contract, Reason);
            var result = await registry.CancelAsync(seeded.Contract, Reason, summary);

            AssertDone(result);
            Assert.Equal(ContractStatus.Ended, result.Contract!.Status);
            Assert.True(result.Contract.IsCancelled);
        }

        // Nothing deleted, nothing edited: every earlier row is still there as it was, each accrual named by one correction.
        var afterRows = await ScalarAsync<string>(world, "SELECT coalesce(string_agg(t::text, '|' ORDER BY t::text), '') FROM account_movements t WHERE kind <> 'Correction'");
        Assert.Equal(before["account_movements=".Length..], afterRows);
        Assert.Equal(accruals, await world.CountAsync("SELECT count(*) FROM account_movements c JOIN account_movements a ON a.id = c.corrects_movement_id WHERE c.kind = 'Correction' AND a.kind = 'RentAccrual' AND c.amount = -a.amount AND c.period = a.period"));
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));

        Assert.Equal("LoadedByMistake", await ScalarAsync<string>(world, "SELECT end_reason FROM contracts WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal("Ended", await ScalarAsync<string>(world, "SELECT status FROM contracts WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal(Today, await ScalarAsync<DateOnly>(world, "SELECT actual_end_date FROM contracts WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal("Cancelled", await ScalarAsync<string>(world, "SELECT kind FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.Equal(Reason, await ScalarAsync<string>(world, "SELECT reason FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
        Assert.Equal(login, await ScalarAsync<string>(world, "SELECT recorded_by::text FROM contract_events WHERE contract_id = @c", ("c", seeded.Contract)));
    }

    [SkippableFact]
    public async Task AContractWithNoMovements_IsCancelled_WritingOnlyTheEvent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.FirstOfMonth(Today).AddMonths(1));

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements"));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_events WHERE kind = 'Cancelled'"));
    }

    [SkippableFact]
    public async Task ItCanBeCancelled_HoweverManyDaysHavePassed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);

        AssertDone(await CancelAsync(world, seeded.Contract, clock: new FixedClock(Today.AddDays(800))));

        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    /// <summary>The summary names who and what, and the exact amount: what the desk asks for, unwritten periods and recargo included.</summary>
    [SkippableFact]
    public async Task TheSummary_IsWhatTheDeskAsksFor_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var asked = await AskDeskAsync(world, seeded.Account);
        var before = await HashAsync(world);

        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Null(summary.Refusal);
        Assert.Contains("Perez", summary.Tenants, StringComparison.Ordinal);
        Assert.Contains("Bv. Belgrano", summary.Units, StringComparison.Ordinal);
        Assert.Equal(asked.PreviousAmountOwed, summary.AmountStoppedBeingOwed);
        Assert.True(summary.AmountStoppedBeingOwed > 3 * 450_000m, $"{summary.AmountStoppedBeingOwed}.");
        Assert.Empty(summary.RetainedRecibos);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>Invalid recargo settings close the desk. They must not stop a cancellation, and no recargo is guessed.</summary>
    [SkippableFact]
    public async Task WithoutRecargoTerms_TheSummaryShowsTheLedger_AndItStillCancels()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var ledger = await BalanceAsync(world, seeded.Account);

        var summary = await new ContractRegistry(world.NewContext(), Clock).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Equal(ledger, summary.AmountStoppedBeingOwed);
        AssertDone(await new ContractRegistry(world.NewContext(), Clock).CancelAsync(seeded.Contract, Reason, summary));
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    /// <summary>What is written is what was read: a period written after the summary refuses, and writes nothing.</summary>
    [SkippableFact]
    public async Task AnAccountThatChangedAfterTheSummary_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        await world.MaterialiseAsync(seeded.Account, Today);
        var before = await HashAsync(world);

        var result = await Registry(world).CancelAsync(seeded.Contract, Reason, summary);

        Assert.Equal(ContractRefusal.CancellationStale, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>Corrections, the contract and the event go together or not at all.</summary>
    [SkippableFact]
    public async Task AFailureAtTheLastStep_RollsTheCancellationBack()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
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

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Registry(world).CancelAsync(seeded.Contract, Reason, summary));

        Assert.Equal("probe failure", Assert.IsType<PostgresException>(failure.GetBaseException()).MessageText);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>A cancellation takes the account's lock, so a period being written or paid is either in its plan or refused.</summary>
    [SkippableFact]
    public async Task ACancellation_WaitsForAWriterOnTheAccount_ThenRefuses()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        await using var writer = world.NewContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await new AccountMaterialiser(writer).MaterialiseDueAccrualsThroughAsync(seeded.Account, Today);

        var cancelling = Registry(world).CancelAsync(seeded.Contract, Reason, summary);
        var finishedFirst = await Task.WhenAny(cancelling, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(cancelling, finishedFirst);

        await transaction.CommitAsync();

        Assert.Equal(ContractRefusal.CancellationStale, (await cancelling).Refusal);
    }

    /// <summary>Two operators confirming the same summary: the second is refused, and nothing is reversed twice.</summary>
    [SkippableFact]
    public async Task ASecondConfirm_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        AssertDone(await Registry(world).CancelAsync(seeded.Contract, Reason, summary));
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).CancelAsync(seeded.Contract, Reason, summary)).Refusal);

        Assert.Equal(before, await HashAsync(world));
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).CancelAsync(Guid.NewGuid(), Reason, summary)).Refusal);
    }

    /// <summary>The migration's comment tells an operator what to expect: this failure, and a clean rollback leaving everything in place.</summary>
    [SkippableFact]
    public async Task M3sDown_FailsOnceAContractIsCancelled_AndLeavesEverythingInPlace()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await CancelAsync(world, seeded.Contract));
        var migrator = world.Database.Context.GetService<IMigrator>();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20261106150156_AddContractEventsAndTermsLock"));

        var violation = Assert.IsType<PostgresException>(failure.GetBaseException());
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        Assert.Equal("ck_contracts_end_reason", violation.ConstraintName);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%AddLoadedByMistakeEndReason'"));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM pg_constraint WHERE conname = 'ck_contracts_end_reason' AND pg_get_constraintdef(oid) LIKE '%LoadedByMistake%'"));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts WHERE end_reason = 'LoadedByMistake'"));
    }

    // ---- Test 73 ------------------------------------------------------------------------------------------------------

    /// <summary>The end date alone would still accrue what fell due before it: the months here were never written, and must stay so.</summary>
    [SkippableFact]
    public async Task NothingAccruesAfterwards_IncludingEarlierPeriodsNeverWritten()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        AssertDone(await CancelAsync(world, seeded.Contract));
        await world.MaterialiseAsync(seeded.Account, Today.AddMonths(3));

        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements"));
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    // ---- Test 74 ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ItsUnitIsFreed_AndANewContractCanNameIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var draft = new ContractDraft
        {
            TenantId = seeded.Tenant,
            LessorIds = [seeded.Tenant],
            Shares = [new UnitShare(seeded.Unit, 100m)],
            StartDate = DeskWorld.FirstOfMonth(Today).AddMonths(1),
            NominalEndDate = Today.AddYears(2),
            MonthlyRent = 500_000m,
            HonorariosPercentage = 8m,
        };
        Assert.Equal(ContractRefusal.UnitUnderActiveContract, (await Registry(world).CreateAsync(draft, true)).Refusal);

        AssertDone(await CancelAsync(world, seeded.Contract));

        await using (var read = world.NewContext())
        {
            Assert.Null((await new UnitRegistry(read).ListAsync()).Single(u => u.Id == seeded.Unit).Covering);
        }

        AssertDone(await Registry(world).CreateAsync(draft, true));
    }

    [SkippableFact]
    public async Task ItIsNotOfferedForCollection_NorListedAtTheDeskOrGoLive_AndAnOrdinaryEndStaysListed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var cancelled = await SeedAsync(world);
        var ended = await SeedAsync(world);
        await world.MaterialiseAsync(cancelled.Account, Today);
        AssertDone(await CancelAsync(world, cancelled.Contract));
        AssertDone(await Registry(world).EndAsync(ended.Contract, EndReason.Expiry, Today));

        var directory = await new AccountDirectory(world.NewContext(), Clock).SearchAsync("Perez");
        Assert.DoesNotContain(directory, l => l.AccountId == cancelled.Account);
        Assert.Contains(directory, l => l.AccountId == ended.Account);

        var goLive = await new GoLiveSetup(world.NewContext(), Clock).ListContractsAsync();
        Assert.DoesNotContain(goLive, l => l.ContractId == cancelled.Contract);
        Assert.Contains(goLive, l => l.ContractId == ended.Contract);

        var asked = await AskDeskAsync(world, cancelled.Account);
        Assert.False(asked.CanCommit);
        Assert.Equal(CollectionRefusal.NothingPayable, asked.Refusal);
        Assert.Equal(0m, asked.PreviousAmountOwed);
    }

    [SkippableFact]
    public async Task ItLeavesTheAdjustmentWorklist()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var seed = world.NewContext();
        var (contract, _) = SchemaConstraintTests.SeedContractWithClause(seed);
        await seed.SaveChangesAsync();
        var farAhead = new DateOnly(2099, 1, 1);

        async Task<bool> OnTheWorklistAsync() =>
            (await new DueAdjustmentQuery(world.NewContext()).GetDueAsync(farAhead)).Any(d => d.ContractId == contract.Id);

        Assert.True(await OnTheWorklistAsync());

        AssertDone(await CancelAsync(world, contract.Id));

        Assert.False(await OnTheWorklistAsync());
    }

    /// <summary>A cancelled contract is off the mora worklist: its balance is zero, and no recargo is projected on what was reversed.</summary>
    [SkippableFact]
    public async Task ItLeavesTheMoraWorklist()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);

        async Task<int> RowsAsync()
        {
            var context = world.NewContext();
            return (await new MoraWorklistQuery(context, new AccountMaterialiser(context)).GetAccountsInMoraAsync(Today, DeskWorld.Terms)).Count;
        }

        Assert.Equal(1, await RowsAsync());

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal(0, await RowsAsync());
    }

    [SkippableFact]
    public async Task ItStaysReadable_MarkedCancelled_WithItsReason_AndCannotBeUndone()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await CancelAsync(world, seeded.Contract));
        var before = await HashAsync(world);

        var detail = (await Registry(world).DetailAsync(seeded.Contract))!;
        Assert.True(detail.IsCancelled);
        Assert.Equal(EndReason.LoadedByMistake, detail.EndReason);
        var line = Assert.Single(detail.Events);
        Assert.Equal(ContractEventKind.Cancelled, line.Kind);
        Assert.Equal(Reason, line.Reason);
        Assert.False(string.IsNullOrWhiteSpace(line.RecordedBy));

        Assert.True(Assert.Single(await Registry(world).ListAsync(null, ContractStatus.Ended)).IsCancelled);

        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).UndoEndAsync(seeded.Contract, Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).SummarizeUndoEndAsync(seeded.Contract, Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason)).Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    // ---- Test 75 ------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(null)]
    public async Task AnEmptyReason_IsRefused_AndWritesNothing(string? reason)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var shown = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.ReasonMissing, (await Registry(world).SummarizeCancelAsync(seeded.Contract, reason)).Refusal);
        Assert.Equal(ContractRefusal.ReasonMissing, (await Registry(world).CancelAsync(seeded.Contract, reason, shown)).Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableFact]
    public async Task TheCheck_AcceptsLoadedByMistake_AndStillRefusesAnythingElse()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using var super = await ReciboTestSupport.OpenAsync(world.Database.Context);

        await ReciboTestSupport.ExecAsync(super, "UPDATE contracts SET end_reason = 'LoadedByMistake' WHERE id = @c", ("c", seeded.Contract));

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ReciboTestSupport.ExecAsync(super, "UPDATE contracts SET end_reason = 'Whim' WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal("ck_contracts_end_reason", refusal.ConstraintName);
    }

    // ---- Test 76 and Test 90 ------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task AVoidablePayment_Blocks_NamingItsRecibos_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);
        var before = await HashAsync(world);

        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Equal(ContractRefusal.PaymentMustBeVoidedFirst, summary.Refusal);
        Assert.Equal([DeskWorld.StartingNumber, DeskWorld.StartingNumber + 1], summary.RecibosToVoid);
        Assert.False(summary.OnlyAnAdminCanVoid);
        Assert.Contains($"{DeskWorld.StartingNumber}", summary.RefusalText, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin", summary.RefusalText, StringComparison.Ordinal);
        Assert.Equal(ContractRefusal.PaymentMustBeVoidedFirst, (await Registry(world).CancelAsync(seeded.Contract, Reason, summary)).Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>
    /// Two operators at once: the summary was clean, and a payment arrived before the confirm. Requirement 23 still has the refusal name the
    /// recibos to void. Found by the independent verifier of S5.
    /// </summary>
    [SkippableFact]
    public async Task APaymentBetweenSummaryAndConfirm_Refuses_NamingItsRecibos()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);
        var before = await HashAsync(world);

        var result = await Registry(world).CancelAsync(seeded.Contract, Reason, summary);

        Assert.Equal(ContractRefusal.PaymentMustBeVoidedFirst, result.Refusal);
        Assert.Contains($"{DeskWorld.StartingNumber}, {DeskWorld.StartingNumber + 1}", result.RefusalText, StringComparison.Ordinal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>A refused summary was never something to confirm, even on an account where it would happen to come out the same.</summary>
    [SkippableFact]
    public async Task ARefusedSummary_CannotBeConfirmed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.FirstOfMonth(Today).AddMonths(1));
        var before = await HashAsync(world);

        var result = await Registry(world).CancelAsync(seeded.Contract, Reason, ContractCancellationSummary.Refused(ContractRefusal.ReasonMissing));

        Assert.Equal(ContractRefusal.CancellationStale, result.Refusal);
        Assert.Equal(before, await HashAsync(world));
    }

    private static async Task RecordCollectionsAtAsync(DeskWorld world, Guid account, DateOnly day, string time)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(
            connection,
            $"SET session_replication_role = replica; UPDATE collections SET recorded_at = (timestamp '{day:yyyy-MM-dd} {time}') AT TIME ZONE 'America/Argentina/Buenos_Aires' WHERE account_id = @a; SET session_replication_role = DEFAULT;",
            ("a", account));
    }

    /// <summary>
    /// The window is measured in agency days, as voiding and the database measure it. A UTC day would move a payment recorded late at night
    /// across the boundary, and the operator would be told to void what can no longer be voided. Found by the independent verifier of S5.
    /// </summary>
    [SkippableTheory]
    [InlineData(31, "23:30:00", false)]
    [InlineData(30, "00:05:00", true)]
    [InlineData(8, "23:30:00", true)]
    public async Task TheWindow_IsMeasuredInAgencyDays_AndAgreesWithVoiding(int days, string time, bool blocks)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var desk = world.NewDesk();
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account)).Succeeded);
        await RecordCollectionsAtAsync(world, seeded.Account, Today.AddDays(-days), time);

        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Equal(blocks, summary.Refusal == ContractRefusal.PaymentMustBeVoidedFirst);
        Assert.Equal(blocks, summary.OnlyAnAdminCanVoid);

        // Whatever this calls voidable has to be something the desk really lets an Admin void, or the operator is stuck.
        var voiding = await desk.PreviewVoidAsync(await world.LatestReciboIdAsync(seeded.Account), "x", Domain.Access.UserRole.Admin);
        Assert.Equal(blocks, voiding.CanCommit && (await desk.VoidAsync(voiding)).Succeeded);
    }

    /// <summary>A payment past the window stays, so only the recent one is named. The text must not say an Admin is needed for a payment anyone can void.</summary>
    [SkippableFact]
    public async Task OneRecentPaymentAndOneOld_NamesOnlyTheRecent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var desk = world.NewDesk();
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account, Oldest)).Succeeded);
        await RecordCollectionsAtAsync(world, seeded.Account, Today.AddDays(-40), "12:00:00");
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account, Oldest.AddMonths(1))).Succeeded);

        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Equal([DeskWorld.StartingNumber + 2, DeskWorld.StartingNumber + 3], summary.RecibosToVoid);
        Assert.False(summary.OnlyAnAdminCanVoid);
    }

    /// <summary>A payment and an accrual arriving while a cancellation holds the lock wait for it, then find nothing to charge. Found by the independent verifier of S5.</summary>
    [SkippableFact]
    public async Task APaymentAndAnAccrual_ArrivingDuringACancellation_ChargeNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Oldest.AddMonths(1).AddDays(15));
        var (preview, request) = await DeskWorld.ConfirmedPreviewAsync(world.NewDesk(), seeded.Account);
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);
        var barrier = new SessionBarrier(2);
        var held = world.NewContext(CommandHook.WaitAtFirst(new System.Text.RegularExpressions.Regex("INSERT INTO \"?contract_events"), barrier));

        var cancelling = new ContractRegistry(held, Clock, DeskWorld.Terms).CancelAsync(seeded.Contract, Reason, summary);
        await AdvisoryLocksReachAsync(world, "granted", 1);
        var paying = world.NewDesk().CommitAsync(preview, request);
        var accruing = world.MaterialiseAsync(seeded.Account, Today);
        await AdvisoryLocksReachAsync(world, "NOT granted", 2);
        Assert.False(paying.IsCompleted);
        Assert.False(accruing.IsCompleted);
        barrier.Arrive();

        AssertDone(await cancelling);
        Assert.False((await paying).Succeeded);
        await accruing;
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    /// <summary>Waits on the database's own record of the lock, so a slow machine cannot start the competing writers too early.</summary>
    private static async Task AdvisoryLocksReachAsync(DeskWorld world, string state, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await world.CountAsync($"SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND {state}") < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Never saw {count} advisory locks {state}.");
            await Task.Delay(50);
        }
    }

    /// <summary>An early payment voided leaves rent dated ahead of today, the shape of S4's blocker. It has to go to zero whatever date is asked about.</summary>
    [SkippableFact]
    public async Task AVoidedEarlyPayment_GoesToZero_DatedRentIncluded()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world, DeskWorld.FirstOfMonth(Today));
        await using (var super = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(super, "UPDATE contracts SET due_day = 31 WHERE id = @c", ("c", seeded.Contract));
        }

        var desk = world.NewDesk();
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account)).Succeeded);
        var voiding = await desk.PreviewVoidAsync(await world.LatestReciboIdAsync(seeded.Account), "x", Domain.Access.UserRole.Admin);
        Assert.True((await desk.VoidAsync(voiding)).Succeeded);
        Assert.Equal(450_000m, await BalanceAsync(world, seeded.Account));

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
        Assert.Equal(0m, (await AskDeskAsync(world, seeded.Account)).PreviousAmountOwed);
    }

    /// <summary>What the event keeps is what happened: what was reversed, what stopped being owed, and the reason it replaced.</summary>
    [SkippableFact]
    public async Task TheEvent_KeepsWhatWasReversed_WhatStoppedBeingOwed_AndTheReasonReplaced()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Oldest.AddDays(14));
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var summary = await Registry(world).SummarizeCancelAsync(seeded.Contract, Reason);

        AssertDone(await Registry(world).CancelAsync(seeded.Contract, Reason, summary));

        var detail = await ScalarAsync<string>(world, "SELECT detail FROM contract_events WHERE kind = 'Cancelled'");
        Assert.Contains("Reversed 450000", detail, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"Stopped being owed {summary.AmountStoppedBeingOwed}"), detail, StringComparison.Ordinal);
        Assert.Contains("Replaced Expiry", detail, StringComparison.Ordinal);
    }

    /// <summary>Cancelling is final, so nothing on the contract changes afterward, and a mistaken rate does not come back as a suggestion.</summary>
    [SkippableFact]
    public async Task ACancelledContract_TakesNoMoreChanges_AndSuggestsNoRate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var lessor = await ScalarAsync<Guid>(world, "SELECT party_id FROM contract_parties WHERE contract_id = @c AND role = 'Lessor'", ("c", seeded.Contract));
        Assert.Equal(8m, await Registry(world).SuggestRateAsync(lessor));
        AssertDone(await CancelAsync(world, seeded.Contract));
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).SetRateAsync(seeded.Contract, 33m)).Refusal);
        Assert.Equal(ContractRefusal.ContractCancelled, (await Registry(world).SetCodebtorsAsync(seeded.Contract, [seeded.Tenant])).Refusal);
        Assert.Equal(ContractRefusal.NoticeNeedsActive, (await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today)).Refusal);
        Assert.Equal(before, await HashAsync(world));
        Assert.Null(await Registry(world).SuggestRateAsync(lessor));
    }

    /// <summary>Spec Test 90: a blocked cancellation says when only an Admin can void.</summary>
    [SkippableFact]
    public async Task APaymentOnlyAnAdminCanVoid_SaysSo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);

        var summary = await Registry(world, new FixedClock(Today.AddDays(8))).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Equal(ContractRefusal.PaymentMustBeVoidedFirst, summary.Refusal);
        Assert.True(summary.OnlyAnAdminCanVoid);
        Assert.Contains("Admin", summary.RefusalText, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task APaymentPastTheWindow_StaysWithItsRecibos_AndTheUnpaidMonthsAreReversed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), seeded.Account)).Succeeded);
        var paymentRows = await HashAsync(world, ["collections", "recibos", "recibo_lines", "recibo_annulments"]);
        var payment = await ScalarAsync<string>(world, "SELECT t::text FROM account_movements t WHERE kind = 'Payment'");
        var later = new FixedClock(Today.AddDays(31));

        var summary = await Registry(world, later).SummarizeCancelAsync(seeded.Contract, Reason);

        Assert.Null(summary.Refusal);
        Assert.Equal([DeskWorld.StartingNumber, DeskWorld.StartingNumber + 1], summary.RetainedRecibos);
        Assert.True(summary.RetainedAmount >= 450_000m, $"{summary.RetainedAmount}.");

        AssertDone(await Registry(world, later).CancelAsync(seeded.Contract, Reason, summary));

        Assert.Equal(paymentRows, await HashAsync(world, ["collections", "recibos", "recibo_lines", "recibo_annulments"]));
        Assert.Equal(payment, await ScalarAsync<string>(world, "SELECT t::text FROM account_movements t WHERE kind = 'Payment'"));
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements c JOIN account_movements p ON p.id = c.corrects_movement_id WHERE p.kind = 'Payment'"));
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    /// <summary>Voiding first is the route the refusal names, so it has to end in a cancellation that takes the rent the payment had covered.</summary>
    [SkippableFact]
    public async Task AfterVoidingThePayment_ItCancels_AndReversesTheRent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Today);
        var desk = world.NewDesk();
        Assert.True((await DeskWorld.PayAsync(desk, seeded.Account)).Succeeded);
        var voiding = await desk.PreviewVoidAsync(await world.LatestReciboIdAsync(seeded.Account), "Error de carga", Domain.Access.UserRole.Admin);
        Assert.True((await desk.VoidAsync(voiding)).Succeeded);

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    [SkippableFact]
    public async Task AContractAlreadyEnded_IsCancelled_ReplacingItsReason_AndKeepingItsDate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var endedOn = Oldest.AddMonths(1).AddDays(14);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, endedOn));
        await world.MaterialiseAsync(seeded.Account, Today);
        Assert.True(await BalanceAsync(world, seeded.Account) > 0m);

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal("LoadedByMistake", await ScalarAsync<string>(world, "SELECT end_reason FROM contracts WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal(endedOn, await ScalarAsync<DateOnly>(world, "SELECT actual_end_date FROM contracts WHERE id = @c", ("c", seeded.Contract)));
        Assert.Equal(0m, await BalanceAsync(world, seeded.Account));
    }

    // ---- Test 77 ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ATenantsOtherContract_IsUnaffected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await using (var seed = world.NewContext())
        {
            seed.Units.Add(new PropertyUnit(OtherUnit, new Address("Alem", "9", "Sunchales", "Santa Fe", "2322")));
            await seed.SaveChangesAsync();
        }

        var other = await Registry(world).CreateAsync(
            new ContractDraft
            {
                TenantId = seeded.Tenant,
                LessorIds = [seeded.Tenant],
                Shares = [new UnitShare(OtherUnit, 100m)],
                StartDate = DeskWorld.FirstOfMonth(Today),
                CutoverDate = null,
                NominalEndDate = Today.AddYears(2),
                MonthlyRent = 300_000m,
                HonorariosPercentage = 8m,
            },
            true);
        AssertDone(other);
        await using (var read = world.NewContext())
        {
            var account = await read.ContractAccounts.AsNoTracking().SingleAsync(a => a.ContractId == other.Contract!.Id);
            await world.MaterialiseAsync(account.Id, Today.AddDays(40));
        }

        async Task<string> OtherRowsAsync() => await ScalarAsync<string>(
            world,
            """
            SELECT (SELECT c::text FROM contracts c WHERE c.id = @o)
                || coalesce((SELECT string_agg(m::text, '|' ORDER BY m::text) FROM account_movements m JOIN contract_accounts a ON a.id = m.account_id WHERE a.contract_id = @o), '')
            """,
            ("o", other.Contract!.Id));

        var before = await OtherRowsAsync();

        AssertDone(await CancelAsync(world, seeded.Contract));

        Assert.Equal(before, await OtherRowsAsync());
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contract_parties WHERE contract_id = @o AND party_id = @t AND removed_at IS NULL AND role = 'Tenant'", ("o", other.Contract!.Id), ("t", seeded.Tenant)));
    }

    private static readonly Guid OtherUnit = Guid.NewGuid();

    // ---- Test 78 ------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task UndoingANotice_ReturnsToActive_WithBothDatesEmpty_AndATrace(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today.AddDays(30)));
        var (registry, context, login) = await AsAsync(world, role);

        await using (context)
        {
            var result = await registry.UndoNoticeAsync(seeded.Contract, "Otro contrato");

            AssertDone(result);
            Assert.Equal(ContractStatus.Active, result.Contract!.Status);
        }

        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts WHERE id = @c AND status = 'Active' AND notice_given_date IS NULL AND planned_move_out_date IS NULL", ("c", seeded.Contract)));
        Assert.Equal("NoticeUndone", await ScalarAsync<string>(world, "SELECT kind FROM contract_events"));
        Assert.Equal("Otro contrato", await ScalarAsync<string>(world, "SELECT reason FROM contract_events"));
        Assert.Equal(login, await ScalarAsync<string>(world, "SELECT recorded_by::text FROM contract_events"));
        Assert.Contains(Today.AddDays(30).ToString("yyyy-MM-dd"), await ScalarAsync<string>(world, "SELECT detail FROM contract_events"), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task UndoingANotice_WithoutOneOrWithoutAReason_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.NoNoticeToUndo, (await Registry(world).UndoNoticeAsync(seeded.Contract, Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).UndoNoticeAsync(Guid.NewGuid(), Reason)).Refusal);
        Assert.Equal(before, await HashAsync(world));

        AssertDone(await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today));
        var withNotice = await HashAsync(world);

        Assert.Equal(ContractRefusal.ReasonMissing, (await Registry(world).UndoNoticeAsync(seeded.Contract, "  ")).Refusal);
        Assert.Equal(withNotice, await HashAsync(world));
    }

    // ---- Test 79 ------------------------------------------------------------------------------------------------------

    /// <summary>Spec scenario: ended by mistake, undone later, and the months in between accrue.</summary>
    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task UndoingAnEnd_ResumesAccrual_ForTheMonthsBetween(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var endedOn = Oldest.AddDays(4);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, endedOn));
        await world.MaterialiseAsync(seeded.Account, Today);
        Assert.Equal(0, await world.CountAsync("SELECT count(*) FROM account_movements"));
        var (registry, context, login) = await AsAsync(world, role);

        IReadOnlyList<DateOnly> named;
        await using (context)
        {
            var summary = await registry.SummarizeUndoEndAsync(seeded.Contract, "Terminado por error");
            Assert.Null(summary.Refusal);
            named = summary.PeriodsToAccrue;

            var result = await registry.UndoEndAsync(seeded.Contract, "Terminado por error");

            AssertDone(result);
            Assert.Equal(ContractStatus.Active, result.Contract!.Status);
        }

        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts WHERE id = @c AND status = 'Active' AND actual_end_date IS NULL AND end_reason IS NULL", ("c", seeded.Contract)));
        Assert.Equal("EndUndone", await ScalarAsync<string>(world, "SELECT kind FROM contract_events"));
        Assert.Equal(login, await ScalarAsync<string>(world, "SELECT recorded_by::text FROM contract_events"));
        Assert.Contains($"Expiry {endedOn:yyyy-MM-dd}", await ScalarAsync<string>(world, "SELECT detail FROM contract_events"), StringComparison.Ordinal);

        await world.MaterialiseAsync(seeded.Account, Today);

        Assert.True(named.Count >= 3, $"{named.Count} named.");
        Assert.Equal(named.Count, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual'"));
        Assert.Contains(Oldest, named);
    }

    /// <summary>The summary names only what will newly accrue: a period already on the ledger is not news.</summary>
    [SkippableFact]
    public async Task TheUndoSummary_LeavesOutPeriodsAlreadyWritten_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        await world.MaterialiseAsync(seeded.Account, Oldest.AddDays(14));
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Oldest.AddDays(14)));
        var before = await HashAsync(world);

        var summary = await Registry(world).SummarizeUndoEndAsync(seeded.Contract, Reason);

        Assert.DoesNotContain(Oldest, summary.PeriodsToAccrue);
        Assert.Contains(Oldest.AddMonths(1), summary.PeriodsToAccrue);
        Assert.Equal(before, await HashAsync(world));
    }

    [SkippableFact]
    public async Task UndoingAnEnd_AfterNotice_ReturnsToPendingTermination()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await Registry(world).GiveNoticeAsync(seeded.Contract, Today, Today.AddDays(30)));
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));

        var result = await Registry(world).UndoEndAsync(seeded.Contract, Reason);

        AssertDone(result);
        Assert.Equal(ContractStatus.PendingTermination, result.Contract!.Status);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM contracts WHERE id = @c AND notice_given_date IS NOT NULL", ("c", seeded.Contract)));
    }

    /// <summary>Spec scenario: a unit let again blocks the undo, naming that contract, whichever of the two live states it holds.</summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUnitLetAgain_BlocksTheUndo_NamingTheTenant(bool givingNotice)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var second = await SeedAsync(world);
        await using (var super = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(super, "UPDATE contract_units SET unit_id = @u WHERE contract_id = @c", ("u", seeded.Unit), ("c", second.Contract));
        }

        if (givingNotice)
        {
            AssertDone(await Registry(world).GiveNoticeAsync(second.Contract, Today, Today));
        }

        var before = await HashAsync(world);

        var summary = await Registry(world).SummarizeUndoEndAsync(seeded.Contract, Reason);
        var result = await Registry(world).UndoEndAsync(seeded.Contract, Reason);

        Assert.Equal(ContractRefusal.UnitCoveredByAnotherContract, summary.Refusal);
        Assert.Contains("Perez", summary.CurrentTenant, StringComparison.Ordinal);
        Assert.Equal(ContractRefusal.UnitCoveredByAnotherContract, result.Refusal);
        Assert.Contains("Perez", result.CurrentTenant, StringComparison.Ordinal);
        Assert.Contains("Perez", result.Refusal!.Value.OperatorText(result.CurrentTenant), StringComparison.Ordinal);
        Assert.Equal(before, await HashAsync(world));
    }

    /// <summary>
    /// Its units are locked before they are read, or a contract being created now could let one of them too. Nothing else stops it: an
    /// undo writes no row naming the unit, so no foreign key makes it wait.
    /// </summary>
    [SkippableFact]
    public async Task UndoingAnEnd_WaitsForAUnitBeingLetRightNow_ThenRefuses()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));
        var other = await SeedAsync(world);
        await using var letting = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(
            letting,
            "BEGIN; SELECT 1 FROM units WHERE id = @u FOR UPDATE; UPDATE contract_units SET unit_id = @u WHERE contract_id = @c;",
            ("u", seeded.Unit),
            ("c", other.Contract));

        var undoing = Registry(world).UndoEndAsync(seeded.Contract, Reason);
        var finishedFirst = await Task.WhenAny(undoing, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(undoing, finishedFirst);

        await ReciboTestSupport.ExecAsync(letting, "COMMIT");

        Assert.Equal(ContractRefusal.UnitCoveredByAnotherContract, (await undoing).Refusal);
    }

    [SkippableFact]
    public async Task UndoingAnEnd_OnAContractNotEnded_OrWithoutAReason_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);

        Assert.Equal(ContractRefusal.NoEndToUndo, (await Registry(world).UndoEndAsync(seeded.Contract, Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).UndoEndAsync(Guid.NewGuid(), Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SummarizeUndoEndAsync(Guid.NewGuid(), Reason)).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SummarizeCancelAsync(Guid.NewGuid(), Reason)).Refusal);

        AssertDone(await Registry(world).EndAsync(seeded.Contract, EndReason.Expiry, Today));

        Assert.Equal(ContractRefusal.ReasonMissing, (await Registry(world).UndoEndAsync(seeded.Contract, " ")).Refusal);
    }

    // ---- Test 89 ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task EndingAsLoadedByMistake_IsRefused_HoweverItArrives()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SeedAsync(world);
        var before = await HashAsync(world);

        Assert.Equal(ContractRefusal.EndReasonNotOffered, (await Registry(world).EndAsync(seeded.Contract, EndReason.LoadedByMistake, Today)).Refusal);
        Assert.Equal(ContractRefusal.EndReasonNotOffered, (await Registry(world).SummarizeEndAsync(seeded.Contract, EndReason.LoadedByMistake, Today)).Refusal);
        Assert.Equal(before, await HashAsync(world));
    }
}
