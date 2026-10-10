using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
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
    /// </summary>
    [Fact]
    public void NoReceiptOnlyConcept_CanEnterTheseFigures()
    {
        var kinds = Enum.GetNames<MovementKind>();

        Assert.Equal(
            ["ContractualFeeInstalment", "Correction", "OpeningBalance", "RecargoFrozen", "RentAccrual"],
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
}
