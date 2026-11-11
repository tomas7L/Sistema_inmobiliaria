using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The mora worklist against a real engine. The test that matters most here is the one for an
/// account nobody has ever read: with no scheduler, that account holds no accrual rows at all, and
/// a worklist that merely queried movements would report the tenant three months behind as clear.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MoraWorklistQueryTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private readonly PostgresFixture _fixture;

    public MoraWorklistQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static MoraWorklistQuery QueryOver(InmobiliariaDbContext context) =>
        new(context, new AccountMaterialiser(context));

    /// <summary>
    /// A contract and its account, and NOTHING ELSE. No movement is written and nothing reads the
    /// account, which is exactly the state a real contract sits in between go-live and the first
    /// time somebody opens it.
    /// </summary>
    private static async Task<(Contract Contract, ContractAccount Account)> SeedUnreadAsync(
        InmobiliariaDbContext context,
        DateOnly startDate,
        decimal monthlyRent = 500_000m,
        int dueDay = 10)
    {
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            startDate,
            startDate.AddYears(2),
            monthlyRent: monthlyRent,
            unitShares: [new UnitShare(unit.Id, 100m)],
            dueDay: dueDay);
        context.Contracts.Add(contract);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);

        await context.SaveChangesAsync();

        return (contract, account);
    }

    /// <summary>
    /// Spec test 20, and the change's highest-ranked risk: an account NOBODY HAS EVER READ, with
    /// periods overdue, appears on the worklist with the right figures.
    ///
    /// The account is seeded with zero movements and is never materialised by the test itself.
    /// If the query only read rows, this would return nothing at all — and the account three
    /// months behind would be the one that never gets chased.
    /// </summary>
    [SkippableFact]
    public async Task AnAccountNobodyHasEverRead_AppearsWithItsRealFigures()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        // Nothing has written a single movement.
        Assert.Empty((await context.ContractAccounts
            .AsNoTracking().Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id)).Movements);

        var asOf = new DateOnly(2026, 3, 20);
        var worklist = await QueryOver(context).GetAccountsInMoraAsync(asOf, LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);

        Assert.Equal(contract.Id, row.ContractId);
        Assert.Equal(new DateOnly(2026, 1, 1), row.OldestUnpaidPeriod);
        Assert.Equal(new DateOnly(2026, 1, 10), row.OldestUnpaidDueDate);

        // January, February and March all fell due: 1,500,000 written.
        Assert.Equal(1_500_000m, row.LedgerBalance);

        // 69 days since January's tenth, and the later periods keep their own clocks.
        Assert.Equal(69, row.DaysOverdue);
        Assert.True(row.RecargoAccrued > 0m);
        Assert.Equal(row.LedgerBalance + row.RecargoAccrued, row.AmountOwed);
        Assert.Null(row.RecargoFrozenOn);
    }

    /// <summary>
    /// And the rows really were written, not merely computed for the answer. Materialisation is a
    /// write: the periods exist on disk afterwards, which is what makes the next read cheap and
    /// the ledger reconcilable.
    /// </summary>
    [SkippableFact]
    public async Task QueryingTheWorklist_LeavesTheMaterialisedPeriodsOnDisk()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 3, 20), LeaseTerms);

        await using var fresh = _fixture.CreateDbContext();
        var movements = (await fresh.ContractAccounts
            .AsNoTracking().Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id)).Movements;

        Assert.Equal(3, movements.Count);
        Assert.All(movements, m => Assert.Equal(MovementKind.RentAccrual, m.Kind));
    }

    /// <summary>
    /// Spec test 20: three accounts overdue by 5, 38 and 200 days all appear, each carrying its
    /// own days overdue, amount owed and recargo accrued.
    /// </summary>
    [SkippableFact]
    public async Task ThreeAccountsOverdueByDifferentAmounts_AllAppearWithTheirOwnFigures()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var asOf = new DateOnly(2026, 9, 1);

        var five = await SeedOverdueByAsync(context, asOf, days: 5);
        var thirtyEight = await SeedOverdueByAsync(context, asOf, days: 38);
        var twoHundred = await SeedOverdueByAsync(context, asOf, days: 200);

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(asOf, LeaseTerms);

        var rows = new[] { five, thirtyEight, twoHundred }
            .Select(seed => Assert.Single(worklist, r => r.AccountId == seed.Account.Id))
            .ToList();

        Assert.Equal([5, 38, 200], rows.Select(r => r.DaysOverdue).Order());

        // Every row carries all three figures, and the surcharge grows with the days.
        Assert.All(rows, row =>
        {
            Assert.True(row.DaysOverdue > 0);
            Assert.True(row.AmountOwed > 0m);
            Assert.True(row.RecargoAccrued > 0m);
        });

        var byDays = rows.OrderBy(r => r.DaysOverdue).ToList();
        Assert.True(byDays[0].RecargoAccrued < byDays[1].RecargoAccrued);
        Assert.True(byDays[1].RecargoAccrued < byDays[2].RecargoAccrued);

        // Oldest debt first is the order somebody chasing payments works in.
        var ours = worklist.Where(r => rows.Any(x => x.AccountId == r.AccountId)).ToList();
        Assert.Equal([200, 38, 5], ours.Select(r => r.DaysOverdue));
    }

    /// <summary>
    /// A contract whose OLDEST unpaid period fell due exactly <paramref name="days"/> before
    /// <paramref name="asOf"/>.
    ///
    /// The due day is derived rather than fixed at 10, because a fixed due day can only ever
    /// produce the gaps that happen to fall on a tenth — the first version of this helper asked
    /// for 5 days and would have got 22. The contract starts on the first of that month, so its
    /// own due date is the first period it owes.
    /// </summary>
    private static Task<(Contract Contract, ContractAccount Account)> SeedOverdueByAsync(
        InmobiliariaDbContext context, DateOnly asOf, int days)
    {
        var dueDate = asOf.AddDays(-days);

        return SeedUnreadAsync(
            context,
            startDate: new DateOnly(dueDate.Year, dueDate.Month, 1),
            dueDay: dueDate.Day);
    }

    /// <summary>
    /// Spec test 21: an account with no unpaid period does not appear. Here the single period is
    /// settled, so nothing is owing — and the account must be absent rather than present with a
    /// zero.
    /// </summary>
    [SkippableFact]
    public async Task AnAccountWithNoUnpaidPeriod_DoesNotAppear()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        // Materialise January, then settle it.
        await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 1, 31));

        var tracked = await context.ContractAccounts
            .Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id);

        var january = tracked.Movements.Single();
        tracked.Append(AccountMovement.Correction(
            Guid.NewGuid(), -january.Amount, new DateOnly(2026, 1, 20),
            january.Id, january.Period));

        await context.SaveChangesAsync();

        var worklist = await QueryOver(context)
            .GetAccountsInMoraAsync(new DateOnly(2026, 1, 31), LeaseTerms);

        Assert.DoesNotContain(worklist, r => r.AccountId == account.Id);
    }

    /// <summary>
    /// A brand new contract whose first period has not reached its due day is not in mora. Nothing
    /// is late yet, and a worklist that listed it would train its reader to ignore it.
    /// </summary>
    [SkippableFact]
    public async Task AContractWhoseFirstPeriodHasNotFallenDue_DoesNotAppear()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        var worklist = await QueryOver(context)
            .GetAccountsInMoraAsync(new DateOnly(2026, 1, 5), LeaseTerms);

        Assert.DoesNotContain(worklist, r => r.AccountId == account.Id);
    }

    /// <summary>
    /// Spec test 22: an account frozen by a novación reports its FROZEN surcharge, not one that
    /// kept growing.
    ///
    /// And not zero either, which is the trap. Once the frozen amount became a movement it joined
    /// the ledger balance, so the difference between the two figures is no longer the surcharge —
    /// the movement is the only place the real number still is.
    /// </summary>
    [SkippableFact]
    public async Task AFrozenAccount_ReportsTheFrozenSurcharge()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 1, 31));

        var tracked = await context.ContractAccounts
            .Include(a => a.Movements)
            .SingleAsync(a => a.Id == account.Id);

        // Due 10 January, plan signed 1 February: 22 days at 2% of 500,000.
        var signedOn = new DateOnly(2026, 2, 1);
        var accrued = tracked.AmountOwed(signedOn, LeaseTerms) - tracked.LedgerBalance(signedOn);
        Assert.Equal(220_000m, accrued);

        tracked.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), accrued, signedOn));
        await context.SaveChangesAsync();

        // Asked a month after the signing, when an unfrozen clock would have reached 500,000.
        var worklist = await QueryOver(context)
            .GetAccountsInMoraAsync(new DateOnly(2026, 3, 1), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);

        Assert.Equal(220_000m, row.RecargoAccrued);
        Assert.Equal(signedOn, row.RecargoFrozenOn);
    }

    /// <summary>
    /// Spec tests 31 and 32: the figures cover rent, honorarios contractuales instalments and
    /// recargo — and nothing else. No tasa municipal, seguro or "Otros Conceptos" amount can enter
    /// them, because no such movement kind exists to carry one: those are typed when a receipt is
    /// closed and never accrue.
    ///
    /// Asserted over the model, so adding such a kind fails here rather than quietly inflating
    /// every balance in the system.
    ///
    /// Payment and RecargoCharged are ledger kinds, not receipt-only concepts: both are written
    /// to the ledger in the same transaction as the recibo (cobranza-y-recibos spec Decision 5),
    /// as a credit and a debit that move the balance, so they belong in these figures. So does
    /// AdjustmentDifference: a debit the operator chose to charge, which the balance must cover
    /// (gestion-de-alquileres spec Requirement 19).
    /// </summary>
    [Fact]
    public void NoReceiptOnlyConcept_CanEnterTheseFigures()
    {
        var kinds = Enum.GetNames<MovementKind>();

        Assert.Equal(
            ["AdjustmentDifference", "ContractualFeeInstalment", "Correction", "OpeningBalance", "Payment", "RecargoCharged", "RecargoFrozen", "RentAccrual"],
            kinds.Order());

        foreach (var forbidden in new[] { "Tasa", "Municipal", "Seguro", "Insurance", "Otros", "Other" })
        {
            Assert.DoesNotContain(
                kinds,
                kind => kind.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The two figures stay named apart on the worklist row, and the surcharge is reported
    /// separately rather than folded into one number a reader would have to guess about.
    /// AmountOwed already includes RecargoAccrued, so a row exposing only a single "balance" would
    /// be the bug this requirement exists to prevent.
    /// </summary>
    [Fact]
    public void TheWorklistRow_NamesBothFiguresApart()
    {
        var properties = typeof(MoraAccount).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains(nameof(MoraAccount.LedgerBalance), properties);
        Assert.Contains(nameof(MoraAccount.AmountOwed), properties);
        Assert.Contains(nameof(MoraAccount.RecargoAccrued), properties);

        // Nothing called just "Balance": that name is what hides which figure is being shown.
        Assert.DoesNotContain("Balance", properties);
    }

    [SkippableFact]
    public async Task TheWorklist_RequiresTerms()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 1, 31), null!));
    }

    /// <summary>
    /// An ended contract still appears while its account owes: the account outlives the contract,
    /// and a debt does not stop being chaseable because the lease finished.
    /// </summary>
    [SkippableFact]
    public async Task AnEndedContractStillOwing_StillAppears()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 2, 20));
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context)
            .GetAccountsInMoraAsync(new DateOnly(2026, 6, 30), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);

        // January and February accrued; nothing after the end date did.
        Assert.Equal(1_000_000m, row.LedgerBalance);
        Assert.Equal(new DateOnly(2026, 1, 1), row.OldestUnpaidPeriod);
    }

    // ---- mora change: spec Requirements 1 and 2 ----

    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly MoraCutover = new(2026, 9, 30);

    /// <summary>
    /// A pre-existing contract that went live on <see cref="MoraCutover"/> owing August and September,
    /// each typed as a monthly opening balance, exactly as go-live loads a debt month by month. Nothing
    /// after the cutover is written: the worklist's own materialisation brings October and November.
    /// </summary>
    private static async Task<(Contract Contract, ContractAccount Account, AccountMovement[] Opening)> SeedPreExistingAsync(
        InmobiliariaDbContext context, int dueDay = 10)
    {
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2028, 6, 1),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(unit.Id, 100m)],
            dueDay: dueDay,
            cutoverDate: MoraCutover);
        context.Contracts.Add(contract);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var opening = new[]
        {
            AccountMovement.OpeningBalance(Guid.NewGuid(), 480_000m, MoraCutover, August),
            AccountMovement.OpeningBalance(Guid.NewGuid(), 480_000m, MoraCutover, September),
        };
        foreach (var movement in opening)
        {
            account.Append(movement);
        }

        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return (contract, account, opening);
    }

    private static async Task SettleAsync(InmobiliariaDbContext context, Guid accountId, DateOnly on, params Guid[] movementIds)
    {
        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == accountId);
        foreach (var id in movementIds)
        {
            var movement = tracked.Movements.Single(m => m.Id == id);
            tracked.Append(AccountMovement.Correction(Guid.NewGuid(), -movement.Amount, on, movement.Id, movement.Period));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// mora spec Test 1, and the reason the change exists (spec Finding F1): an account whose oldest
    /// unpaid periods are opening-balance months, followed by unpaid rent, appears with August as its
    /// oldest unpaid period. Before the fix the row was built only from a rent accrual on that period,
    /// which an opening-balance month never has, so the account most behind dropped off entirely.
    /// </summary>
    [SkippableFact]
    public async Task OpeningBalanceMonthsAtTheFrontOfTheDebt_StillPutTheAccountOnTheList()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account, _) = await SeedPreExistingAsync(context);

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 11, 20), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);
        Assert.Equal(August, row.OldestUnpaidPeriod);
    }

    /// <summary>
    /// mora spec Test 4: an opening-balance month falls due on that month's due day on the contract,
    /// computed by the rule rent uses, never from the day the balance was typed.
    /// </summary>
    [SkippableTheory]
    [InlineData(10, 102)]
    [InlineData(5, 107)]
    public async Task AnOpeningBalanceMonth_CountsFromItsOwnDueDate(int dueDay, int expectedDays)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account, _) = await SeedPreExistingAsync(context, dueDay);

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 11, 20), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 8, dueDay), row.OldestUnpaidDueDate);
        Assert.Equal(expectedDays, row.DaysOverdue);
    }

    /// <summary>
    /// mora spec Test 3: opening-balance months that are all paid, with nothing else unpaid, do not keep
    /// the account on the list. Read on 5 October, before October's rent falls due.
    /// </summary>
    [SkippableFact]
    public async Task PaidOpeningBalanceMonths_DoNotKeepTheAccountOnTheList()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account, opening) = await SeedPreExistingAsync(context);
        await SettleAsync(context, account.Id, new DateOnly(2026, 10, 1), opening.Select(m => m.Id).ToArray());

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 10, 5), LeaseTerms);

        Assert.DoesNotContain(worklist, r => r.AccountId == account.Id);
    }

    /// <summary>
    /// mora spec Test 2: an ended contract whose only unpaid charge is a difference on its last period
    /// appears, counted from that difference's due date.
    /// </summary>
    [SkippableFact]
    public async Task AnEndedContractOwingOnlyADifference_Appears()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 2, 20));
        await context.SaveChangesAsync();

        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 2, 28));
        var rents = (await context.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == account.Id))
            .Movements.Select(m => m.Id).ToArray();
        await SettleAsync(context, account.Id, new DateOnly(2026, 2, 25), rents);

        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account.Id);
        tracked.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 40_000m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 10)));
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 6, 30), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 2, 1), row.OldestUnpaidPeriod);
        Assert.Equal(new DateOnly(2026, 2, 10), row.OldestUnpaidDueDate);
        Assert.Equal(40_000m, row.LedgerBalance);
    }

    /// <summary>
    /// The half of spec Finding F1 that really drops an account: a difference on a period whose charge
    /// was an opening balance, already paid. That period holds no rent accrual at all, so the row could
    /// not be built. The difference's OWN date is its due date (spec Requirement 2): here it was charged when the
    /// contract still fell due on the 12th, and the contract's due day is the 10th today, so the two rules differ.
    /// </summary>
    [SkippableFact]
    public async Task ADifferenceOnAPaidOpeningBalanceMonth_Appears()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account, opening) = await SeedPreExistingAsync(context);
        await SettleAsync(context, account.Id, new DateOnly(2026, 10, 1), opening.Select(m => m.Id).ToArray());

        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account.Id);
        tracked.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 30_000m, September, new DateOnly(2026, 9, 12)));
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 10, 5), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);
        Assert.Equal(September, row.OldestUnpaidPeriod);
        Assert.Equal(new DateOnly(2026, 9, 12), row.OldestUnpaidDueDate);
        Assert.Equal(23, row.DaysOverdue);
    }

    /// <summary>
    /// mora spec Test 5: on the due date itself the account is not yet in mora; the next day it is,
    /// with one day.
    /// </summary>
    [SkippableFact]
    public async Task TheDueDateItself_IsNotMora_TheNextDayIs()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));

        var onTheDay = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 1, 10), LeaseTerms);
        Assert.DoesNotContain(onTheDay, r => r.AccountId == account.Id);

        var nextDay = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 1, 11), LeaseTerms);
        Assert.Equal(1, Assert.Single(nextDay, r => r.AccountId == account.Id).DaysOverdue);
    }

    // ---- mora change: spec Requirements 3 and 4, and the order (design D4) ----

    /// <summary>An unread contract whose tenant and lessor are named, so its row has text to compare.</summary>
    private static async Task<(Contract Contract, ContractAccount Account)> SeedNamedAsync(
        InmobiliariaDbContext context, string tenantLast, DateOnly startDate, int dueDay = 10)
    {
        var (contract, account) = await SeedUnreadAsync(context, startDate, dueDay: dueDay);

        var tracked = await context.Contracts.SingleAsync(c => c.Id == contract.Id);
        var tenant = new Party(Guid.NewGuid(), "Maria", tenantLast);
        var lessor = new Party(Guid.NewGuid(), "Juan", "Lessor");
        context.Parties.AddRange(tenant, lessor);
        tracked.AssignParty(tenant, PartyRole.Tenant);
        tracked.AssignParty(lessor, PartyRole.Lessor);
        await context.SaveChangesAsync();

        return (tracked, account);
    }

    /// <summary>
    /// mora spec Test 8: a row names the tenant, the units and the short id exactly as the cash desk's directory
    /// shows that account, and carries the contract's status, an ended one included.
    /// </summary>
    [SkippableFact]
    public async Task ARow_NamesTheDebtorAsTheCashDeskDoes()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, active) = await SeedNamedAsync(context, "Prueba", new DateOnly(2026, 1, 1));
        var (endedContract, ended) = await SeedNamedAsync(context, "Terminada", new DateOnly(2026, 1, 1));
        endedContract.End(EndReason.MutualAgreement, new DateOnly(2026, 2, 20));
        await context.SaveChangesAsync();

        var asOf = new DateOnly(2026, 3, 20);
        var worklist = await QueryOver(context).GetAccountsInMoraAsync(asOf, LeaseTerms);
        var directory = new AccountDirectory(context, new FixedClock(asOf));

        foreach (var accountId in new[] { active.Id, ended.Id })
        {
            var row = Assert.Single(worklist, r => r.AccountId == accountId);
            var listing = await directory.FindAsync(accountId);
            Assert.NotNull(listing);
            Assert.Equal(
                (listing.ContractShortId, listing.Tenants, listing.Units, listing.Status),
                (row.ContractShortId, row.Tenants, row.Units, row.Status));
        }

        Assert.Equal("Prueba, Maria", Assert.Single(worklist, r => r.AccountId == active.Id).Tenants);
        Assert.Equal(ContractStatus.Ended, Assert.Single(worklist, r => r.AccountId == ended.Id).Status);
    }

    /// <summary>
    /// mora spec Test 10, order half (design F7): most days overdue first, then the tenant's name folded as the
    /// desk's search folds it, so "Álvarez" sorts before "Beta" and both before "Zeta".
    /// </summary>
    [SkippableFact]
    public async Task Rows_AreOrderedByDaysOverdue_ThenByTenantName()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var zeta = await SeedNamedAsync(context, "Zeta", new DateOnly(2026, 3, 1), dueDay: 7);
        var alvarez = await SeedNamedAsync(context, "Álvarez", new DateOnly(2026, 3, 1), dueDay: 7);
        var beta = await SeedNamedAsync(context, "Beta", new DateOnly(2026, 3, 1), dueDay: 7);
        var older = await SeedNamedAsync(context, "Older", new DateOnly(2026, 2, 1), dueDay: 7);

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 3, 20), LeaseTerms);

        var ours = new[] { zeta.Account.Id, alvarez.Account.Id, beta.Account.Id, older.Account.Id };
        Assert.Equal(
            [older.Account.Id, alvarez.Account.Id, beta.Account.Id, zeta.Account.Id],
            worklist.Where(r => ours.Contains(r.AccountId)).Select(r => r.AccountId));
    }

    /// <summary>
    /// mora spec Test 6: for accounts covering every movement kind, the row's ledger balance and amount owed are the
    /// cash desk's own figures for that account and day (<see cref="ContractAccount.AmountOwedForPayment"/>), and an
    /// opening-balance month adds no recargo: the row's recargo is October's and November's only.
    /// </summary>
    [SkippableFact]
    public async Task EveryRowFigure_IsTheCashDesksFigure_AndAnOpeningMonthAddsNoRecargo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        var asOf = new DateOnly(2026, 11, 20);
        var october = new DateOnly(2026, 10, 1);
        var november = new DateOnly(2026, 11, 1);

        await using var context = _fixture.CreateDbContext();

        // Opening balances, rent, a part payment, a charged recargo, a difference and a correction.
        var (_, mixed, opening) = await SeedPreExistingAsync(context);
        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(mixed.Id, asOf);
        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == mixed.Id);
        tracked.Append(AccountMovement.Payment(Guid.NewGuid(), -200_000m, october, new DateOnly(2026, 10, 15)));
        tracked.Append(AccountMovement.RecargoCharged(Guid.NewGuid(), 10_000m, october, new DateOnly(2026, 10, 15)));
        tracked.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 25_000m, october, new DateOnly(2026, 10, 10)));
        tracked.Append(AccountMovement.Correction(Guid.NewGuid(), -30_000m, new DateOnly(2026, 10, 1), opening[1].Id, September));
        await context.SaveChangesAsync();

        // Honorarios contractuales instalments riding with the rent.
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);
        var withPlan = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2028, 8, 1), monthlyRent: 400_000m,
            unitShares: [new UnitShare(unit.Id, 100m)], contractualFeePlan: new ContractualFeePlan(90_000m, 3));
        context.Contracts.Add(withPlan);
        var planned = new ContractAccount(Guid.NewGuid(), withPlan.Id);
        context.ContractAccounts.Add(planned);
        await context.SaveChangesAsync();

        // A frozen account.
        var (_, frozenAccount) = await SeedUnreadAsync(context, new DateOnly(2026, 8, 1));
        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(frozenAccount.Id, new DateOnly(2026, 8, 31));
        var frozen = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == frozenAccount.Id);
        frozen.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 50_000m, new DateOnly(2026, 9, 1)));
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(asOf, LeaseTerms);

        await using var fresh = _fixture.CreateDbContext();
        var kinds = new HashSet<MovementKind>();
        foreach (var accountId in new[] { mixed.Id, planned.Id, frozenAccount.Id })
        {
            var account = await fresh.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == accountId);
            kinds.UnionWith(account.Movements.Select(m => m.Kind));

            var row = Assert.Single(worklist, r => r.AccountId == accountId);
            Assert.Equal(account.LedgerBalance(asOf), row.LedgerBalance);
            Assert.Equal(account.AmountOwedForPayment(asOf, LeaseTerms), row.AmountOwed);
        }

        Assert.Equal(Enum.GetValues<MovementKind>().Order(), kinds.Order());

        var mixedAccount = await fresh.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == mixed.Id);
        var mixedRow = Assert.Single(worklist, r => r.AccountId == mixed.Id);
        Assert.Equal(0m, mixedAccount.RecargoProjectedForPayment(August, asOf, LeaseTerms));
        Assert.Equal(0m, mixedAccount.RecargoProjectedForPayment(September, asOf, LeaseTerms));
        Assert.Equal(
            mixedAccount.RecargoProjectedForPayment(october, asOf, LeaseTerms) + mixedAccount.RecargoProjectedForPayment(november, asOf, LeaseTerms),
            mixedRow.RecargoAccrued);
        Assert.True(mixedRow.RecargoAccrued > 0m);
        Assert.Equal(50_000m, Assert.Single(worklist, r => r.AccountId == frozenAccount.Id).RecargoAccrued);
    }

    // ---- mora task 2.0: follow-ups from the M1 review ----

    /// <summary>
    /// Review finding R3-difference-due-date-nondeterministic: a period with no rent and two differences is dated by the EARLIEST
    /// one, whatever order the movements load in. The later one is appended first, so a "first loaded" rule reads the 14th.
    /// </summary>
    [SkippableFact]
    public async Task APeriodWithTwoDifferencesAndNoRent_IsDatedByTheEarliest()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account, opening) = await SeedPreExistingAsync(context);
        await SettleAsync(context, account.Id, new DateOnly(2026, 10, 1), opening.Select(m => m.Id).ToArray());

        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account.Id);
        tracked.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 20_000m, September, new DateOnly(2026, 9, 14)));
        await context.SaveChangesAsync();
        tracked.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 10_000m, September, new DateOnly(2026, 9, 12)));
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 10, 5), LeaseTerms);

        var row = Assert.Single(worklist, r => r.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 9, 12), row.OldestUnpaidDueDate);
        Assert.Equal(23, row.DaysOverdue);
    }

    /// <summary>
    /// Review finding R3-mistaken-contract-row-unopenable, pinned: a contract cancelled as loaded by mistake, with periods it
    /// owed, never appears, because cancelling nets every period to zero and nothing accrues afterwards (design F8). A row
    /// for it could never be opened at the desk, which hides that contract.
    /// </summary>
    [SkippableFact]
    public async Task AContractCancelledAsLoadedByMistake_NeverAppears()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, account) = await SeedUnreadAsync(context, new DateOnly(2026, 1, 1));
        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 3, 20));

        var today = new DateOnly(2026, 3, 20);
        var tracked = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account.Id);
        var plan = ContractCancellation.Plan(tracked, contract, [], "Cargado dos veces", today);
        Assert.Null(plan.Refusal);
        foreach (var correction in plan.Corrections)
        {
            tracked.Append(correction);
        }

        contract.Cancel(today);
        await context.SaveChangesAsync();

        var worklist = await QueryOver(context).GetAccountsInMoraAsync(new DateOnly(2026, 6, 30), LeaseTerms);

        Assert.DoesNotContain(worklist, r => r.AccountId == account.Id);
    }
}
