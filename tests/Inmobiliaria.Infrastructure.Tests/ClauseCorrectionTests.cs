using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 38, 40 and 81 (Requirements 13 and 14, Decisions 25, 32 and 39) against real Postgres, as real
/// logins where the role matters. All people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClauseCorrectionTests
{
    private static readonly DateOnly Today = DeskWorld.Today;
    private static readonly IAgencyClock Clock = new FixedClock(Today);
    private static readonly DateOnly NextMonth = DeskWorld.FirstOfMonth(Today).AddMonths(1);

    private readonly PostgresFixture _fixture;

    public ClauseCorrectionTests(PostgresFixture fixture) => _fixture = fixture;

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static ContractRegistry Registry(DeskWorld world) => new(world.NewContext(), Clock, DeskWorld.Terms);

    private static async Task<Guid> IndexAsync(DeskWorld world, string name)
    {
        await using var read = world.NewContext();
        return await read.EconomicIndices.AsNoTracking().Where(i => i.Name == name).Select(i => i.Id).SingleAsync();
    }

    private static async Task<Guid> ContractOfAsync(DeskWorld world, Guid accountId)
    {
        await using var read = world.NewContext();
        return await read.ContractAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.ContractId).SingleAsync();
    }

    private static async Task<ContractClauseLine?> ClauseOfAsync(DeskWorld world, Guid contractId) =>
        (await Registry(world).DetailAsync(contractId))!.Clause;

    private static async Task<(Guid Contract, Guid Clause, Guid Index)> SeedConfirmedAsync(DeskWorld world)
    {
        await using var seed = world.NewContext();
        var userId = await AccessTestSupport.ProvisionUserAsync(seed, $"clausula-{Guid.NewGuid():N}"[..20], UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(seed);
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 8_000m, new IndexPeriod(2026, 7), 9_200m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, userId);
        await seed.SaveChangesAsync();

        var clauseId = await seed.AdjustmentClauses.AsNoTracking().Where(c => c.ContractId == contract.Id).Select(c => c.Id).SingleAsync();
        return (contract.Id, clauseId, index.Id);
    }

    private static async Task<NpgsqlConnection> OpenAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, role, password);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertHasMovementsAsync(DeskWorld world) =>
        Assert.True(await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'RentAccrual'") > 0);

    private static Task<long> IndicesOfAsync(DeskWorld world, Guid clause) =>
        world.CountAsync("SELECT count(*) FROM adjustment_clause_indices WHERE adjustment_clause_id = @c", ("c", clause));

    // ---- Test 38, through the port ----------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task AClause_CanBeAttachedAfterMovements_AndTheFormsOffsetIsKept()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var account = await world.SeedAccountAsync(NextMonth);
        await world.MaterialiseAsync(account, NextMonth.AddDays(14));
        await AssertHasMovementsAsync(world);
        var contract = await ContractOfAsync(world, account);
        var icl = await IndexAsync(world, "ICL");

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([icl], 6, 0));

        Assert.True(result.Succeeded);
        var clause = await ClauseOfAsync(world, contract);
        Assert.Equal(["ICL"], clause!.IndexNames);
        Assert.Equal([icl], clause.IndexIds);
        Assert.Equal(6, clause.IntervalMonths);
        Assert.Equal(0, clause.WindowOffsetMonths);
    }

    /// <summary>The offset the form chose, through the one path a new contract takes, including the 0 EF could take as unset.</summary>
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task CreatingAContract_KeepsItsClausesOffset(int offset)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var earlier = await ContractOfAsync(world, await world.SeedAccountAsync(DeskWorld.OldestPeriod));
        var detail = (await Registry(world).DetailAsync(earlier))!;
        Assert.True((await Registry(world).EndAsync(earlier, EndReason.MutualAgreement, Today)).Succeeded);

        var created = await Registry(world).CreateAsync(
            new ContractDraft
            {
                TenantId = detail.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId,
                LessorIds = detail.Parties.Where(p => p.Role == PartyRole.Lessor).Select(p => p.PartyId).ToList(),
                Shares = [new UnitShare(detail.Units.Single().UnitId, 100m)],
                StartDate = NextMonth,
                NominalEndDate = NextMonth.AddYears(2),
                MonthlyRent = 120_000m,
                HonorariosPercentage = 8m,
                Clause = new ClauseDraft([await IndexAsync(world, "ICL")], 6, offset),
            },
            acceptWarnings: true);

        Assert.True(created.Succeeded, created.Refusal?.ToString());
        Assert.Equal(offset, (await ClauseOfAsync(world, created.Contract!.Id))!.WindowOffsetMonths);
    }

    [SkippableFact]
    public async Task AClause_CanBeCorrectedAfterMovements_FromTwoIndicesToOne_AndTakesEffectOnTheNextProposal()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var account = await world.SeedAccountAsync(NextMonth);
        await world.MaterialiseAsync(account, NextMonth.AddDays(14));
        await AssertHasMovementsAsync(world);
        var contract = await ContractOfAsync(world, account);
        var (ipc, ripte) = (await IndexAsync(world, "IPC"), await IndexAsync(world, "RIPTE"));
        Assert.True((await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc, ripte], 6, 3))).Succeeded);

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc], 3, 2));

        Assert.True(result.Succeeded);
        var clause = await ClauseOfAsync(world, contract);
        Assert.Equal(["IPC"], clause!.IndexNames);
        Assert.Equal(3, clause.IntervalMonths);
        Assert.Equal(2, clause.WindowOffsetMonths);

        await using var read = world.NewContext();
        var due = Assert.Single(await new DueAdjustmentQuery(read).GetDueAsync(NextMonth.AddMonths(3)), d => d.ContractId == contract);
        var expected = AdjustmentWindow.For(NextMonth.AddMonths(3), 3, 2);
        Assert.Equal((expected.Base, expected.End), (due.BasePeriod, due.EndPeriod));
    }

    [SkippableFact]
    public async Task CorrectingTheOrderOfTheIndices_KeepsTheOrderGiven()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        var (ipc, ripte) = (await IndexAsync(world, "IPC"), await IndexAsync(world, "RIPTE"));
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc, ripte], 6, 3));

        await Registry(world).SetClauseAsync(contract, new ClauseDraft([ripte, ipc], 6, 3));

        Assert.Equal(["RIPTE", "IPC"], (await ClauseOfAsync(world, contract))!.IndexNames);
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM adjustment_clause_indices WHERE economic_index_id = @i AND ordinal = 0", ("i", ripte)));
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM adjustment_clause_indices WHERE economic_index_id = @i AND ordinal = 1", ("i", ipc)));
    }

    [SkippableFact]
    public async Task AfterAConfirmedAdjustment_ACorrectionIsRefused_AndChangesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var (contract, clause, index) = await SeedConfirmedAsync(world);
        var before = await ClauseOfAsync(world, contract);

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], 12, 2));

        Assert.Equal(ContractRefusal.AdjustmentConfirmed, result.Refusal);
        var after = await ClauseOfAsync(world, contract);
        Assert.Equal((before!.IntervalMonths, before.WindowOffsetMonths), (after!.IntervalMonths, after.WindowOffsetMonths));
        Assert.Equal([index], after.IndexIds);
        Assert.Equal(1, await IndicesOfAsync(world, clause));
    }

    [SkippableTheory]
    [InlineData(4, 6)]
    [InlineData(-1, 6)]
    [InlineData(1, 0)]
    [InlineData(1, 61)]
    public async Task AnInvalidClause_IsRefusedBeforeAnythingIsWritten(int offset, int interval)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], interval, offset));

        Assert.Equal(ContractRefusal.ClauseInvalid, result.Refusal);
        Assert.Null(await ClauseOfAsync(world, contract));
    }

    [SkippableFact]
    public async Task AnIndexThatDoesNotExist_IsRefused_AndKeepsTheClause()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        var ipc = await IndexAsync(world, "IPC");
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc], 6, 2));

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([Guid.NewGuid()], 3, 0));

        Assert.Equal(ContractRefusal.IndexNotFound, result.Refusal);
        var clause = await ClauseOfAsync(world, contract);
        Assert.Equal([ipc], clause!.IndexIds);
        Assert.Equal((6, 2), (clause.IntervalMonths, clause.WindowOffsetMonths));
    }

    [SkippableFact]
    public async Task AnEndedContract_AndOneThatDoesNotExist_AreRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var account = await world.SeedAccountAsync(DeskWorld.OldestPeriod);
        var contract = await ContractOfAsync(world, account);
        var ipc = await IndexAsync(world, "IPC");
        Assert.True((await Registry(world).EndAsync(contract, EndReason.MutualAgreement, Today)).Succeeded);

        Assert.Equal(ContractRefusal.ContractEnded, (await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc], 6, 2))).Refusal);
        Assert.Equal(ContractRefusal.ContractNotFound, (await Registry(world).SetClauseAsync(Guid.NewGuid(), new ClauseDraft([ipc], 6, 2))).Refusal);
    }

    [SkippableFact]
    public async Task ACancelledContract_IsRefusedAsCancelled()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        var summary = await Registry(world).SummarizeCancelAsync(contract, "Cargado por error");
        Assert.True((await Registry(world).CancelAsync(contract, "Cargado por error", summary)).Succeeded);

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], 6, 2));

        Assert.Equal(ContractRefusal.ContractCancelled, result.Refusal);
    }

    // ---- Test 38, as each role ----------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task EachRole_ReplacesIndicesThroughTheFunction_ThoughNeitherHoldsDelete(string role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        var (ipc, ripte) = (await IndexAsync(world, "IPC"), await IndexAsync(world, "RIPTE"));
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc, ripte], 6, 3));
        var clause = await world.NewContext().AdjustmentClauses.AsNoTracking().Where(c => c.ContractId == contract).Select(c => c.Id).SingleAsync();
        await using var connection = await OpenAsAsync(world, role);

        var noDelete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, "DELETE FROM adjustment_clause_indices WHERE adjustment_clause_id = @c", ("c", clause)));
        Assert.Equal("42501", noDelete.SqlState);

        await ExecuteAsync(connection, "SELECT app_replace_clause_indices(@c, @i)", ("c", clause), ("i", new[] { ripte }));

        Assert.Equal(["RIPTE"], (await ClauseOfAsync(world, contract))!.IndexNames);
    }

    public static TheoryData<string, string> Writes => new()
    {
        { "inmobiliaria_admin", "SELECT app_replace_clause_indices(@c, @i)" },
        { "inmobiliaria_empleado", "SELECT app_replace_clause_indices(@c, @i)" },
        { "inmobiliaria_admin", "UPDATE adjustment_clauses SET window_offset_months = 2 WHERE id = @c" },
        { "inmobiliaria_empleado", "UPDATE adjustment_clauses SET window_offset_months = 2 WHERE id = @c" },
        { "inmobiliaria_admin", "UPDATE adjustment_clauses SET interval_months = 12 WHERE id = @c" },
        { "inmobiliaria_empleado", "UPDATE adjustment_clauses SET interval_months = 12 WHERE id = @c" },
        { "inmobiliaria_admin", "INSERT INTO adjustment_clause_indices (adjustment_clause_id, economic_index_id, ordinal) VALUES (@c, @i[1], 1)" },
        { "inmobiliaria_empleado", "INSERT INTO adjustment_clause_indices (adjustment_clause_id, economic_index_id, ordinal) VALUES (@c, @i[1], 1)" },
        { "inmobiliaria_admin", "UPDATE adjustment_clause_indices SET ordinal = 5 WHERE adjustment_clause_id = @c AND @i IS NOT NULL" },
        { "inmobiliaria_empleado", "UPDATE adjustment_clause_indices SET ordinal = 5 WHERE adjustment_clause_id = @c AND @i IS NOT NULL" },
    };

    [SkippableTheory]
    [MemberData(nameof(Writes))]
    public async Task AfterAConfirmedAdjustment_EveryWayToChangeTheClause_IsRefused_AsEachRole(string role, string sql)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var (contract, clause, index) = await SeedConfirmedAsync(world);
        var other = await IndexAsync(world, "IPC");
        await using var connection = await OpenAsAsync(world, role);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, sql, ("c", clause), ("i", new[] { other })));

        Assert.Equal("P0001", refused.SqlState);
        Assert.Equal("adjustment_clauses_locked", refused.ConstraintName);
        var after = await ClauseOfAsync(world, contract);
        Assert.Equal([index], after!.IndexIds);
        Assert.Equal(1, after.WindowOffsetMonths);
        Assert.Equal(6, after.IntervalMonths);
    }

    /// <summary>Attaching runs the clause table's INSERT branch of the lock, the only way a clause can arrive after an adjustment.</summary>
    [SkippableFact]
    public async Task AttachingToAContractWithAConfirmedAdjustment_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var (contract, _, _) = await SeedConfirmedAsync(world);
        await using (var superuser = world.NewContext())
        {
            await superuser.Database.ExecuteSqlAsync($"DELETE FROM adjustment_clauses WHERE contract_id = {contract}");
        }

        var result = await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], 6, 2));

        Assert.Equal(ContractRefusal.AdjustmentConfirmed, result.Refusal);
        Assert.Null(await ClauseOfAsync(world, contract));

        await using var connection = await OpenAsAsync(world, "inmobiliaria_empleado");
        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO adjustment_clauses (id, contract_id, interval_months, rounding_rule) VALUES (@n, @k, 6, 'TruncateToWholePeso')",
            ("n", Guid.NewGuid()),
            ("k", contract)));
        Assert.Equal("adjustment_clauses_locked", refused.ConstraintName);
    }

    /// <summary>The lock reads committed rows only, so without waiting on the contract a write could slip in beside an adjustment.</summary>
    [SkippableTheory]
    [InlineData("UPDATE adjustment_clauses SET window_offset_months = 3 WHERE id = @c")]
    [InlineData("INSERT INTO adjustment_clause_indices (adjustment_clause_id, economic_index_id, ordinal) VALUES (@c, @i[1], 1)")]
    public async Task AWriteRacingAnAdjustmentBeingConfirmed_WaitsForIt_AndIsRefused(string sql)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var confirming = world.NewContext();
        var userId = await AccessTestSupport.ProvisionUserAsync(confirming, $"carrera-{Guid.NewGuid():N}"[..20], UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(confirming);
        var clause = await confirming.AdjustmentClauses.AsNoTracking().Where(c => c.ContractId == contract.Id).Select(c => c.Id).SingleAsync();
        var other = await IndexAsync(world, "IPC");
        await using var connection = await OpenAsAsync(world, "inmobiliaria_empleado");

        await using var transaction = await confirming.Database.BeginTransactionAsync();
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 8_000m, new IndexPeriod(2026, 7), 9_200m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, userId);
        await confirming.SaveChangesAsync();

        var racing = ExecuteAsync(connection, sql, ("c", clause), ("i", new[] { other }));
        await Task.Delay(500);
        Assert.False(racing.IsCompleted);

        await transaction.CommitAsync();

        var refused = await Assert.ThrowsAsync<PostgresException>(() => racing);
        Assert.Equal("adjustment_clauses_locked", refused.ConstraintName);
    }

    [SkippableTheory]
    [InlineData("{}")]
    [InlineData("{a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11,a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11}")]
    [InlineData("{NULL}")]
    public async Task TheFunction_RefusesAnEmptyOrRepeatedList_AndLeavesTheClauseWithItsIndices(string list)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], 6, 2));
        var clause = await world.NewContext().AdjustmentClauses.AsNoTracking().Where(c => c.ContractId == contract).Select(c => c.Id).SingleAsync();
        await using var connection = await OpenAsAsync(world, "inmobiliaria_empleado");

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, $"SELECT app_replace_clause_indices(@c, '{list}'::uuid[])", ("c", clause)));

        Assert.Equal("adjustment_clause_indices_invalid", refused.ConstraintName);
        Assert.Equal(1, await IndicesOfAsync(world, clause));
    }

    [SkippableFact]
    public async Task TheFunction_RefusesAClauseThatDoesNotExist()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using var connection = await OpenAsAsync(world, "inmobiliaria_admin");

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, "SELECT app_replace_clause_indices(@c, @i)", ("c", Guid.NewGuid()), ("i", new[] { Guid.NewGuid() })));

        Assert.Equal("adjustment_clauses_not_found", refused.ConstraintName);
    }

    /// <summary>
    /// A search_path that does not name pg_temp searches it first. A role's temporary table could then stand in for the real one,
    /// and a trigger on it would run as the function's owner.
    /// </summary>
    [SkippableFact]
    public async Task TheFunction_IgnoresTemporaryTablesNamedLikeItsOwn()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(NextMonth));
        var (ipc, ripte) = (await IndexAsync(world, "IPC"), await IndexAsync(world, "RIPTE"));
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([ipc], 6, 2));
        var clause = await world.NewContext().AdjustmentClauses.AsNoTracking().Where(c => c.ContractId == contract).Select(c => c.Id).SingleAsync();
        await using var connection = await OpenAsAsync(world, "inmobiliaria_empleado");
        await ExecuteAsync(connection, "CREATE TEMP TABLE adjustment_clauses (id uuid, contract_id uuid)");
        await ExecuteAsync(connection, "CREATE TEMP TABLE adjustment_clause_indices (adjustment_clause_id uuid, economic_index_id uuid, ordinal int)");
        await ExecuteAsync(connection, "INSERT INTO pg_temp.adjustment_clauses VALUES (@c, gen_random_uuid())", ("c", clause));

        await ExecuteAsync(connection, "SELECT app_replace_clause_indices(@c, @i)", ("c", clause), ("i", new[] { ripte }));

        Assert.Equal(["RIPTE"], (await ClauseOfAsync(world, contract))!.IndexNames);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_temp.adjustment_clause_indices", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task TheFunction_IsDefinerWithAPinnedSearchPath_AndOnlyTheTwoRolesCanRunIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        const string Function = "app_replace_clause_indices(uuid, uuid[])";

        Assert.Equal(1, await world.CountAsync(
            "SELECT count(*) FROM pg_proc WHERE proname = 'app_replace_clause_indices' AND prosecdef AND array_to_string(proconfig, ',') LIKE '%search_path=pg_catalog, public, pg_temp'"));
        Assert.Equal(1, await world.CountAsync($"SELECT count(*) WHERE has_function_privilege('inmobiliaria_admin', '{Function}', 'EXECUTE')"));
        Assert.Equal(1, await world.CountAsync($"SELECT count(*) WHERE has_function_privilege('inmobiliaria_empleado', '{Function}', 'EXECUTE')"));
        Assert.Equal(0, await world.CountAsync($"SELECT count(*) WHERE has_function_privilege('public', '{Function}', 'EXECUTE')"));
    }

    // ---- Test 40 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task AContractUnderNotice_IsListed_AndOnceEndedItIsNot()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var contract = await ContractOfAsync(world, await world.SeedAccountAsync(DeskWorld.OldestPeriod));
        await Registry(world).SetClauseAsync(contract, new ClauseDraft([await IndexAsync(world, "IPC")], 1, 2));

        async Task<bool> ListedAsync()
        {
            await using var read = world.NewContext();
            return (await new DueAdjustmentQuery(read).GetDueAsync(Today)).Any(d => d.ContractId == contract);
        }

        Assert.True(await ListedAsync());

        Assert.True((await Registry(world).GiveNoticeAsync(contract, Today, Today.AddMonths(2))).Succeeded);
        Assert.Equal(ContractStatus.PendingTermination, (await Registry(world).DetailAsync(contract))!.Status);
        Assert.True(await ListedAsync());

        Assert.True((await Registry(world).EndAsync(contract, EndReason.MutualAgreement, Today)).Succeeded);
        Assert.False(await ListedAsync());
    }

    // ---- Test 81 --------------------------------------------------------------------------------------------------------

    /// <param name="beforeTheDueDate">
    /// False: ended last month past its due date, so that month is the old contract's. True: ended on this month's 1st, before its
    /// due date, so this month is left uncharged and named, and the new contract starts on that same 1st (spec Decision 39).
    /// </param>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAgreedNewDeal_LeavesTheOldDebtCollectable_NoPeriodTwiceOrSkipped_AndTheOldAdjustmentOffTheWorklist(bool beforeTheDueDate)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var thisMonth = DeskWorld.FirstOfMonth(Today);
        var endDay = beforeTheDueDate ? thisMonth : thisMonth.AddMonths(-1).AddDays(14);
        var oldAccount = await world.SeedAccountAsync(DeskWorld.OldestPeriod);
        var oldContract = await ContractOfAsync(world, oldAccount);
        Assert.True((await Registry(world).SetClauseAsync(oldContract, new ClauseDraft([await IndexAsync(world, "IPC")], 1, 2))).Succeeded);
        var detail = (await Registry(world).DetailAsync(oldContract))!;

        await using (var before = world.NewContext())
        {
            Assert.Contains(await new DueAdjustmentQuery(before).GetDueAsync(Today), d => d.ContractId == oldContract);
        }

        var summary = await Registry(world).SummarizeEndAsync(oldContract, EndReason.MutualAgreement, endDay);
        Assert.Equal(beforeTheDueDate ? thisMonth : null, summary.MonthLeftUncharged);

        Assert.True((await Registry(world).EndAsync(oldContract, EndReason.MutualAgreement, endDay)).Succeeded);

        var created = await Registry(world).CreateAsync(
            new ContractDraft
            {
                TenantId = detail.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId,
                LessorIds = detail.Parties.Where(p => p.Role == PartyRole.Lessor).Select(p => p.PartyId).ToList(),
                Shares = [new UnitShare(detail.Units.Single().UnitId, 100m)],
                StartDate = thisMonth,
                NominalEndDate = thisMonth.AddYears(2),
                MonthlyRent = 120_000m,
                HonorariosPercentage = 8m,
            },
            acceptWarnings: true);
        Assert.True(created.Succeeded, created.Refusal?.ToString());

        await using var read = world.NewContext();
        var newAccount = await read.ContractAccounts.AsNoTracking()
            .Where(a => a.ContractId == created.Contract!.Id).Select(a => a.Id).SingleAsync();
        var through = NextMonth.AddMonths(1).AddDays(14);
        await world.MaterialiseAsync(oldAccount, through);
        await world.MaterialiseAsync(newAccount, through);

        var oldPeriods = await PeriodsAsync(world, oldAccount);
        var newPeriods = await PeriodsAsync(world, newAccount);
        Assert.Equal(DeskWorld.OldestPeriod, oldPeriods[0]);
        Assert.Equal(thisMonth.AddMonths(-1), oldPeriods[^1]);
        Assert.Equal(thisMonth, newPeriods[0]);
        var all = oldPeriods.Concat(newPeriods).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(all, Enumerable.Range(0, all.Count).Select(i => DeskWorld.OldestPeriod.AddMonths(i)));

        await using var worklist = world.NewContext();
        Assert.DoesNotContain(await new DueAdjustmentQuery(worklist).GetDueAsync(through), d => d.ContractId == oldContract);

        var paid = await DeskWorld.PayAsync(world.NewDesk(Clock), oldAccount, DeskWorld.OldestPeriod);
        Assert.True(paid.Succeeded, paid.Refusal?.ToString());
        Assert.Equal(1, await world.CountAsync("SELECT count(*) FROM collections WHERE id = @c", ("c", paid.CollectionId!.Value)));
    }

    private static async Task<List<DateOnly>> PeriodsAsync(DeskWorld world, Guid accountId)
    {
        await using var read = world.NewContext();
        return (await read.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == accountId))
            .Movements.Where(m => m.Kind == MovementKind.RentAccrual).Select(m => m.Period!.Value).Order().ToList();
    }
}
