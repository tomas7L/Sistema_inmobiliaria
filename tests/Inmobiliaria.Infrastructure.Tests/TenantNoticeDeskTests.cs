using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// notificaciones spec Tests 1 to 5 and 7 (Requirements 1 to 3): today's board against a real engine. Each test owns a freshly
/// migrated database, so the board holds only what the test seeded. The figures are checked against the cash desk's own
/// preview, through its public port: a notice that disagrees with the receipt is worse than none (spec Decision 9).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantNoticeDeskTests
{
    private readonly PostgresFixture _fixture;

    public TenantNoticeDeskTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly Today => DeskWorld.Today;

    /// <summary>A difference attached to next month: on the ledger table, but not owed today (design F8 of the adjustments).</summary>
    private const decimal NotYetBegunDifference = 40_000m;

    private static TenantNoticeDesk DeskOver(DeskWorld world) => new(world.NewContext(), new AgencyClock(), DeskWorld.Terms);

    /// <summary>What a seed wrote: the account, its contract and the tenant's name as the desk prints it.</summary>
    private sealed record Seeded(Guid AccountId, Guid ContractId);

    /// <summary>
    /// A contract with a lessor and a unit, its current tenant holding <paramref name="tenantEmail"/>, and its empty account.
    /// <paramref name="shape"/> may change the contract before it is saved.
    /// </summary>
    private static async Task<Seeded> SeedAsync(
        DeskWorld world,
        string? tenantEmail,
        string tenantLastName = "Perez",
        DateOnly? start = null,
        DateOnly? cutoverDate = null,
        Action<Contract, ContractAccount, InmobiliariaDbContext>? shape = null)
    {
        await using var context = world.NewContext();

        var unit = new PropertyUnit(
            Guid.NewGuid(), new Address("Bv. Belgrano", Guid.NewGuid().ToString("N")[..4], "Sunchales", "Santa Fe", "2322"));
        var tenant = new Party(Guid.NewGuid(), "Maria", tenantLastName, email: tenantEmail);
        var lessor = new Party(Guid.NewGuid(), "Juan", "Gomez", email: "lessor@example.com");
        var from = start ?? DeskWorld.OldestPeriod;

        var contract = new Contract(
            Guid.NewGuid(), from, from.AddYears(2).AddDays(-1), 450_000m, [new UnitShare(unit.Id, 100m)], 8m,
            Contract.DefaultDueDay, cutoverDate);
        contract.AssignParty(tenant, PartyRole.Tenant);
        contract.AssignParty(lessor, PartyRole.Lessor);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        context.Units.Add(unit);
        context.Parties.AddRange(tenant, lessor);
        shape?.Invoke(contract, account, context);
        context.Contracts.Add(contract);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return new Seeded(account.Id, contract.Id);
    }

    /// <summary>The cash desk's figure for a payment received today, through its public port.</summary>
    private static async Task<decimal> DeskFigureAsync(DeskWorld world, Guid accountId) =>
        (await world.NewDesk().PreviewAsync(new CollectionRequest(accountId, null, null, 0m))).PreviousAmountOwed;

    private static Task<long> SentRowsAsync(DeskWorld world) => world.CountAsync("SELECT count(*) FROM tenant_notices");

    // ---- Test 1 ---------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ACreatedContractThatOwes_WithATenantEmail_IsListedWithTheDeskFigure()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");

        var board = await DeskOver(world).ReadAsync();

        var notice = Assert.Single(board.ToSend);
        Assert.Equal(seeded.AccountId, notice.AccountId);
        Assert.Equal(seeded.ContractId, notice.ContractId);
        Assert.Equal("maria.perez@example.com", notice.Recipient);
        Assert.Equal(Today, notice.FiguresOn);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1), notice.NoticeMonth);
        Assert.True(notice.Total > 0m);
        Assert.Equal(await DeskFigureAsync(world, seeded.AccountId), notice.Total);

        // Days overdue as the Mora list counts them, read from the Mora list itself.
        await using var context = world.NewContext();
        var mora = Assert.Single(
            await new MoraWorklistQuery(context, new AccountMaterialiser(context)).GetAccountsInMoraAsync(Today, DeskWorld.Terms));
        Assert.True(notice.DaysOverdue > 0);
        Assert.Equal(mora.DaysOverdue, notice.DaysOverdue);

        Assert.Empty(board.NoEmail);
        Assert.Empty(board.AlreadySent);
    }

    // ---- Test 2 ---------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task OnlyAnEndedContractThatOwes_IsListed_AmongOneOwingNothingOneLoadedAndOneCancelledAsLoadedByMistake()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);

        // Starts next month: nothing has fallen due.
        var owesNothing = await SeedAsync(world, "a@example.com", "Alvarez", start: DeskWorld.FirstOfMonth(Today).AddMonths(1));

        // Predates the system: its cutover date is set, and it owes from it.
        var loaded = await SeedAsync(world, "b@example.com", "Benitez", cutoverDate: DeskWorld.OldestPeriod);

        // Its rent was written, then it was cancelled as loaded by mistake without the reversal: it still shows a debt, so
        // only the loaded-by-mistake rule keeps it off the board.
        var mistaken = await SeedAsync(world, "c@example.com", "Castro");
        await world.MaterialiseAsync(mistaken.AccountId, Today);
        await using (var context = world.NewContext())
        {
            var contract = await context.Contracts.SingleAsync(c => c.Id == mistaken.ContractId);
            contract.Cancel(Today);
            await context.SaveChangesAsync();
        }

        var ended = await SeedAsync(world, "d@example.com", "Diaz");
        await world.MaterialiseAsync(ended.AccountId, Today);
        await using (var context = world.NewContext())
        {
            var contract = await context.Contracts.SingleAsync(c => c.Id == ended.ContractId);
            contract.End(EndReason.Expiry, Today);
            await context.SaveChangesAsync();
        }

        Assert.Equal(0m, await DeskFigureAsync(world, owesNothing.AccountId));
        Assert.True(await DeskFigureAsync(world, loaded.AccountId) > 0m);
        Assert.True(await DeskFigureAsync(world, mistaken.AccountId) > 0m);

        var board = await DeskOver(world).ReadAsync();

        var notice = Assert.Single(board.ToSend);
        Assert.Equal(ended.AccountId, notice.AccountId);
        Assert.Equal(await DeskFigureAsync(world, ended.AccountId), notice.Total);
        Assert.Empty(board.NoEmail);
    }

    // ---- Test 3 ---------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("maria.perez")]
    [InlineData("Maria <maria@example.com>")]
    public async Task ATenantWithNoUsableEmail_IsInTheNoEmailList_WithTheirName_AndNotAmongTheNotices(string? email)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, email);

        var board = await DeskOver(world).ReadAsync();

        Assert.Empty(board.ToSend);
        var unreachable = Assert.Single(board.NoEmail);
        Assert.Equal("Perez, Maria", unreachable.Tenant);
        Assert.Equal(seeded.ContractId.ToString("N")[..8], unreachable.ContractShortId);
    }

    // ---- Test 4 ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reading writes no sent row. The mail port half of spec Test 4 holds by construction in this slice: the desk is given no
    /// sender to call, and Phase 2 re-asserts it with a counting fake once sending takes one.
    /// </summary>
    [SkippableFact]
    public async Task ReadingTheBoard_WritesNoSentRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "maria.perez@example.com");

        var first = await DeskOver(world).ReadAsync();
        var second = await DeskOver(world).ReadAsync();

        Assert.Single(first.ToSend);
        Assert.Equal(first.ToSend, second.ToSend);
        Assert.Equal(0L, await SentRowsAsync(world));
    }

    // ---- Test 5 ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Accounts covering every movement kind a listed contract can hold (an opening balance exists only on a loaded contract,
    /// which is never listed), one of them frozen: each total is the desk's preview, and each recargo is the total less what
    /// the ledger holds, counted by the database, so zero on the frozen one.
    /// </summary>
    [SkippableFact]
    public async Task EveryNotice_StatesTheDesksFigure_AndTheRecargoInsideIt_FrozenIncluded()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);

        // A rent paid with its recargo (Payment, RecargoCharged), an instalment, a correction, a difference on a begun month,
        // and one on a month not yet begun, which the desk does not ask for today and so is no part of the ledger either.
        var everyKind = await SeedAsync(
            world, "a@example.com", "Alvarez", shape: (contract, account, _) =>
                contract.ChangeContractualFeePlan(new ContractualFeePlan(90_000m, 3), account));
        await world.MaterialiseAsync(everyKind.AccountId, Today);
        await DeskWorld.PayAsync(world.NewDesk(), everyKind.AccountId);
        await AppendAsync(world, everyKind.AccountId, account =>
        {
            var rent = account.Movements.Where(m => m.Kind == MovementKind.RentAccrual).MinBy(m => m.Period)!;
            account.Append(AccountMovement.Correction(Guid.NewGuid(), -1_000m, Today, rent.Id, rent.Period));
            account.Append(AccountMovement.AdjustmentDifference(
                Guid.NewGuid(), 30_000m, DeskWorld.OldestPeriod.AddMonths(1), DeskWorld.OldestPeriod.AddMonths(1).AddDays(9)));
            account.Append(AccountMovement.AdjustmentDifference(
                Guid.NewGuid(), NotYetBegunDifference, DeskWorld.FirstOfMonth(Today).AddMonths(1), Today));
        });

        var frozen = await SeedAsync(world, "b@example.com", "Benitez");
        await world.MaterialiseAsync(frozen.AccountId, Today);
        await AppendAsync(world, frozen.AccountId, account =>
            account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 75_000m, Today.AddDays(-1))));

        var board = await DeskOver(world).ReadAsync();

        foreach (var (seeded, notYetBegun) in new[] { (everyKind, NotYetBegunDifference), (frozen, 0m) })
        {
            var notice = Assert.Single(board.ToSend, n => n.AccountId == seeded.AccountId);
            var everyRow = await world.CountAsync(
                "SELECT sum(amount * 100)::bigint FROM account_movements WHERE account_id = @a", ("a", seeded.AccountId));
            var ledger = everyRow / 100m - notYetBegun;

            Assert.Equal(await DeskFigureAsync(world, seeded.AccountId), notice.Total);
            Assert.Equal(notice.Total - ledger, notice.Recargo);
        }

        Assert.True(Assert.Single(board.ToSend, n => n.AccountId == everyKind.AccountId).Recargo > 0m);
        Assert.Equal(0m, Assert.Single(board.ToSend, n => n.AccountId == frozen.AccountId).Recargo);
    }

    private static async Task AppendAsync(DeskWorld world, Guid accountId, Action<ContractAccount> append)
    {
        await using var context = world.NewContext();
        var account = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == accountId);
        append(account);
        await context.SaveChangesAsync();
    }

    // ---- Test 7 ---------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ANotice_GoesToTheCurrentTenantOnly_AndNamesThemAsTheDesksSearchDoes()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);

        // The first tenant is replaced before any movement; a codeudor and the lessor carry emails of their own.
        var seeded = await SeedAsync(world, "first.tenant@example.com", "Primero", shape: (contract, account, context) =>
        {
            var first = contract.Parties.Single(p => p.Role == PartyRole.Tenant).PartyId;
            var second = new Party(Guid.NewGuid(), "Sofia", "Segundo", email: "second.tenant@example.com");
            var codeudor = new Party(Guid.NewGuid(), "Carlos", "Codeudor", email: "codeudor@example.com");
            context.Parties.AddRange(second, codeudor);

            contract.RemoveParty(first, PartyRole.Tenant, account, DateTimeOffset.UtcNow, replacementFollows: true);
            contract.AssignParty(second, PartyRole.Tenant);
            contract.AssignParty(codeudor, PartyRole.Codebtor);
        });

        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);

        Assert.Equal("second.tenant@example.com", notice.Recipient);

        var listing = await new AccountDirectory(world.NewContext(), new AgencyClock()).FindAsync(seeded.AccountId);
        Assert.NotNull(listing);
        Assert.Equal("Segundo, Sofia", notice.Tenant);
        Assert.Equal(listing.Tenants, notice.Tenant);
        Assert.Equal(listing.Units, notice.Units);
        Assert.Equal(listing.ContractShortId, notice.ContractShortId);
    }

    // ---- Already sent ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A notice already sent this month is shown as sent, with who and when, and not offered again; last month's sent row hides
    /// nothing this month (spec Requirement 1; Requirement 4's "next month" scenario).
    /// </summary>
    [SkippableFact]
    public async Task ANoticeSentThisMonth_IsShownAsSent_AndLastMonthsHidesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var sentNow = await SeedAsync(world, "a@example.com", "Alvarez");
        var sentLastMonth = await SeedAsync(world, "b@example.com", "Benitez");

        var month = TenantNotice.MonthOf(Today);
        await InsertSentAsync(world, sentNow.ContractId, month);
        await InsertSentAsync(world, sentLastMonth.ContractId, month.AddMonths(-1));

        var board = await DeskOver(world).ReadAsync();

        var offered = Assert.Single(board.ToSend);
        Assert.Equal(sentLastMonth.AccountId, offered.AccountId);

        var sent = Assert.Single(board.AlreadySent);
        Assert.Equal(sentNow.ContractId.ToString("N")[..8], sent.ContractShortId);
        Assert.Equal("Alvarez, Maria", sent.Tenant);
        Assert.Equal("inmobiliaria_test", sent.RecordedBy);
        Assert.True(sent.RecordedAt > DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Empty(board.NoEmail);
    }

    private static async Task InsertSentAsync(DeskWorld world, Guid contractId, DateOnly month)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(
            connection,
            """
            INSERT INTO tenant_notices (id, contract_id, kind, notice_month, recipient, figures_on, total, recargo)
            VALUES (gen_random_uuid(), @contract, 'amount_owed', @month, 'x@example.com', @month, 1, 0)
            """,
            ("contract", contractId), ("month", month));
    }
}
